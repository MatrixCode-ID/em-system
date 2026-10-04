using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Inspection and control of business tasks by id: the personal task hub of every user and the Business
   /// Task Manager screen. Starting a task is not here - modules start their own through
   /// <c>ServicesBase.StartBusinessTask</c> - and neither is looking a task up by key, which stays behind
   /// the claim of the module that owns it.
   /// </summary>
   [Module(Defaults.AdministrativeToolsModuleName)]
   public class BusinessTaskServices : ServicesBase, IBusinessTaskServices
   {
      private BusinessTaskRunner Runner => GetService<BusinessTaskRunner>()!;

      // Mirrors the gate's own rule: the debug path and administrators pass every claim check.
      private bool HoldsManagerClaim =>
         Request.IsDebugRequest || Request.IsAdmin ||
         Request.Claims.Any(r =>
            string.Equals(r.ModuleName, Defaults.AdministrativeToolsModuleName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Name, IBusinessTaskServices.ManagerClaim, StringComparison.OrdinalIgnoreCase));

      #region Meta's

      [GetAction(claim: IBusinessTaskServices.ManagerClaim)]
      public Task<BusinessTaskInfo[]> GetMeta_BusinessTasks() =>
         Task.FromResult(Runner.List(Request));

      [GetAction]
      public Task<BusinessTaskInfo[]> GetMeta_UserBusinessTasks() {
         var userId = Request.RequireUserId();
         return Task.FromResult(Runner.List(Request,
            r => r.Scope == BusinessTaskScope.Personal && r.OwnerUserId == userId));
      }

      [GetAction]
      public Task<BusinessTaskInfo?> GetMeta_BusinessTask(string id) {
         Request.RequireUserId();
         var info = Runner.Get(id, Request);

         // Someone else's task is answered exactly like a missing one, so ids cannot be probed.
         if (info is null || !(Request.IsSelf(info.OwnerUserId) || HoldsManagerClaim)) {
            throw new ActionException("There is no such task.", 404);
         }

         return Task.FromResult<BusinessTaskInfo?>(info);
      }

      [PostAction]
      public Task PostMeta_BusinessTaskCancel(string id) {
         Request.RequireSelfOrAdmin(Runner.RequireOwner(id));
         Runner.Cancel(id);
         return Task.CompletedTask;
      }

      [PostAction]
      public Task PostMeta_BusinessTaskClear(string id) {
         Request.RequireSelfOrAdmin(Runner.RequireOwner(id));
         Runner.Clear(id);
         return Task.CompletedTask;
      }

      [GetAction]
      public async Task<string> GetMeta_BusinessTaskJsonResult(string id) {
         Request.RequireSelfOrAdmin(Runner.RequireOwner(id));
         var (path, _) = Runner.RequireResult(id, BusinessTaskOutputKind.Json);
         return await File.ReadAllTextAsync(path, AbortToken);
      }

      [GetAction]
      public Task<Stream> GetMeta_BusinessTaskFileResult(string id) {
         Request.RequireSelfOrAdmin(Runner.RequireOwner(id));
         var (path, _) = Runner.RequireResult(id, BusinessTaskOutputKind.File);

         // FileShare.Delete: a clear while the download is still running removes the file without waiting
         // for it; the download already holds the open handle and finishes normally.
         Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
         return Task.FromResult(stream);
      }

      [GetAction(claim: IBusinessTaskServices.ManagerClaim)]
      public Task<BusinessTaskLimit> GetMeta_BusinessTaskLimit() =>
         Task.FromResult(Runner.Limit);

      [PostAction]
      public async Task PostMeta_BusinessTaskLimit(BusinessTaskLimit limit) {
         Request.RequireAdmin();
         ArgumentNullException.ThrowIfNull(limit);

         if (limit.Limit < 1) {
            throw new ActionException("The limit must be at least 1.", 400);
         }

         if (!Enum.IsDefined(limit.Mode)) {
            throw new ActionException($"'{limit.Mode}' is not a known limit mode.", 400);
         }

         await SetMetaValue(BusinessTaskRunner.LimitModeMetaKey, limit.Mode.ToString(),
            "How the business task concurrency limit is counted.");
         await SetMetaValue(BusinessTaskRunner.LimitMetaKey, $"{limit.Limit}",
            "How many business tasks may run at the same time.");
         Runner.SetLimit(limit);
      }

      #endregion
   }
}
