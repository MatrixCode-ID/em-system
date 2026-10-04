using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Membaca request approval untuk dilihat pemanggil: daftar, per dokumen, rincian beserta riwayatnya,
   /// dan potret yang digambar ke PDF.
   /// </summary>
   /// <remarks>
   /// Semua pemeriksaan hak lihat ada di sini, jadi action yang memanggilnya tidak bisa lupa. Satu
   /// instance dipakai untuk satu permintaan: ia memegang siapa pemanggilnya.
   /// </remarks>
   internal sealed class ApprovalRequestReader(ApiCoreContext ctx, ApprovalRegistry registry,
      ActionRequest caller, CancellationToken cancellationToken)
   {
      private const int MaxPageSize = 200;
      private const int MaxChainLength = 100;

      // A summary column name ends up inside a JSON path that the database evaluates, so it is
      // restricted to what a column title can sensibly be rather than escaped.
      private static readonly Regex SummaryColumnName = new(@"^[\p{L}\p{N} _\-]{1,64}$", RegexOptions.Compiled);

      private readonly string _me = caller.RequireUserId();

      // Whether the caller is one of the two built-in accounts, which can never take part in an approval:
      // nothing is ever waiting for them, however many claims they hold.
      private bool IsSystemAccount => _me is Defaults.AdminUserId or Defaults.DebuggerUserId;

      // The administrator switch and the developer path hold every claim, in the same way the gate treats them.
      private bool HoldsEverything => caller.IsDebugRequest || caller.IsAdmin;

      private string[] HeldClaimKeys => [.. caller.Claims.Select(r => r.Key)];

      #region Requests

      /// <summary>Daftar request yang boleh dilihat pemanggil, disaring dan dihalamankan di server.</summary>
      /// <param name="query">Penyaring, pengurut, dan halaman yang diminta.</param>
      public async Task<PagedResult<ApprovalRequestInfo>> ListAsync(ApprovalQuery query) {
         ArgumentNullException.ThrowIfNull(query);

         var page = Math.Max(query.Page, 1);
         var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
         var empty = new PagedResult<ApprovalRequestInfo> { Page = page, PageSize = pageSize };

         IReadOnlyList<string> docTypes;
         if (!string.IsNullOrWhiteSpace(query.DocType)) {
            var flow = registry.Get(query.DocType);
            ApprovalAccess.RequireView(caller, flow);
            docTypes = [flow.DocType];
         }
         else {
            docTypes = ApprovalAccess.ViewableDocTypes(caller, registry);
         }

         if (docTypes.Count == 0) return empty;

         var requests = ctx.ta_ApprovalRequests.Where(r => docTypes.Contains(r.cApprovalRequestDocType));

         if (!string.IsNullOrWhiteSpace(query.DocKey)) {
            var docKey = query.DocKey;
            requests = requests.Where(r => r.cApprovalRequestDocKey == docKey);
         }

         if (!string.IsNullOrWhiteSpace(query.DocVersion)) {
            var docVersion = query.DocVersion;
            requests = requests.Where(r => r.cApprovalRequestDocVersion == docVersion);
         }

         if (query.Stages.Length > 0) {
            var stages = query.Stages;
            requests = requests.Where(r => stages.Contains(r.cApprovalRequestStage));
         }

         if (query.WaitingForMeOnly) requests = requests.Where(WaitingForCaller(asSubstitute: false));
         if (query.CanSignAsSubstituteOnly) requests = requests.Where(WaitingForCaller(asSubstitute: true));

         var rows =
            from r in requests
            join u in ctx.ta_Users on r.cApprovalRequestRequesterId equals u.cUserId
            join c in ctx.ta_Contacts on u.cContactId equals c.cContactId
            select new RequestRow { Request = r, RequesterName = c.cContactFullName };

         if (!string.IsNullOrWhiteSpace(query.Search)) {
            var search = query.Search.Trim();
            rows = rows.Where(x =>
               x.Request.cApprovalRequestDocKey.Contains(search) ||
               x.RequesterName.Contains(search) ||
               (x.Request.json_object != null && x.Request.json_object.Contains(search)));
         }

         var total = await rows.CountAsync(cancellationToken);
         if (total == 0) return empty;

         var pageRows = await Order(rows, query).Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

         return new PagedResult<ApprovalRequestInfo> {
            Items = await ToInfosAsync(pageRows),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
         };
      }

      /// <summary>
      /// Seluruh request untuk satu dokumen, dari yang paling lama, termasuk yang sudah selesai, ditolak,
      /// dan ditarik kembali.
      /// </summary>
      /// <param name="docType">Jenis dokumennya.</param>
      /// <param name="docKey">Kunci dokumennya dalam bentuk kanonik.</param>
      public async Task<ApprovalRequestInfo[]> ByDocAsync(string docType, string docKey) {
         var flow = registry.Get(docType);
         ApprovalAccess.RequireView(caller, flow);

         var rows = await (
            from r in ctx.ta_ApprovalRequests
            where r.cApprovalRequestDocType == flow.DocType && r.cApprovalRequestDocKey == docKey
            join u in ctx.ta_Users on r.cApprovalRequestRequesterId equals u.cUserId
            join c in ctx.ta_Contacts on u.cContactId equals c.cContactId
            orderby r.cApprovalRequestDate, r.cApprovalRequestId
            select new RequestRow { Request = r, RequesterName = c.cContactFullName }).ToListAsync(cancellationToken);

         return await ToInfosAsync(rows);
      }

      /// <summary>Rincian satu request, atau <c>null</c> kalau tidak ada.</summary>
      /// <param name="approvalRequestId">Request yang diminta.</param>
      public async Task<ApprovalRequestDetail?> DetailAsync(string approvalRequestId) {
         var request = await FindRequestAsync(approvalRequestId);
         if (request is null) return null;

         var flow = registry.Get(request.cApprovalRequestDocType);
         ApprovalAccess.RequireView(caller, flow);

         var requester = await NamesAsync([request.cApprovalRequestRequesterId]);
         var row = new RequestRow(request, requester.GetValueOrDefault(request.cApprovalRequestRequesterId, string.Empty));
         var info = (await ToInfosAsync([row]))[0];

         var steps = await ctx.ta_ApprovalRequestSteps
            .Where(r => r.cApprovalRequestId == approvalRequestId)
            .OrderBy(r => r.cApprovalRequestStepLevel).ThenBy(r => r.cApprovalRequestStepOrder)
            .ToListAsync(cancellationToken);
         var signers = await SignersAsync([approvalRequestId]);

         var userIds = steps.SelectMany(r => new[] { r.cApprovalRequestStepSignerId, r.cApprovalRequestStepOnBehalfId })
            .Concat(signers.Select(r => r.cUserId))
            .OfType<string>();
         var names = await NamesAsync(userIds);

         return new ApprovalRequestDetail {
            Info = info,
            Steps = [.. steps.Select(step => ToStepInfo(request, flow, step, signers, names))],
            Changes = await ChangesAsync(request),
            Timeline = await TimelineAsync(request)
         };
      }

      /// <summary>
      /// Keadaan terbaru sebuah request sebagai baris daftar, atau <c>null</c> kalau tidak ada atau kalau
      /// pemanggil tidak boleh melihatnya.
      /// </summary>
      /// <param name="approvalRequestId">Request yang diminta.</param>
      /// <param name="viewProven">
      /// Hak melihatnya tidak perlu diperiksa lagi karena pemanggil sudah membuktikannya dengan
      /// keputusan yang berhasil disimpan; keputusan yang gagal tidak membuktikan apa pun.
      /// </param>
      public async Task<ApprovalRequestInfo?> InfoAsync(string approvalRequestId, bool viewProven = false) {
         var request = await FindRequestAsync(approvalRequestId);
         if (request is null) return null;

         if (!viewProven && !ApprovalAccess.CanView(caller, registry.Get(request.cApprovalRequestDocType))) return null;

         var requester = await NamesAsync([request.cApprovalRequestRequesterId]);
         var row = new RequestRow(request, requester.GetValueOrDefault(request.cApprovalRequestRequesterId, string.Empty));
         return (await ToInfosAsync([row]))[0];
      }

      /// <summary>
      /// Memuat sebuah request untuk dikerjakan action lain, setelah memastikan pemanggil boleh
      /// melihatnya.
      /// </summary>
      /// <param name="approvalRequestId">Request yang diminta.</param>
      /// <exception cref="ActionException">404 kalau request-nya tidak ada; 403 kalau tidak boleh dilihat.</exception>
      public async Task<(ta_ApprovalRequest Request, ApprovalFlowDeclaration Flow)> RequireViewableAsync(
         string approvalRequestId) {
         var request = await FindRequestAsync(approvalRequestId) ??
                       throw new ActionException($"Approval request '{approvalRequestId}' was not found.", 404);
         var flow = registry.Get(request.cApprovalRequestDocType);
         ApprovalAccess.RequireView(caller, flow);
         return (request, flow);
      }

      /// <summary>
      /// Potret keadaan sebuah request untuk digambar ke PDF-nya: langkah beserta kotaknya, isian yang
      /// sudah diberikan, dan kode di tepi halaman.
      /// </summary>
      /// <param name="request">Request yang digambar.</param>
      /// <param name="flow">Alur jenis dokumennya.</param>
      public async Task<ApprovalStampSnapshot> StampSnapshotAsync(ta_ApprovalRequest request,
         ApprovalFlowDeclaration flow) {
         var steps = await ctx.ta_ApprovalRequestSteps
            .Where(r => r.cApprovalRequestId == request.cApprovalRequestId)
            .OrderBy(r => r.cApprovalRequestStepLevel).ThenBy(r => r.cApprovalRequestStepOrder)
            .ToListAsync(cancellationToken);
         var names = await NamesAsync(steps.SelectMany(r => new[] { r.cApprovalRequestStepSignerId, r.cApprovalRequestStepOnBehalfId })
            .OfType<string>());

         var stampSteps = new List<ApprovalStampStep>();
         var stampInputs = new List<ApprovalStampInput>();

         foreach (var step in steps) {
            var snapshot = ApprovalStepSnapshot.Read(step.json_object);
            var decided = step.cApprovalRequestStepStage is ApprovalStepStatus.Approved or ApprovalStepStatus.Rejected;

            // A step without a box has nowhere to print its signature; it still shows on the approval
            // sheet when the document asks for one, and that sheet is built from the steps below.
            stampSteps.Add(new ApprovalStampStep {
               StepName = step.cApprovalRequestStepName,
               Slot = snapshot.Slot?.ToSlot() ?? new ApprovalSlot(0, 0, 0, 0, 0),
               Status = step.cApprovalRequestStepStage,
               SignerName = step.cApprovalRequestStepSignerId is { } signer ? names.GetValueOrDefault(signer) : null,
               OnBehalfName = step.cApprovalRequestStepOnBehalfId is { } onBehalf ? names.GetValueOrDefault(onBehalf) : null,
               SignerRole = step.cApprovalRequestStepSignerRole,
               SignedDate = step.cApprovalRequestStepSignedDate,
               VerificationCode = step.cApprovalRequestStepVerificationCode
            });

            if (!decided) continue;

            stampInputs.AddRange(snapshot.Fields.Select(field => new ApprovalStampInput {
               StepName = step.cApprovalRequestStepName,
               Kind = field.Kind,
               Slot = field.Slot.ToSlot(),
               Text = field.Text,
               Checked = field.Checked
            }));
         }

         var lastCode = steps
            .Where(r => r.cApprovalRequestStepVerificationCode is not null && r.cApprovalRequestStepSignedDate is not null)
            .OrderByDescending(r => r.cApprovalRequestStepSignedDate)
            .Select(r => r.cApprovalRequestStepVerificationCode)
            .FirstOrDefault();

         return new ApprovalStampSnapshot {
            DocumentTitle = $"{request.cApprovalRequestDocType} {DisplayKey(request.cApprovalRequestDocKey)}",
            Steps = stampSteps,
            Inputs = stampInputs,
            PageMarginCode = lastCode,
            IncludeApprovalSheet = flow.ApprovalSheet
         };
      }

      private async Task<ta_ApprovalRequest?> FindRequestAsync(string approvalRequestId) {
         if (string.IsNullOrWhiteSpace(approvalRequestId)) return null;

         return await ctx.ta_ApprovalRequests
            .Where(r => r.cApprovalRequestId == approvalRequestId)
            .FirstOrDefaultAsync(cancellationToken);
      }

      /// <summary>
      /// Request yang menunggu pemanggil, dikelompokkan per jenis approval dan jenis dokumen, dengan
      /// jumlah dan tanggal pengajuan tertuanya. Dihitung dari datanya setiap kali ditanya.
      /// </summary>
      public async Task<IReadOnlyList<ApprovalHubGroup>> HubGroupsAsync() {
         // A request whose document type no longer has a flow cannot be opened from the list either,
         // so it is left out here too instead of showing a task that leads nowhere.
         var docTypes = registry.DocTypes.ToArray();
         if (docTypes.Length == 0) return [];

         var groups = await ctx.ta_ApprovalRequests
            .Where(r => docTypes.Contains(r.cApprovalRequestDocType))
            .Where(WaitingForCaller(asSubstitute: false))
            .GroupBy(r => new { r.cApprovalRequestKind, r.cApprovalRequestDocType })
            .Select(g => new {
               g.Key.cApprovalRequestKind,
               g.Key.cApprovalRequestDocType,
               Count = g.Count(),
               Oldest = g.Min(r => r.cApprovalRequestDate)
            })
            .ToListAsync(cancellationToken);

         return [.. groups
            .OrderBy(r => r.cApprovalRequestKind).ThenBy(r => r.Oldest).ThenBy(r => r.cApprovalRequestDocType)
            .Select(r => new ApprovalHubGroup(r.cApprovalRequestKind, r.cApprovalRequestDocType, r.Count, r.Oldest))];
      }

      #endregion

      #region Mapping

      private async Task<ApprovalRequestInfo[]> ToInfosAsync(IReadOnlyList<RequestRow> rows) {
         if (rows.Count == 0) return [];

         var ids = rows.Select(r => r.Request.cApprovalRequestId).ToArray();

         var steps = await ctx.ta_ApprovalRequestSteps
            .Where(r => ids.Contains(r.cApprovalRequestId))
            .ToListAsync(cancellationToken);
         var stepsByRequest = steps.ToLookup(r => r.cApprovalRequestId);
         var signers = await SignersAsync(ids);

         var cancels = rows.ToDictionary(r => r.Request.cApprovalRequestId,
            r => ApprovalRequestJson.ReadCancel(r.Request.json_object));

         var names = await NamesAsync(steps.Select(r => r.cApprovalRequestStepSignerId).OfType<string>()
            .Concat(cancels.Values.OfType<ApprovalCancelRecord>().Select(r => r.UserId)));
         var reinstateCounts = await ReinstateCountsAsync(rows.Select(r => r.Request).ToList());

         var infos = new ApprovalRequestInfo[rows.Count];
         for (var i = 0; i < rows.Count; i++) {
            var request = rows[i].Request;
            var ownSteps = stepsByRequest[request.cApprovalRequestId].ToList();
            var flow = registry.Find(request.cApprovalRequestDocType);

            var waiting = false;
            var substitute = false;
            foreach (var step in ownSteps) {
               var (stepWaiting, stepSubstitute) = StepFlags(request, step, signers);
               waiting |= stepWaiting;
               substitute |= stepSubstitute;
            }

            var (lastAction, lastActor, lastDate) =
               LastAction(request, rows[i].RequesterName, ownSteps, cancels[request.cApprovalRequestId], names);

            infos[i] = new ApprovalRequestInfo {
               cApprovalRequestId = request.cApprovalRequestId,
               DocType = request.cApprovalRequestDocType,
               DocKey = request.cApprovalRequestDocKey,
               DocKeyDisplay = DisplayKey(request.cApprovalRequestDocKey),
               // A data request has no version of its own: the id stored in its place only keeps the
               // requests for one record apart, and is not something to show.
               DocVersion = request.cApprovalRequestKind == ApprovalKind.Data ? string.Empty : request.cApprovalRequestDocVersion,
               Kind = request.cApprovalRequestKind,
               Stage = request.cApprovalRequestStage,
               RequesterName = rows[i].RequesterName,
               RequestDate = request.cApprovalRequestDate,
               CompletedDate = request.cApprovalRequestCompletedDate,
               Level = request.cApprovalRequestLevel,
               WaitingSteps = string.Join(", ", ownSteps
                  .Where(r => r.cApprovalRequestStepStage == ApprovalStepStatus.Waiting &&
                              r.cApprovalRequestStepLevel == request.cApprovalRequestLevel &&
                              request.cApprovalRequestStage == ApprovalStage.Pending)
                  .OrderBy(r => r.cApprovalRequestStepOrder)
                  .Select(r => r.cApprovalRequestStepName)),
               SignedStepCount = ownSteps.Count(r => r.cApprovalRequestStepStage == ApprovalStepStatus.Approved),
               // A step skipped because its condition did not hold was never part of the flow for this
               // document, so it does not count towards the total.
               TotalStepCount = ownSteps.Count(r => r.cApprovalRequestStepStage != ApprovalStepStatus.Skipped),
               WaitingForMe = waiting,
               CanSignAsSubstitute = substitute,
               HasPdf = request.cApprovalRequestPdfKey is not null,
               ReinstateCount = reinstateCounts.GetValueOrDefault(request.cApprovalRequestId),
               RequireOpen = flow?.RequireOpen ?? false,
               Summary = ApprovalRequestJson.ReadSummary(request.json_object),
               LastAction = lastAction,
               LastActorName = lastActor,
               LastActionDate = lastDate
            };
         }

         return infos;
      }

      private ApprovalStepInfo ToStepInfo(ta_ApprovalRequest request, ApprovalFlowDeclaration flow,
         ta_ApprovalRequestStep step, IReadOnlyList<ta_ApprovalRequestStepSigner> signers,
         IReadOnlyDictionary<string, string> names) {
         var (waiting, substitute) = StepFlags(request, step, signers);

         return new ApprovalStepInfo {
            StepName = step.cApprovalRequestStepName,
            Level = step.cApprovalRequestStepLevel,
            Status = step.cApprovalRequestStepStage,
            SignerName = step.cApprovalRequestStepSignerId is { } signer ? names.GetValueOrDefault(signer) : null,
            OnBehalfName = step.cApprovalRequestStepOnBehalfId is { } onBehalf ? names.GetValueOrDefault(onBehalf) : null,
            SignerRole = step.cApprovalRequestStepSignerRole,
            SignedDate = step.cApprovalRequestStepSignedDate,
            Note = step.cApprovalRequestStepNote,
            VerificationCode = step.cApprovalRequestStepVerificationCode,
            AssignedSignerNames = [.. signers
               .Where(r => r.cApprovalRequestId == step.cApprovalRequestId &&
                           string.Equals(r.cApprovalRequestStepName, step.cApprovalRequestStepName, StringComparison.OrdinalIgnoreCase))
               .Select(r => names.GetValueOrDefault(r.cUserId, r.cUserId))
               .Order()],
            WaitingForMe = waiting,
            CanSignAsSubstitute = substitute,
            RequiresInput = flow.StepRequiresInput(step.cApprovalRequestStepName)
         };
      }

      // The in-memory form of the rule WaitingForCaller expresses for the database. They answer the
      // same question - one for a filter over many rows, one for the flags on the rows already loaded -
      // and have to change together.
      private (bool Waiting, bool Substitute) StepFlags(ta_ApprovalRequest request, ta_ApprovalRequestStep step,
         IReadOnlyList<ta_ApprovalRequestStepSigner> signers) {
         if (IsSystemAccount ||
             request.cApprovalRequestStage != ApprovalStage.Pending ||
             step.cApprovalRequestStepLevel != request.cApprovalRequestLevel ||
             step.cApprovalRequestStepStage != ApprovalStepStatus.Waiting) {
            return (false, false);
         }

         var holdsClaim = HoldsEverything ||
                          HeldClaimKeys.Contains(step.cApprovalRequestStepClaim, StringComparer.OrdinalIgnoreCase);
         if (!holdsClaim) return (false, false);

         var assigned = signers
            .Where(r => r.cApprovalRequestId == step.cApprovalRequestId &&
                        string.Equals(r.cApprovalRequestStepName, step.cApprovalRequestStepName, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.cUserId)
            .ToList();

         // No assigned signers means any holder of the claim signs as themselves.
         if (assigned.Count == 0) return (true, false);

         return assigned.Contains(_me) ? (true, false) : (false, true);
      }

      private static (string Action, string Actor, DateTime Date) LastAction(ta_ApprovalRequest request,
         string requesterName, IReadOnlyList<ta_ApprovalRequestStep> steps, ApprovalCancelRecord? cancel,
         IReadOnlyDictionary<string, string> names) {
         // Candidates are listed oldest kind first, so when two happen in the same instant - the
         // requester's own signature lands with the submission - the later kind wins the tie.
         var candidates = new List<(DateTime Date, string Action, string Actor)> {
            (request.cApprovalRequestDate, ApprovalTimelineAction.Submitted, requesterName)
         };

         foreach (var step in steps.Where(r => r.cApprovalRequestStepSignedDate is not null)
                     .OrderBy(r => r.cApprovalRequestStepSignedDate).ThenBy(r => r.cApprovalRequestStepLevel)) {
            candidates.Add((step.cApprovalRequestStepSignedDate!.Value, DecisionAction(step),
               step.cApprovalRequestStepSignerId is { } signer ? names.GetValueOrDefault(signer, string.Empty) : string.Empty));
         }

         if (cancel is not null) {
            candidates.Add((cancel.Date,
               request.cApprovalRequestStage == ApprovalStage.ReinstatedAfterFinish
                  ? ApprovalTimelineAction.ReinstatedAfterFinish
                  : ApprovalTimelineAction.Cancelled,
               names.GetValueOrDefault(cancel.UserId, string.Empty)));
         }

         var last = candidates.Select((r, index) => (r, index)).OrderBy(r => r.r.Date).ThenBy(r => r.index).Last().r;
         return (last.Action, last.Actor, last.Date);
      }

      private static string DecisionAction(ta_ApprovalRequestStep step) {
         if (step.cApprovalRequestStepStage == ApprovalStepStatus.Rejected) return ApprovalTimelineAction.Rejected;

         return step.cApprovalRequestStepSignerRole switch {
            ApprovalSignerRole.Substitute => ApprovalTimelineAction.ApprovedAsSubstitute,
            ApprovalSignerRole.Override => ApprovalTimelineAction.ApprovedWithOverride,
            _ => ApprovalTimelineAction.Approved
         };
      }

      private static string DisplayKey(string canonicalKey) {
         try {
            return ApprovalKey.ToDisplay(canonicalKey);
         }
         catch (InvalidOperationException) {
            // A key that is not in the canonical form still has to show up in the list.
            return canonicalKey;
         }
      }

      #endregion

      #region Changes and timeline

      private async Task<ApprovalConflictField[]> ChangesAsync(ta_ApprovalRequest request) {
         if (request.cApprovalRequestKind != ApprovalKind.Data) return [];

         var items = await ctx.ta_ApprovalRequestItems
            .Where(r => r.cApprovalRequestId == request.cApprovalRequestId)
            .OrderBy(r => r.cApprovalRequestItemOrder)
            .ToListAsync(cancellationToken);
         if (items.Count == 0) return [];

         var itemIds = items.Select(r => r.cApprovalRequestItemId).ToArray();
         var fields = await ctx.ta_ApprovalRequestItemFields
            .Where(r => itemIds.Contains(r.cApprovalRequestItemId))
            .ToListAsync(cancellationToken);

         return [.. items.SelectMany(item => {
            var own = fields.Where(f => f.cApprovalRequestItemId == item.cApprovalRequestItemId)
               .OrderBy(f => f.cApprovalRequestItemFieldOrder).ToList();

            // An entity proposed without columns - a deletion, a reinstatement - would not show up at
            // all if only columns were listed, so it is listed by what is asked of it.
            if (own.Count == 0) {
               return new[] {
                  new ApprovalConflictField {
                     Entity = item.cApprovalRequestItemEntity,
                     EntityKey = DisplayKey(item.cApprovalRequestItemKey),
                     FieldName = item.cApprovalRequestItemOperation.ToString()
                  }
               };
            }

            return own.Select(f => new ApprovalConflictField {
               Entity = item.cApprovalRequestItemEntity,
               EntityKey = DisplayKey(item.cApprovalRequestItemKey),
               FieldName = f.cApprovalRequestItemFieldName,
               OldValue = f.cApprovalRequestItemFieldOldValue,
               CurrentValue = f.cApprovalRequestItemFieldCurrentValue,
               NewValue = f.cApprovalRequestItemFieldNewValue
            });
         })];
      }

      // The history of a document is the history of every request that is a resubmission of another,
      // in both directions: the request opened may be the first of a chain or the fourth.
      private async Task<ApprovalTimelineEntry[]> TimelineAsync(ta_ApprovalRequest start) {
         var chainIds = new HashSet<string> { start.cApprovalRequestId };
         var frontier = new[] { start.cApprovalRequestId };

         for (var depth = 0; frontier.Length > 0 && depth < MaxChainLength; depth++) {
            var current = frontier;
            var neighbours = await ctx.ta_ApprovalRequests
               .Where(r => current.Contains(r.cApprovalRequestId) ||
                           (r.cApprovalRequestReinstateOf != null && current.Contains(r.cApprovalRequestReinstateOf)))
               .Select(r => new { r.cApprovalRequestId, r.cApprovalRequestReinstateOf })
               .ToListAsync(cancellationToken);

            frontier = [.. neighbours
               .SelectMany(r => new[] { r.cApprovalRequestId, r.cApprovalRequestReinstateOf })
               .OfType<string>()
               .Where(chainIds.Add)];
         }

         var chain = chainIds.ToArray();
         var requests = await (
            from r in ctx.ta_ApprovalRequests
            where chain.Contains(r.cApprovalRequestId)
            join u in ctx.ta_Users on r.cApprovalRequestRequesterId equals u.cUserId
            join c in ctx.ta_Contacts on u.cContactId equals c.cContactId
            select new RequestRow { Request = r, RequesterName = c.cContactFullName }).ToListAsync(cancellationToken);

         var steps = await ctx.ta_ApprovalRequestSteps
            .Where(r => chain.Contains(r.cApprovalRequestId) && r.cApprovalRequestStepSignedDate != null)
            .ToListAsync(cancellationToken);
         var comments = await ctx.ta_ApprovalRequestComments
            .Where(r => chain.Contains(r.cApprovalRequestId))
            .ToListAsync(cancellationToken);

         var cancels = requests.ToDictionary(r => r.Request.cApprovalRequestId,
            r => ApprovalRequestJson.ReadCancel(r.Request.json_object));
         var names = await NamesAsync(steps.Select(r => r.cApprovalRequestStepSignerId).OfType<string>()
            .Concat(comments.Select(r => r.cUserId))
            .Concat(cancels.Values.OfType<ApprovalCancelRecord>().Select(r => r.UserId)));

         var entries = new List<ApprovalTimelineEntry>();

         foreach (var row in requests) {
            var request = row.Request;

            entries.Add(new ApprovalTimelineEntry {
               Date = request.cApprovalRequestDate,
               ActorName = row.RequesterName,
               Action = ApprovalTimelineAction.Submitted,
               Note = request.cApprovalRequestNote,
               cApprovalRequestId = request.cApprovalRequestId
            });

            if (cancels[request.cApprovalRequestId] is { } cancel) {
               entries.Add(new ApprovalTimelineEntry {
                  Date = cancel.Date,
                  ActorName = names.GetValueOrDefault(cancel.UserId, string.Empty),
                  Action = request.cApprovalRequestStage == ApprovalStage.ReinstatedAfterFinish
                     ? ApprovalTimelineAction.ReinstatedAfterFinish
                     : ApprovalTimelineAction.Cancelled,
                  Note = cancel.Reason,
                  cApprovalRequestId = request.cApprovalRequestId
               });
            }
         }

         entries.AddRange(steps.Select(step => new ApprovalTimelineEntry {
            Date = step.cApprovalRequestStepSignedDate!.Value,
            ActorName = step.cApprovalRequestStepSignerId is { } signer ? names.GetValueOrDefault(signer, string.Empty) : string.Empty,
            Action = DecisionAction(step),
            StepName = step.cApprovalRequestStepName,
            Note = step.cApprovalRequestStepNote,
            cApprovalRequestId = step.cApprovalRequestId
         }));

         entries.AddRange(comments.Select(comment => new ApprovalTimelineEntry {
            Date = comment.cApprovalRequestCommentDate,
            ActorName = names.GetValueOrDefault(comment.cUserId, string.Empty),
            Action = ApprovalTimelineAction.Commented,
            Note = comment.cApprovalRequestCommentNote,
            cApprovalRequestId = comment.cApprovalRequestId
         }));

         // The order of the entries as built is the tie-breaker: a submission comes before the
         // signature the requester puts on it in the same instant.
         return [.. entries.Select((r, index) => (r, index)).OrderBy(r => r.r.Date).ThenBy(r => r.index).Select(r => r.r)];
      }

      #endregion

      #region Queries

      // Whether a request waits for the caller, as a condition the database can evaluate. A request
      // waits for someone when it is pending and some step at the running level is still open, the
      // person holds that step's claim, and either nobody was assigned to the step or they were.
      // The substitute form is the same except that signers were assigned and the caller is not one.
      // StepFlags is the in-memory form of this rule.
      private Expression<Func<ta_ApprovalRequest, bool>> WaitingForCaller(bool asSubstitute) {
         if (IsSystemAccount) return r => false;

         var me = _me;
         var all = HoldsEverything;
         var claims = HeldClaimKeys;

         if (!asSubstitute) {
            return r => r.cApprovalRequestStage == ApprovalStage.Pending &&
               ctx.ta_ApprovalRequestSteps.Any(s =>
                  s.cApprovalRequestId == r.cApprovalRequestId &&
                  s.cApprovalRequestStepLevel == r.cApprovalRequestLevel &&
                  s.cApprovalRequestStepStage == ApprovalStepStatus.Waiting &&
                  (all || claims.Contains(s.cApprovalRequestStepClaim)) &&
                  (!ctx.ta_ApprovalRequestStepSigners.Any(g =>
                        g.cApprovalRequestId == s.cApprovalRequestId &&
                        g.cApprovalRequestStepName == s.cApprovalRequestStepName) ||
                   ctx.ta_ApprovalRequestStepSigners.Any(g =>
                      g.cApprovalRequestId == s.cApprovalRequestId &&
                      g.cApprovalRequestStepName == s.cApprovalRequestStepName &&
                      g.cUserId == me)));
         }

         return r => r.cApprovalRequestStage == ApprovalStage.Pending &&
            ctx.ta_ApprovalRequestSteps.Any(s =>
               s.cApprovalRequestId == r.cApprovalRequestId &&
               s.cApprovalRequestStepLevel == r.cApprovalRequestLevel &&
               s.cApprovalRequestStepStage == ApprovalStepStatus.Waiting &&
               (all || claims.Contains(s.cApprovalRequestStepClaim)) &&
               ctx.ta_ApprovalRequestStepSigners.Any(g =>
                  g.cApprovalRequestId == s.cApprovalRequestId &&
                  g.cApprovalRequestStepName == s.cApprovalRequestStepName) &&
               !ctx.ta_ApprovalRequestStepSigners.Any(g =>
                  g.cApprovalRequestId == s.cApprovalRequestId &&
                  g.cApprovalRequestStepName == s.cApprovalRequestStepName &&
                  g.cUserId == me));
      }

      private static IQueryable<RequestRow> Order(IQueryable<RequestRow> rows, ApprovalQuery query) {
         var descending = query.SortDescending;

         // Without a column the order is the one the screen promises: whoever has waited longest first.
         IOrderedQueryable<RequestRow> ordered = query.SortBy?.Trim() switch {
            null or "" or nameof(ApprovalRequestInfo.RequestDate) =>
               By(rows, r => r.Request.cApprovalRequestDate, descending),
            nameof(ApprovalRequestInfo.DocType) => By(rows, r => r.Request.cApprovalRequestDocType, descending),
            nameof(ApprovalRequestInfo.DocKey) or nameof(ApprovalRequestInfo.DocKeyDisplay) =>
               By(rows, r => r.Request.cApprovalRequestDocKey, descending),
            nameof(ApprovalRequestInfo.DocVersion) => By(rows, r => r.Request.cApprovalRequestDocVersion, descending),
            nameof(ApprovalRequestInfo.Kind) => By(rows, r => r.Request.cApprovalRequestKind, descending),
            nameof(ApprovalRequestInfo.Stage) => By(rows, r => r.Request.cApprovalRequestStage, descending),
            nameof(ApprovalRequestInfo.Level) => By(rows, r => r.Request.cApprovalRequestLevel, descending),
            nameof(ApprovalRequestInfo.RequesterName) => By(rows, r => r.RequesterName, descending),
            nameof(ApprovalRequestInfo.CompletedDate) => By(rows, r => r.Request.cApprovalRequestCompletedDate, descending),
            var column => ByColumn(rows, column, descending)
         };

         return ordered.ThenBy(r => r.Request.cApprovalRequestId);
      }

      // Anything that is not a column of the list itself is a column the module declared in its summary.
      private static IOrderedQueryable<RequestRow> ByColumn(IQueryable<RequestRow> rows, string column, bool descending) {
         if (!SummaryColumnName.IsMatch(column)) {
            throw new ActionException($"'{column}' is not a column that can be sorted on.", 400);
         }

         var path = $"$.\"{column}\"";
         return By(rows, r => ApprovalJsonFunctions.JsonValue(r.Request.json_object, path), descending);
      }

      private static IOrderedQueryable<RequestRow> By<TKey>(IQueryable<RequestRow> rows,
         Expression<Func<RequestRow, TKey>> key, bool descending) =>
         descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

      private async Task<List<ta_ApprovalRequestStepSigner>> SignersAsync(IReadOnlyCollection<string> requestIds) {
         var ids = requestIds.ToArray();
         return await ctx.ta_ApprovalRequestStepSigners
            .Where(r => ids.Contains(r.cApprovalRequestId))
            .ToListAsync(cancellationToken);
      }

      private async Task<Dictionary<string, string>> NamesAsync(IEnumerable<string> userIds) {
         var ids = userIds.Distinct().ToArray();
         if (ids.Length == 0) return [];

         return await (
            from u in ctx.ta_Users
            where ids.Contains(u.cUserId)
            join c in ctx.ta_Contacts on u.cContactId equals c.cContactId
            select new { u.cUserId, c.cContactFullName })
            .ToDictionaryAsync(r => r.cUserId, r => r.cContactFullName, cancellationToken);
      }

      // How many times a request is a resubmission: the length of the chain behind it.
      private async Task<Dictionary<string, int>> ReinstateCountsAsync(IReadOnlyList<ta_ApprovalRequest> requests) {
         var counts = requests.ToDictionary(r => r.cApprovalRequestId, _ => 0);

         // Each pending entry is "the request being counted" -> "the request it is a resubmission of".
         var pending = requests.Where(r => r.cApprovalRequestReinstateOf is not null)
            .ToDictionary(r => r.cApprovalRequestId, r => r.cApprovalRequestReinstateOf!);

         for (var depth = 0; pending.Count > 0 && depth < MaxChainLength; depth++) {
            var parentIds = pending.Values.Distinct().ToArray();
            var parents = await ctx.ta_ApprovalRequests
               .Where(r => parentIds.Contains(r.cApprovalRequestId))
               .Select(r => new { r.cApprovalRequestId, r.cApprovalRequestReinstateOf })
               .ToDictionaryAsync(r => r.cApprovalRequestId, r => r.cApprovalRequestReinstateOf, cancellationToken);

            var next = new Dictionary<string, string>();
            foreach (var (origin, parentId) in pending) {
               counts[origin]++;
               if (parents.GetValueOrDefault(parentId) is { } grandparent) next[origin] = grandparent;
            }

            pending = next;
         }

         return counts;
      }

      #endregion

      // A class with initializers rather than a positional record: the database translates a sort on a
      // member of an object it builds in the query only when the object is built by member
      // initialization, not by a constructor.
      private sealed class RequestRow
      {
         public RequestRow() { }

         public RequestRow(ta_ApprovalRequest request, string requesterName) {
            Request = request;
            RequesterName = requesterName;
         }

         public ta_ApprovalRequest Request { get; set; } = null!;

         public string RequesterName { get; set; } = string.Empty;
      }
   }
}
