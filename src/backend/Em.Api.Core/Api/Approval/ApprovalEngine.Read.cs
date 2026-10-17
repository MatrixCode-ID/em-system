using Em.Api.Core.Models;
using Em.Api.Core.ObjectStorage;
using Em.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Api.Core.Approval;

internal sealed partial class ApprovalEngine {
   private IQueryable<ta_ApprovalRequest> VisibleQuery() {
      caller.RequireUserId();
      var dataNames = registry.Data.Where(x => x.ParentType is null).Where(x => caller.IsAdmin || x.Resolve(services).CanView(caller)).Select(x => ApprovalSubjectKey.NameOf(x.DefinitionType)).ToArray();
      var systems = registry.Systems.Select(x => ApprovalSubjectKey.NameOf(x.DefinitionType)).ToArray();
      var systemView = systems.Where(x => caller.IsAdmin || ApprovalAccess.HoldsClaim(caller, ViewClaim(x))).ToArray();
      var involved = from candidate in db.ta_ApprovalRequestStepCandidates
                     join step in db.ta_ApprovalRequestSteps on candidate.cApprovalRequestStepId equals step.cApprovalRequestStepId
                     where candidate.cUserId == caller.cUserId select step.cApprovalRequestId;
      return db.ta_ApprovalRequests.Where(x => (x.cApprovalRequestKind == ApprovalKind.Data && dataNames.Contains(x.cApprovalName)) ||
         (x.cApprovalRequestKind == ApprovalKind.System && systems.Contains(x.cApprovalName) &&
          (systemView.Contains(x.cApprovalName) || x.cApprovalRequestRequesterId == caller.cUserId || involved.Contains(x.cApprovalRequestId))));
   }
   private static ClaimAction ViewClaim(string name) => new() { ModuleName = Defaults.ApprovalModuleName, Name = $"View {name}" };
   private IQueryable<ta_ApprovalRequest> WaitingQuery(IQueryable<ta_ApprovalRequest> source) {
      if (caller.cUserId is null or Defaults.AdminUserId or Defaults.DebuggerUserId) return source.Where(_ => false);
      var data = registry.Data.Where(x => x.ParentType is null && (caller.IsAdmin || x.Resolve(services).CanDecide(caller)))
         .Select(x => ApprovalSubjectKey.NameOf(x.DefinitionType)).ToArray();
      return source.Where(x => x.cApprovalRequestStage == ApprovalStage.Pending &&
         ((x.cApprovalRequestKind == ApprovalKind.Data && data.Contains(x.cApprovalName)) ||
          (x.cApprovalRequestKind == ApprovalKind.System && (caller.IsAdmin ||
            (from step in db.ta_ApprovalRequestSteps
             join candidate in db.ta_ApprovalRequestStepCandidates on step.cApprovalRequestStepId equals candidate.cApprovalRequestStepId
             where step.cApprovalRequestId == x.cApprovalRequestId && step.cApprovalRequestStepLevel == x.cApprovalRequestLevel &&
                   step.cApprovalRequestStepStage == ApprovalStepStatus.Waiting && candidate.cUserId == caller.cUserId select step).Any()))));
   }
   internal async Task<ApprovalCapability[]> CapabilitiesAsync(CancellationToken ct) {
      caller.RequireUserId(); var result = new List<ApprovalCapability>();
      var real = caller.cUserId is not (null or Defaults.AdminUserId or Defaults.DebuggerUserId) && await IsActiveAsync(caller.cUserId, ct);
      var visible = VisibleQuery(); var waiting = WaitingQuery(visible);
      foreach (var registration in registry.Data.Where(x => x.ParentType is null)) {
         var table = registration.Resolve(services); var name = ApprovalSubjectKey.NameOf(registration.DefinitionType);
         var canView = caller.IsAdmin || table.CanView(caller);
         if (!canView) continue;
         result.Add(new ApprovalCapability(ApprovalKind.Data, name, string.IsNullOrEmpty(table.Title) ? name : table.Title, true,
            real && (caller.IsAdmin || table.CanDecide(caller)), real ? await waiting.CountAsync(x => x.cApprovalRequestKind == ApprovalKind.Data && x.cApprovalName == name, ct) : 0,
            table.ApproverOnly, []));
      }
      foreach (var registration in registry.Systems) {
         var name = ApprovalSubjectKey.NameOf(registration.DefinitionType);
         var canView = caller.IsAdmin || ApprovalAccess.HoldsClaim(caller, ViewClaim(name)) ||
            await visible.AnyAsync(x => x.cApprovalRequestKind == ApprovalKind.System && x.cApprovalName == name, ct);
         if (!canView) continue;
         var count = real ? await waiting.CountAsync(x => x.cApprovalRequestKind == ApprovalKind.System && x.cApprovalName == name, ct) : 0;
         result.Add(new ApprovalCapability(ApprovalKind.System, name, string.IsNullOrEmpty(registration.Definition.Title) ? name : registration.Definition.Title,
            true, real && (caller.IsAdmin || count > 0), count, [], registration.Definition.Steps.ToArray()));
      }
      return result.ToArray();
   }
   internal async Task<ApprovalPage> ListAsync(ApprovalQuery filter, CancellationToken ct) {
      var query = VisibleQuery();
      if (filter.Kind is { } kind) query = query.Where(x => x.cApprovalRequestKind == kind);
      if (filter.Name is { } name) query = query.Where(x => x.cApprovalName == name);
      if (filter.SubjectKey is { } key) query = query.Where(x => x.cSubjectKey == key);
      if (filter.Stage is { } stage) query = query.Where(x => x.cApprovalRequestStage == stage);
      if (!string.IsNullOrWhiteSpace(filter.Search)) query = query.Where(x => x.cSummary.Contains(filter.Search) || x.cApprovalName.Contains(filter.Search));
      if (filter.WaitingForMeOnly) query = WaitingQuery(query);
      var total = await query.CountAsync(ct); var page = Math.Max(1, filter.Page); var size = Math.Clamp(filter.PageSize, 1, 200);
      return new ApprovalPage(await query.AsNoTracking().OrderByDescending(x => x.datestamp).ThenByDescending(x => x.cApprovalRequestId).Skip((page-1)*size).Take(size).ToArrayAsync(ct), total, page, size);
   }
   internal Task<ta_ApprovalRequest[]> BySubjectAsync(ApprovalKind kind, string name, string key, CancellationToken ct) =>
      VisibleQuery().AsNoTracking().Where(x => x.cApprovalRequestKind == kind && x.cApprovalName == name && x.cSubjectKey == key).OrderByDescending(x => x.datestamp).ToArrayAsync(ct);
   internal async Task<ApprovalRequestDetail> DetailAsync(string id, CancellationToken ct) {
      var request = await RequireViewAsync(id, ct);
      var items = await db.ta_ApprovalRequestItems.AsNoTracking().Where(x => x.cApprovalRequestId == id).OrderBy(x => x.cApprovalRequestItemOrder).ToArrayAsync(ct);
      var itemIds = items.Select(x => x.cApprovalRequestItemId).ToArray();
      var fields = await db.ta_ApprovalRequestItemFields.AsNoTracking().Where(x => itemIds.Contains(x.cApprovalRequestItemId)).ToArrayAsync(ct);
      if (request.cApprovalRequestStage == ApprovalStage.Pending && request.cApprovalRequestKind == ApprovalKind.Data) {
         foreach (var item in items.Where(x => x.cApprovalRequestItemOperation == ApprovalItemOperation.Update)) {
            var table = registry.DataEntity(item.cApprovalRequestItemEntity).Resolve(services);
            var row = await table.FindAsync(item.cSubjectKey!, ct);
            foreach (var field in fields.Where(x => x.cApprovalRequestItemId == item.cApprovalRequestItemId)) {
               field.cApprovalRequestItemFieldCurrent = row is null ? null : ApprovalValue.Normalize(table.Property(field.cApprovalRequestItemFieldName).GetValue(row));
               field.cApprovalRequestItemFieldConflict = row is null || (field.cApprovalRequestItemFieldCurrent != field.cApprovalRequestItemFieldOld && field.cApprovalRequestItemFieldCurrent != field.cApprovalRequestItemFieldNew);
            }
         }
      }
      var steps = await db.ta_ApprovalRequestSteps.AsNoTracking().Where(x => x.cApprovalRequestId == id).OrderBy(x => x.cApprovalRequestStepOrder).ToArrayAsync(ct);
      var stepIds = steps.Select(x => x.cApprovalRequestStepId).ToArray();
      var candidates = await db.ta_ApprovalRequestStepCandidates.AsNoTracking().Where(x => stepIds.Contains(x.cApprovalRequestStepId)).OrderBy(x => x.cApprovalRequestStepCandidateOrder).ToArrayAsync(ct);
      var real = caller.cUserId is not (null or Defaults.AdminUserId or Defaults.DebuggerUserId) && await IsActiveAsync(caller.cUserId, ct);
      var pending = request.cApprovalRequestStage == ApprovalStage.Pending;
      var decidable = steps.Where(x => pending && real && x.cApprovalRequestStepLevel == request.cApprovalRequestLevel &&
         x.cApprovalRequestStepStage == ApprovalStepStatus.Waiting && (caller.IsAdmin || candidates.Any(c => c.cApprovalRequestStepId == x.cApprovalRequestStepId && c.cUserId == caller.cUserId))).Select(x => x.cApprovalRequestStepId).ToArray();
      return new ApprovalRequestDetail(request, items, fields, steps, candidates,
         await db.ta_ApprovalRequestSignatures.AsNoTracking().Where(x => x.cApprovalRequestId == id).OrderBy(x => x.datestamp).ToArrayAsync(ct),
         await db.ta_ApprovalRequestComments.AsNoTracking().Where(x => x.cApprovalRequestId == id).OrderBy(x => x.datestamp).ToArrayAsync(ct),
         await db.ta_ApprovalRequestAttachments.AsNoTracking().Where(x => x.cApprovalRequestId == id).OrderBy(x => x.datestamp).ToArrayAsync(ct),
         real && pending && (request.cApprovalRequestKind == ApprovalKind.Data ? CanDecideData(request) : decidable.Length > 0),
         real && pending && CanWithdraw(request), real && request.cApprovalRequestStage >= ApprovalStage.Rejected && CanVoid(request),
         real && request.cApprovalRequestStage >= ApprovalStage.Rejected && CanVoid(request),
         real && services.GetService<IObjectStore>() is not null, decidable);
   }
   internal async Task<ta_ApprovalRequestAttachment> AttachAsync(ApprovalAttachmentUpload upload, Stream content, CancellationToken ct) {
      var user = await RealUserAsync(ct); await RequireViewAsync(upload.RequestId, ct);
      RequireText(upload.Name, "File name", 255);
      if (upload.CommentId is not null && !await db.ta_ApprovalRequestComments.AnyAsync(x => x.cApprovalRequestCommentId == upload.CommentId && x.cApprovalRequestId == upload.RequestId, ct))
         throw new ActionException("The comment does not belong to this request.", 400);
      var store = services.GetService<IObjectStore>() ?? throw new ActionException("Object storage is not enabled; attachments are unavailable.", 409);
      var id = Id(); var key = $"{upload.RequestId}/{id}";
      var stored = await store.PutAsync("approval-attachments", key, content, ct: ct);
      var row = new ta_ApprovalRequestAttachment {
         cApprovalRequestAttachmentId = id, cApprovalRequestId = upload.RequestId, cApprovalRequestCommentId = upload.CommentId,
         cApprovalRequestAttachmentKey = key, cApprovalRequestAttachmentVersion = stored.Version,
         cApprovalRequestAttachmentName = upload.Name, cApprovalRequestAttachmentSize = stored.Size, cUserId = user,
         datestamp = DateTime.Now, ustamp = DateTime.Now
      };
      try { db.ta_ApprovalRequestAttachments.Add(row); await db.SaveChangesAsync(ct); }
      catch {
         try { await store.PurgeVersionAsync("approval-attachments", key, stored.Version, CancellationToken.None); }
         catch (Exception cleanup) { logger.LogWarning(cleanup, "Attachment compensation failed for {Key}.", key); }
         throw;
      }
      return row;
   }
   internal async Task<Stream> AttachmentAsync(string id, CancellationToken ct) {
      var row = await db.ta_ApprovalRequestAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.cApprovalRequestAttachmentId == id, ct) ?? throw new ActionException("Attachment was not found.", 404);
      await RequireViewAsync(row.cApprovalRequestId, ct);
      var store = services.GetService<IObjectStore>() ?? throw new ActionException("Object storage is not enabled.", 409);
      return (await store.OpenAsync("approval-attachments", row.cApprovalRequestAttachmentKey, row.cApprovalRequestAttachmentVersion, ct)).Content;
   }
   internal async Task<IReadOnlyList<ApprovalHubGroup>> HubAsync(CancellationToken ct) {
      var rows = await WaitingQuery(VisibleQuery()).Select(x => new { x.cApprovalRequestKind, x.cApprovalName, x.datestamp }).ToArrayAsync(ct);
      return rows.GroupBy(x => (x.cApprovalRequestKind, x.cApprovalName)).Select(g => new ApprovalHubGroup(g.Key.cApprovalRequestKind, g.Key.cApprovalName, g.Count(), g.Min(x => x.datestamp))).ToArray();
   }
}
