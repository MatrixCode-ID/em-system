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
         if (!canView && !caller.IsDebugRequest && !ApprovalAccess.HoldsClaim(caller, new ClaimAction { ModuleName = "Administrative Tools", Name = "Approval Signer Settings" })) continue;
         var count = real ? await visible.CountAsync(x => x.cApprovalRequestKind == ApprovalKind.System && x.cApprovalName == name && x.cApprovalRequestStage == ApprovalStage.Pending &&
            db.ta_ApprovalRequestSteps.Any(s => s.cApprovalRequestId == x.cApprovalRequestId && s.cApprovalRequestStepLevel == x.cApprovalRequestLevel && s.cApprovalRequestStepStage == ApprovalStepStatus.Waiting && db.ta_ApprovalRequestStepCandidates.Any(c => c.cApprovalRequestStepId == s.cApprovalRequestStepId && c.cUserId == caller.cUserId)), ct) : 0;
         result.Add(new ApprovalCapability(ApprovalKind.System, name, string.IsNullOrEmpty(registration.Definition.Title) ? name : registration.Definition.Title,
            canView, real && (caller.IsAdmin || count > 0), count, [], registration.Definition.Steps.ToArray(), registration.Definition.AllowBatchSign));
      }
      return result.ToArray();
   }
   private IQueryable<ta_ApprovalRequest> FilterQuery(ApprovalQuery filter) {
      var query = VisibleQuery();
      if (filter.Kind is { } kind) query = query.Where(x => x.cApprovalRequestKind == kind);
      if (filter.Name is { } name) query = query.Where(x => x.cApprovalName == name);
      if (filter.SubjectKey is { } key) query = query.Where(x => x.cSubjectKey == key);
      if (filter.Stage is { } stage) query = query.Where(x => x.cApprovalRequestStage == stage);
      if (!string.IsNullOrWhiteSpace(filter.Search)) query = query.Where(x => x.cSummary.Contains(filter.Search) || (x.cSubSummary != null && x.cSubSummary.Contains(filter.Search)) || x.cApprovalName.Contains(filter.Search));
      if (filter.WaitingForMeOnly) query = WaitingQuery(query);
      if (filter.DateFrom is { } from) query = query.Where(x => x.datestamp >= from.Date);
      if (filter.DateTo is { } to) {
         var end = to.Date.AddDays(1); query = query.Where(x => x.datestamp < end);
      }
      return query;
   }
   internal Task<ApprovalYearMonth[]> YearMonthsAsync(ApprovalQuery filter, CancellationToken ct) =>
      FilterQuery(filter with { DateFrom = null, DateTo = null }).AsNoTracking()
         .GroupBy(x => new { x.datestamp.Year, x.datestamp.Month })
         .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month)
         .Select(g => new ApprovalYearMonth(g.Key.Year, g.Key.Month, g.Count())).ToArrayAsync(ct);
   internal async Task<ApprovalPage> ListAsync(ApprovalQuery filter, CancellationToken ct) {
      var query = FilterQuery(filter);
      var total = await query.CountAsync(ct); var page = Math.Max(1, filter.Page); var size = Math.Clamp(filter.PageSize, 1, 200);
      var ordered = filter.OldestFirst ? query.AsNoTracking().OrderBy(x => x.datestamp).ThenBy(x => x.cApprovalRequestId) : query.AsNoTracking().OrderByDescending(x => x.datestamp).ThenByDescending(x => x.cApprovalRequestId);
      var rows = await ordered.Skip((page-1)*size).Take(size).ToArrayAsync(ct);
      var ids = rows.Select(x => x.cApprovalRequestId).ToArray();
      var steps = await db.ta_ApprovalRequestSteps.AsNoTracking().Where(x => ids.Contains(x.cApprovalRequestId)).ToArrayAsync(ct);
      var signatures = await db.ta_ApprovalRequestSignatures.AsNoTracking().Where(x => ids.Contains(x.cApprovalRequestId)).ToArrayAsync(ct);
      foreach (var row in rows.Where(x => x.cApprovalRequestStage == ApprovalStage.Pending && x.cApprovalRequestKind == ApprovalKind.System)) {
         row.CurrentStep = string.Join(", ", steps.Where(x => x.cApprovalRequestId == row.cApprovalRequestId && x.cApprovalRequestStepLevel == row.cApprovalRequestLevel && x.cApprovalRequestStepStage == ApprovalStepStatus.Waiting).Select(x => x.cApprovalRequestStepName));
         var earlier = steps.Where(x => x.cApprovalRequestId == row.cApprovalRequestId && x.cApprovalRequestStepLevel < row.cApprovalRequestLevel).Select(x => x.cApprovalRequestStepId).ToArray();
         row.WaitingSince = signatures.Where(x => x.cApprovalRequestId == row.cApprovalRequestId && earlier.Contains(x.cApprovalRequestStepId)).Select(x => (DateTime?)x.datestamp).Max() ?? row.datestamp;
      }
      return new ApprovalPage(rows, total, page, size);
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
      var comments = await db.ta_ApprovalRequestComments.AsNoTracking().Where(x => x.cApprovalRequestId == id).OrderBy(x => x.datestamp).ToArrayAsync(ct);
      var users = await db.ta_Users.Where(u => u.cUserId == request.cApprovalRequestRequesterId || candidates.Select(c => c.cUserId).Contains(u.cUserId) ||
         db.ta_ApprovalRequestSignatures.Any(s => s.cApprovalRequestId == id && s.cUserId == u.cUserId) ||
         db.ta_ApprovalRequestComments.Any(c => c.cApprovalRequestId == id && c.cUserId == u.cUserId))
         .Select(u => new ApprovalSignerUser(u.cUserId, u.cUserAccount, u.cUserState == UserState.Active)).ToListAsync(ct);
      foreach (var (systemId, account) in new[] { (Defaults.DebuggerUserId, Defaults.DebuggerUserAccount), (Defaults.AdminUserId, Defaults.AdminUserAccount) })
         if (comments.Any(x => x.cUserId == systemId) && users.All(x => x.UserId != systemId)) users.Add(new(systemId, account, true));
      return new ApprovalRequestDetail(request, items, fields, steps, candidates,
         await db.ta_ApprovalRequestSignatures.AsNoTracking().Where(x => x.cApprovalRequestId == id).OrderBy(x => x.datestamp).ToArrayAsync(ct),
         comments,
         await db.ta_ApprovalRequestAttachments.AsNoTracking().Where(x => x.cApprovalRequestId == id).OrderBy(x => x.datestamp).ToArrayAsync(ct),
         real && pending && (request.cApprovalRequestKind == ApprovalKind.Data ? CanDecideData(request) : decidable.Length > 0),
         real && pending && CanWithdraw(request), real && request.cApprovalRequestStage >= ApprovalStage.Rejected && CanVoid(request),
         real && request.cApprovalRequestStage >= ApprovalStage.Rejected && CanVoid(request),
         real && services.GetService<IObjectStore>() is not null, decidable,
         users.ToArray());
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
