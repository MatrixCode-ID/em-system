using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Em.Api.Core.Models;
using Em.Api.Core.Storage;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Implementasi <see cref="IApprovalEngine"/>: pintu masuk modul ke engine approval.
   /// </summary>
   /// <remarks>
   /// Turunan <see cref="ServicesBase"/> bukan karena ia action, tetapi karena ia yang menyerahkan giliran
   /// ke handler modul, dan handler itu perlu tahu siapa pemanggilnya. Keterangan itu diisi di konstruktor
   /// dari request yang sedang berjalan, sama seperti yang diisi gerbang pada service action.
   /// </remarks>
   internal sealed partial class ApprovalEngine : ServicesBase, IApprovalEngine
   {
      private const int MaxNoteLength = 500;

      private readonly ApiCoreContext _ctx;
      private readonly ApprovalRegistry _registry;

      public ApprovalEngine(ApiCoreContext ctx, ApprovalRegistry registry, EmApp app,
         IHttpContextAccessor httpContextAccessor, ActionRequest request, ILogger<ApprovalEngine> logger) {
         _ctx = ctx;
         _registry = registry;

         App = app;
         HttpContext = httpContextAccessor.HttpContext!;
         Request = request;
         Logger = logger;
      }

      #region Submit

      /// <inheritdoc />
      public async Task<string> SubmitAsync<TKey>(string docType, TKey docKey, string docVersion, string? note = null)
         where TKey : notnull {
         ArgumentNullException.ThrowIfNull(docKey);
         ArgumentException.ThrowIfNullOrWhiteSpace(docVersion);

         var flow = _registry.Get(docType);
         if (flow.Kind != ApprovalKind.Document) {
            throw new InvalidOperationException(
               $"Document type '{flow.DocType}' is a data approval, so it is submitted with SubmitDataAsync.");
         }

         if (flow.KeyType != typeof(TKey)) {
            throw new InvalidOperationException(
               $"Document type '{flow.DocType}' is keyed by {flow.KeyType?.Name}, but the key given is a {typeof(TKey).Name}.");
         }

         note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
         if (note is { Length: > MaxNoteLength }) {
            throw new ActionException($"The note is {note.Length} characters long; the limit is {MaxNoteLength}.", 400);
         }

         // 1. Who is asking: a real, active user. The two built-in accounts are refused here, with an
         //    answer that says why, instead of by the database later.
         var requesterId = await new ApprovalAccess(_ctx).RequireRealUserAsync(Request);

         var canonicalKey = ApprovalKey.ToCanonical(docKey);
         var requestId = $"{Ulid.NewUlid()}";
         var now = DateTime.Now;
         var stamp = DateTime.UtcNow;

         // The row exists in memory before anything is written: the handlers of the module are given the
         // request id and the key of it from the start, so they have to be built from something.
         var request = new ta_ApprovalRequest {
            cApprovalRequestId = requestId,
            cApprovalRequestKind = ApprovalKind.Document,
            cApprovalRequestDocType = flow.DocType,
            cApprovalRequestDocKey = canonicalKey,
            cApprovalRequestDocVersion = docVersion,
            cApprovalRequestRequesterId = requesterId,
            cApprovalRequestStage = ApprovalStage.Pending,
            cApprovalRequestNote = note,
            cApprovalRequestDate = now,
            ustamp = stamp,
            datestamp = stamp
         };
         var scope = new ApprovalRunScope(this, App.ServiceProvider, new ApprovalUserLookup(_ctx, CancellationToken.None),
            request);

         // 2. The steps that apply to this document, and the claim of the first one.
         var plan = await flow.PlanStepsAsync(scope);
         var active = plan.Where(r => r.Applies).ToList();
         if (active.Count == 0) {
            throw new ActionException(
               $"No step of the approval flow for '{flow.DocType}' applies to this document, so there is nobody to approve it.", 409);
         }

         var first = active[0];
         if (!ApprovalAccess.HoldsClaim(Request, first.Claim)) {
            throw new ActionException(
               $"You are not allowed to submit {flow.DocType} for approval: it needs the claim '{first.Claim.Name}', which signs its first step.", 403);
         }

         if (first.RequiresInput) {
            throw new InvalidOperationException(
               $"Step '{first.Name}' of the approval flow for '{flow.DocType}' is signed automatically by the requester, so it cannot ask for input.");
         }

         // 3. One pending request per document and version. The index on the table settles two submissions
         //    that race; this check is what gives the second one a readable answer in the ordinary case.
         if (await PendingExistsAsync(flow.DocType, canonicalKey, docVersion)) {
            throw new ActionException(AlreadyPendingMessage(flow.DocType, canonicalKey, docVersion), 409);
         }

         // A document that was pulled back and is submitted again - same key, same version - continues the
         // history of the request it replaces. Only the latest request counts: one that was rejected or
         // approved since then means this is not a resubmission of the pulled-back one.
         var latest = await _ctx.ta_ApprovalRequests
            .Where(r => r.cApprovalRequestDocType == flow.DocType && r.cApprovalRequestDocKey == canonicalKey &&
                        r.cApprovalRequestDocVersion == docVersion)
            .OrderByDescending(r => r.cApprovalRequestDate).ThenByDescending(r => r.cApprovalRequestId)
            .Select(r => new { r.cApprovalRequestId, r.cApprovalRequestStage })
            .FirstOrDefaultAsync();
         if (latest is { cApprovalRequestStage: ApprovalStage.Cancelled or ApprovalStage.ReinstatedAfterFinish }) {
            request.cApprovalRequestReinstateOf = latest.cApprovalRequestId;
         }

         // 4. Every signer is decided now, not when somebody is about to sign: a problem found here lands
         //    on the requester, who can fix it, instead of stopping somebody else's decision halfway.
         await ResolveSignersAsync(flow, scope, active, first, requesterId);

         // 5. The summary columns, validated before anything is written.
         var summary = await flow.SummaryAsync(scope);
         var json = ApprovalRequestJson.Compose(summary, null);

         // 6. The PDF is stored outside the transaction: it can take minutes to make, and a transaction
         //    held that long would hold locks that long. A failure below deletes it again.
         string? pdfKey = null;
         IBinaryStorage? storage = null;
         if (flow.HasPdf) {
            storage = GetService<IBinaryStorage>() ??
                      throw new ActionException("Binary storage is not configured, so the PDF of the document cannot be kept.", 501);
            pdfKey = BinaryStorageKey.Combine("approval",
               now.ToString("yyyy", CultureInfo.InvariantCulture), now.ToString("MM", CultureInfo.InvariantCulture),
               requestId, $"v{StorageSegment(docVersion)}.pdf");

            await using var pdf = await flow.OpenPdfAsync(scope) ??
                                  throw new InvalidOperationException($"The PDF delegate of '{flow.DocType}' returned no stream.");

            // The boxes are frozen with the PDF they were found on, so a layout that moves with the
            // content (rows, pages) is read from this very copy before it is stored.
            await using var buffer = new MemoryStream();
            await pdf.CopyToAsync(buffer);
            buffer.Position = 0;
            await flow.LocateSlotsAsync(scope, plan, buffer);
            buffer.Position = 0;
            await storage.PutAsync(pdfKey, buffer);
         }

         request.cApprovalRequestPdfKey = pdfKey;
         request.json_object = json;
         request.cApprovalRequestLevel = active.Min(r => r.Level);

         // 7. One transaction: the header, every step with its frozen boxes, the signers, the signature
         //    of the requester on the first step, and the move to the next level.
         bool finished;
         await using var transaction = await ApprovalTransaction.BeginAsync(_ctx, flow, App.ServiceProvider);
         try {
            // The header goes first on its own: the rows below point at it, the database enforces that,
            // and EF is not told about the foreign key, so it would not order them by itself.
            _ctx.ta_ApprovalRequests.Add(request);
            await _ctx.SaveChangesAsync();

            var steps = new List<ta_ApprovalRequestStep>();
            foreach (var planned in plan) {
               steps.Add(new ta_ApprovalRequestStep {
                  cApprovalRequestStepId = $"{Ulid.NewUlid()}",
                  cApprovalRequestId = requestId,
                  cApprovalRequestStepName = planned.Name,
                  cApprovalRequestStepClaim = planned.Claim.Key,
                  cApprovalRequestStepLevel = planned.Level,
                  cApprovalRequestStepOrder = planned.Order,
                  cApprovalRequestStepStage = planned.Applies ? ApprovalStepStatus.Waiting : ApprovalStepStatus.Skipped,
                  ustamp = stamp,
                  datestamp = stamp,
                  json_object = planned.Snapshot.Slot is null && planned.Snapshot.Fields.Count == 0
                     ? null
                     : planned.Snapshot.Write()
               });
            }

            var firstStep = steps.First(r => string.Equals(r.cApprovalRequestStepName, first.Name, StringComparison.OrdinalIgnoreCase));
            firstStep.cApprovalRequestStepStage = ApprovalStepStatus.Approved;
            firstStep.cApprovalRequestStepSignerId = requesterId;
            firstStep.cApprovalRequestStepSignedDate = now;
            firstStep.cApprovalRequestStepSignerRole = ApprovalSignerRole.Assigned;
            firstStep.cApprovalRequestStepVerificationCode = await ApprovalVerificationCode.CreateUnusedAsync(_ctx);

            _ctx.ta_ApprovalRequestSteps.AddRange(steps);
            _ctx.ta_ApprovalRequestStepSigners.AddRange(active
               .Where(r => r.Signers is not null)
               .SelectMany(r => r.Signers!.Select(userId => new ta_ApprovalRequestStepSigner {
                  cApprovalRequestId = requestId,
                  cApprovalRequestStepName = r.Name,
                  cUserId = userId
               })));
            await _ctx.SaveChangesAsync();

            finished = await ApprovalProgress.AdvanceAsync(_ctx, flow, scope, request, steps);
            await transaction.CommitAsync();
         }
         catch (Exception ex) {
            await transaction.RollbackAsync();
            _ctx.ChangeTracker.Clear();

            if (storage is not null && pdfKey is not null) {
               await DeleteQuietlyAsync(storage, pdfKey);
            }

            // Two submissions that raced: the loser reads the same as one that was told no up front.
            if (ex is DbUpdateException && await PendingExistsAsync(flow.DocType, canonicalKey, docVersion)) {
               throw new ActionException(AlreadyPendingMessage(flow.DocType, canonicalKey, docVersion), 409);
            }

            throw;
         }

         // Everything below happens after the request is safely stored, so none of it can undo it.
         await RunAfterCommitAsync(flow.RunSubmittedAsync(scope), "OnSubmitted", flow.DocType, requestId);
         if (finished) {
            await RunAfterCommitAsync(flow.RunFinishedAsync(scope), "OnFinished", flow.DocType, requestId);
         }

         return requestId;
      }

      // Fills in the signers of every step that applies and refuses what cannot be signed. The first
      // step is a special case twice over: the requester signs it, so the list the module names for it
      // has to include the requester, and for the four-eyes rule it counts as signed by the requester
      // even when the module named nobody.
      private async Task ResolveSignersAsync(ApprovalFlowDeclaration flow, ApprovalRunScope scope,
         IReadOnlyList<ApprovalPlannedStep> active, ApprovalPlannedStep first, string requesterId) {
         foreach (var step in active.Where(r => r.DeclaresSigners)) {
            var signers = await flow.ResolveSignersAsync(scope, step.Name) ?? [];
            step.Signers = [.. signers.Distinct(StringComparer.OrdinalIgnoreCase)];

            if (step.Signers.Count == 0) {
               throw new ActionException(
                  $"The document does not name anybody to sign step '{step.Name}', so the request cannot be submitted.", 409);
            }
         }

         var named = active.Where(r => r.Signers is not null).SelectMany(r => r.Signers!).Distinct().ToArray();
         if (named.Length > 0) {
            var activeIds = await _ctx.ta_Users
               .Where(r => named.Contains(r.cUserId) && r.cUserState == UserState.Active)
               .Select(r => r.cUserId)
               .ToListAsync();

            var inactive = named.Where(r => !activeIds.Contains(r, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (inactive.Length > 0) {
               throw new ActionException(
                  $"The document names {string.Join(", ", inactive)} to sign, but that is not an active user.", 409);
            }
         }

         if (first.Signers is not null && !first.Signers.Contains(requesterId, StringComparer.OrdinalIgnoreCase)) {
            throw new ActionException(
               $"The document names other people to sign step '{first.Name}', so only they can submit it.", 403);
         }

         // Four-eyes against the lists as they stand. A step without a list is open to every holder of its
         // claim, so there is nobody to compare until somebody signs - that part is checked at signing.
         IReadOnlyList<string>? Effective(ApprovalPlannedStep step) =>
            step.Signers ?? (ReferenceEquals(step, first) ? [requesterId] : null);

         foreach (var step in active.Where(r => r.DistinctFrom is not null)) {
            var other = active.FirstOrDefault(r => string.Equals(r.Name, step.DistinctFrom, StringComparison.OrdinalIgnoreCase));
            if (other is null) continue;

            var mine = Effective(step);
            var theirs = Effective(other);
            if (mine is null || theirs is null) continue;

            var shared = mine.Intersect(theirs, StringComparer.OrdinalIgnoreCase).ToArray();
            if (shared.Length == 0) continue;

            // A user with the administrator switch on may sign both: the switch counts as holding every
            // claim, and the rule is not meant to be stricter than that.
            var admins = await _ctx.ta_Users
               .Where(r => shared.Contains(r.cUserId) && r.cUserIsAdmin)
               .Select(r => r.cUserId)
               .ToListAsync();

            if (shared.Except(admins, StringComparer.OrdinalIgnoreCase).Any()) {
               throw new ActionException(
                  $"Step '{step.Name}' and step '{other.Name}' have to be signed by different people, but the same person would sign both.", 409);
            }
         }
      }

      private async Task RunAfterCommitAsync(Task hook, string name, string docType, string requestId) {
         try {
            await hook;
         }
         catch (Exception ex) {
            // The request is stored; a hook that fails after that has nothing left to roll back.
            Logger.LogError(ex, "Hook {Hook} of approval request {RequestId} ({DocType}) failed after the request was stored.",
               name, requestId, docType);
         }
      }

      private async Task DeleteQuietlyAsync(IBinaryStorage storage, string key) {
         try {
            await storage.DeleteAsync(key);
         }
         catch (Exception ex) {
            Logger.LogWarning(ex, "The PDF {Key} of a submission that failed could not be deleted.", key);
         }
      }

      // A version is free text, but a storage key part is not: anything outside what a key allows
      // becomes an underscore. The version is kept on the request itself, so nothing is lost by this.
      private static string StorageSegment(string value) {
         var segment = new string([.. value.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_')]);
         return segment.Length > 100 ? segment[..100] : segment;
      }

      #endregion

      #region Lock

      /// <inheritdoc />
      public async Task EnsureNotInApprovalAsync<TKey>(string docType, TKey docKey, string? docVersion = null)
         where TKey : notnull {
         ArgumentNullException.ThrowIfNull(docKey);

         var flow = _registry.Get(docType);
         if (flow.KeyType is not null && flow.KeyType != typeof(TKey)) {
            throw new InvalidOperationException(
               $"Document type '{flow.DocType}' is keyed by {flow.KeyType.Name}, but the key given is a {typeof(TKey).Name}.");
         }

         var canonicalKey = ApprovalKey.ToCanonical(docKey);
         var pending = _ctx.ta_ApprovalRequests.Where(r =>
            r.cApprovalRequestDocType == flow.DocType &&
            r.cApprovalRequestDocKey == canonicalKey &&
            r.cApprovalRequestStage == ApprovalStage.Pending);

         if (docVersion is not null) {
            pending = pending.Where(r => r.cApprovalRequestDocVersion == docVersion);
         }

         var found = await pending
            .OrderBy(r => r.cApprovalRequestDate)
            .Select(r => new { r.cApprovalRequestDocVersion, r.cApprovalRequestDate })
            .FirstOrDefaultAsync();
         if (found is null) return;

         throw new ActionException(
            $"{flow.DocType} {ApprovalKey.ToDisplay(canonicalKey)} (version {found.cApprovalRequestDocVersion}) is waiting for approval " +
            $"since {found.cApprovalRequestDate:yyyy-MM-dd HH:mm}, so it cannot be changed. Pull back the approval request first.", 409);
      }

      private Task<bool> PendingExistsAsync(string docType, string canonicalKey, string docVersion) =>
         _ctx.ta_ApprovalRequests.AnyAsync(r =>
            r.cApprovalRequestDocType == docType &&
            r.cApprovalRequestDocKey == canonicalKey &&
            r.cApprovalRequestDocVersion == docVersion &&
            r.cApprovalRequestStage == ApprovalStage.Pending);

      private static string AlreadyPendingMessage(string docType, string canonicalKey, string docVersion) =>
         $"{docType} {ApprovalKey.ToDisplay(canonicalKey)} (version {docVersion}) is already waiting for approval.";

      #endregion

      #region Reinstate

      /// <inheritdoc />
      public Task ReinstateAsync(string approvalRequestId, string reason) =>
         new ApprovalCanceller(_ctx, _registry, this).CancelAsync(approvalRequestId, reason);

      #endregion
   }
}
