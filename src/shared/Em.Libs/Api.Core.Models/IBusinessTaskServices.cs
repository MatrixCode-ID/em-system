using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Business task monitoring: long-running work the server runs outside a request, so the result does
   /// not depend on the client application staying open. Tasks are started by each module's actions, not
   /// here; this contract only views tasks, cancels them, clears them, fetches results, and sets the limit
   /// on concurrently running tasks.
   /// </summary>
   /// <remarks>
   /// Visible here are the caller's own <see cref="BusinessTaskScope.Personal"/> tasks, or every task for
   /// holders of claim <see cref="ManagerClaim"/> in module
   /// <see cref="Defaults.AdministrativeToolsModuleName"/>. The status of a
   /// <see cref="BusinessTaskScope.Global"/> task owned by a module screen is asked through that module's
   /// own service, not here. The flags <see cref="BusinessTaskInfo.CanCancel"/>,
   /// <see cref="BusinessTaskInfo.CanClear"/> and <see cref="BusinessTaskInfo.CanReadResult"/> on every
   /// result are already computed for the caller, so the UI does not need to guess rights.
   /// </remarks>
   public interface IBusinessTaskServices : IServices
   {
      /// <summary>
      /// Claim name that opens the Business Task Manager screen, written without its module name. Holders
      /// who are not administrators can only view.
      /// </summary>
      const string ManagerClaim = "Business Task Manager Access";

      #region Meta's

      /// <summary>
      /// Every task on the server, personal and global, including finished ones still stored, with their
      /// owner names. Requires claim <see cref="ManagerClaim"/>.
      /// </summary>
      Task<BusinessTaskInfo[]> GetMeta_BusinessTasks();

      /// <summary>
      /// The caller's personal tasks, alive or finished and still stored. Signing in is enough.
      /// </summary>
      Task<BusinessTaskInfo[]> GetMeta_UserBusinessTasks();

      /// <summary>
      /// One task by its ID. Visible to its owner, administrators and holders of claim
      /// <see cref="ManagerClaim"/>; anyone else gets 404, as for a task that does not exist.
      /// </summary>
      Task<BusinessTaskInfo?> GetMeta_BusinessTask(string id);

      /// <summary>
      /// Cancels a task that is still alive. Only its owner or an administrator (403); a finished task
      /// answers 409. A canceled task disappears on its own shortly afterwards.
      /// </summary>
      Task PostMeta_BusinessTaskCancel(string id);

      /// <summary>
      /// Clears a finished task together with its stored result. Only its owner or an administrator
      /// (403); a task that is still alive answers 409.
      /// </summary>
      Task PostMeta_BusinessTaskClear(string id);

      /// <summary>
      /// JSON result of a task, as the raw JSON text. Only its owner or an administrator; answers 400 when
      /// the task's result is not JSON, and 409 when the task has not succeeded.
      /// </summary>
      Task<string> GetMeta_BusinessTaskJsonResult(string id);

      /// <summary>
      /// File result of a task, as a stream the caller reads to the end and then closes. Only its owner or
      /// an administrator; answers 400 when the task's result is not a file, and 409 when the task has not
      /// succeeded.
      /// </summary>
      Task<Stream> GetMeta_BusinessTaskFileResult(string id);

      /// <summary>Limit on concurrently running tasks. Requires claim <see cref="ManagerClaim"/>.</summary>
      Task<BusinessTaskLimit> GetMeta_BusinessTaskLimit();

      /// <summary>
      /// Changes the limit on concurrently running tasks. Administrators only. Takes effect immediately:
      /// queued tasks start right away when the new limit allows it. Running tasks are not stopped even if
      /// the new limit is lower.
      /// </summary>
      Task PostMeta_BusinessTaskLimit(BusinessTaskLimit limit);

      #endregion
   }
}
