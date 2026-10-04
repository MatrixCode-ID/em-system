using Em.Api.Core.Hub;
using Em.Api.Core.Models;
using Em.Api.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Sisi server approval: melihat request, memutuskannya, menarik kembali, berkomentar, mengambil
   /// PDF-nya, dan mengumpulkan daftar pekerjaan user aktif.
   /// </summary>
   /// <remarks>
   /// Satu service untuk semua jenis dokumen - itulah sebabnya keputusan bukan action modul. Yang
   /// khusus per jenis dokumen datang dari deklarasi alur milik modul, bukan dari action tersendiri.
   /// <para>
   /// Haknya diperiksa di dalam setiap action, bukan lewat claim pada atributnya: claim yang berlaku
   /// bergantung pada jenis dokumen yang sedang dilihat, dan jenis itu baru diketahui setelah
   /// request-nya dibaca. Karena itu service ini berdiri di luar default "claim apa pun di modul ini".
   /// </para>
   /// </remarks>
   [Module(Defaults.ApprovalModuleName)]
   public class ApprovalServices(ApiCoreContext ctx, ApprovalRegistry registry) : ServicesBase, IApprovalServices
   {
      // Rendering a document can take minutes - a report engine is on the other side of it - so the
      // two actions that produce a PDF get the same long limit a report action gets.
      private const int PdfTimeoutSecond = 360;

      // The width of the comment column.
      private const int MaxCommentLength = 1000;

      // Built per call, not per instance: the caller and the abort token are filled in by the gate after
      // this service has been created.
      private ApprovalRequestReader Reader => new(ctx, registry, Request, AbortToken);

      #region Meta's

      /// <inheritdoc />
      [GetAction]
      public Task<string[]> GetMeta_ApprovalDocumentTypes() {
         Request.RequireUserId();
         return Task.FromResult(ApprovalAccess.ViewableDocTypes(Request, registry).ToArray());
      }

      /// <inheritdoc />
      [GetAction]
      public Task<PagedResult<ApprovalRequestInfo>> GetMeta_ApprovalRequests(ApprovalQuery query) =>
         Reader.ListAsync(query);

      /// <inheritdoc />
      [GetAction]
      public Task<ApprovalRequestInfo[]> GetMeta_ApprovalRequestsByDoc(string docType, string docKey) =>
         Reader.ByDocAsync(docType, docKey);

      /// <inheritdoc />
      [GetAction]
      public Task<ApprovalRequestDetail?> GetMeta_ApprovalRequest(string approvalRequestId) =>
         Reader.DetailAsync(approvalRequestId);

      /// <inheritdoc />
      [GetAction(PdfTimeoutSecond)]
      public async Task<Stream> GetMeta_ApprovalRequestPdf(string approvalRequestId) {
         var reader = Reader;
         var (request, flow) = await reader.RequireViewableAsync(approvalRequestId);

         if (request.cApprovalRequestPdfKey is not { } pdfKey) {
            throw new ActionException("This approval request has no PDF.", 404);
         }

         var storage = GetService<IBinaryStorage>() ??
                       throw new ActionException("Binary storage is not configured, so the PDF cannot be read.", 501);
         var basePdf = await storage.OpenReadAsync(pdfKey, AbortToken);

         // Without a renderer the frozen document is all there is to give. That is what a deployment
         // that has not wired one up yet gets, rather than an error on every request.
         if (GetService<IApprovalPdfRenderer>() is not { } renderer) return basePdf;

         try {
            return await renderer.RenderStampedAsync(basePdf, await reader.StampSnapshotAsync(request, flow), AbortToken);
         }
         finally {
            await basePdf.DisposeAsync();
         }
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<ApprovalGuardResult> GetMeta_ApprovalGuard(string approvalRequestId, string stepName) {
         var (request, flow) = await Reader.RequireViewableAsync(approvalRequestId);

         var step = await ctx.ta_ApprovalRequestSteps
            .Where(r => r.cApprovalRequestId == approvalRequestId && r.cApprovalRequestStepName == stepName)
            .FirstOrDefaultAsync(AbortToken) ??
            throw new ActionException($"Approval request '{approvalRequestId}' has no step named '{stepName}'.", 404);

         // A step that is not open has nothing left to block: the answer is that it cannot be decided
         // at all, which is a different thing from being blocked.
         if (request.cApprovalRequestStage != ApprovalStage.Pending ||
             step.cApprovalRequestStepStage != ApprovalStepStatus.Waiting ||
             step.cApprovalRequestStepLevel != request.cApprovalRequestLevel) {
            return new ApprovalGuardResult { Allowed = false, Reason = "This step is not waiting for a decision." };
         }

         var scope = new ApprovalRunScope(this, App.ServiceProvider,
            request);
         var guard = await flow.EvaluateGuardAsync(scope, stepName, CallerUserId);

         if (guard is null || guard.IsAllowed) return new ApprovalGuardResult { Allowed = true };

         var overrideClaim = guard.OverridableBy is { } name
            ? new ClaimAction { ModuleName = flow.ModuleName, Name = name }
            : null;

         return new ApprovalGuardResult {
            Allowed = false,
            Reason = guard.Reason,
            CanBeOverridden = overrideClaim is not null,
            // The built-in accounts hold every claim but cannot sign, so offering them an override
            // would only lead to a refusal later.
            CallerCanOverride = overrideClaim is not null && CallerUserId is not (Defaults.AdminUserId or Defaults.DebuggerUserId) &&
                                ApprovalAccess.HoldsClaim(Request, overrideClaim)
         };
      }

      /// <inheritdoc />
      [PostAction]
      public Task<ApprovalDecisionResult[]> PostGetMeta_ApprovalDecide(ApprovalDecision[] decisions) =>
         new ApprovalDecider(ctx, registry, this).DecideAsync(decisions);

      /// <inheritdoc />
      [PostAction]
      public Task PostMeta_ApprovalCancel(string approvalRequestId, string reason) =>
         new ApprovalCanceller(ctx, registry, this).CancelAsync(approvalRequestId, reason);

      /// <inheritdoc />
      [PostAction]
      public async Task PostMeta_ApprovalComment(string approvalRequestId, string note) {
         // A comment names its writer, so it needs a real user, not one of the built-in accounts.
         var me = await new ApprovalAccess(ctx).RequireRealUserAsync(Request, AbortToken);

         note = string.IsNullOrWhiteSpace(note) ? string.Empty : note.Trim();
         if (note.Length == 0) {
            throw new ActionException("A comment cannot be empty.", 400);
         }

         if (note.Length > MaxCommentLength) {
            throw new ActionException($"The comment is {note.Length} characters long; the limit is {MaxCommentLength}.", 400);
         }

         // Whoever may open the request may comment on it, also after it is finished or rejected.
         await Reader.RequireViewableAsync(approvalRequestId);

         var stamp = DateTime.UtcNow;
         ctx.ta_ApprovalRequestComments.Add(new ta_ApprovalRequestComment {
            cApprovalRequestCommentId = $"{Ulid.NewUlid()}",
            cApprovalRequestId = approvalRequestId,
            cUserId = me,
            cApprovalRequestCommentNote = note,
            cApprovalRequestCommentDate = DateTime.Now,
            ustamp = stamp,
            datestamp = stamp
         });
         await ctx.SaveChangesAsync(AbortToken);
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<HubTaskInfo[]> GetMeta_UserHubTasks() {
         Request.RequireUserId();

         var tasks = new List<HubTaskInfo>();

         foreach (var source in GetService<IEnumerable<IHubTaskSource>>() ?? []) {
            try {
               tasks.AddRange(await source.GetTasksAsync(AbortToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !AbortToken.IsCancellationRequested) {
               // One source failing must not empty the whole list: its part stays empty and the rest still shows.
               Logger.LogError(ex, "Hub task source {Source} failed and was left out of the list.", source.GetType().Name);
            }
         }

         return [.. tasks];
      }

      /// <inheritdoc />
      [GetAction(PdfTimeoutSecond)]
      public async Task<Stream> GetMeta_ApprovalSlotCalibration(string docType, string docKey, string docVersion) {
         Request.RequireUserId();

         // A developer tool: the layout of a document is measured against a real one, and nothing of it
         // is stored, so it asks no more than that the caller is a developer or an administrator.
         if (!Request.IsDebugRequest && !Request.IsAdmin) {
            throw new ActionException("Only a developer or an administrator can draw the signature boxes of a document.", 403);
         }

         var flow = registry.Get(docType);
         if (flow.Kind != ApprovalKind.Document || !flow.HasPdf) {
            throw new ActionException($"Document type '{flow.DocType}' has no PDF to draw the boxes on.", 404);
         }

         try {
            ApprovalKey.ReadParts(docKey);
         }
         catch (InvalidOperationException ex) {
            throw new ActionException($"The document key is not valid: {ex.Message}", 400);
         }

         if (string.IsNullOrWhiteSpace(docVersion)) {
            throw new ActionException("A document version is required.", 400);
         }

         var renderer = GetService<IApprovalPdfRenderer>() ??
                        throw new ActionException("No PDF renderer is configured, so the boxes cannot be drawn.", 501);

         // The document is a real one, but nothing is submitted: the request only exists in memory so the
         // handlers of the module have the key and the version they ask for.
         var now = DateTime.Now;
         var scope = new ApprovalRunScope(this, App.ServiceProvider,
            new ta_ApprovalRequest {
               cApprovalRequestId = $"{Ulid.NewUlid()}",
               cApprovalRequestKind = ApprovalKind.Document,
               cApprovalRequestDocType = flow.DocType,
               cApprovalRequestDocKey = docKey,
               cApprovalRequestDocVersion = docVersion,
               cApprovalRequestRequesterId = CallerUserId ?? string.Empty,
               cApprovalRequestStage = ApprovalStage.Draft,
               cApprovalRequestDate = now
            });

         await using var source = await flow.OpenPdfAsync(scope) ??
                                  throw new InvalidOperationException($"The PDF delegate of '{flow.DocType}' returned no stream.");
         await using var pdf = new MemoryStream();
         await source.CopyToAsync(pdf, AbortToken);

         // Every box the flow declares is drawn, also those of steps that would not apply to this document,
         // at the place the layout of this very document puts them.
         var plan = await flow.PlanStepsAsync(scope);
         pdf.Position = 0;
         await flow.LocateSlotsAsync(scope, plan, pdf);
         pdf.Position = 0;

         var labels = new List<ApprovalSlotLabel>();
         foreach (var step in plan) {
            if (step.Snapshot.Slot is { } slot) {
               labels.Add(new ApprovalSlotLabel(step.Name, slot.ToSlot()));
            }

            for (var i = 0; i < step.Snapshot.Fields.Count; i++) {
               var field = step.Snapshot.Fields[i];
               labels.Add(new ApprovalSlotLabel($"{step.Name} / {field.Kind} {i + 1}", field.Slot.ToSlot(), ApprovalSlotKind.Input));
            }
         }

         return await renderer.RenderCalibrationAsync(pdf, labels, AbortToken);
      }

      #endregion
   }
}
