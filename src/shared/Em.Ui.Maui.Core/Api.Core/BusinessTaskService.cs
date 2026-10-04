using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Maui.Core;

namespace Em.Api.Core
{
   // Registered so that both UI cores keep the same structure, but not wired to the API yet: the MAUI
   // client has no task hub, status dialog or Business Task Manager screen. The real implementation
   // follows in a separate plan.
   [Module(Defaults.AdministrativeToolsModuleName)]
   public class BusinessTaskService(EmApp emApp) : ServiceMauiBase(emApp), IBusinessTaskServices
   {
      private const string NotAvailable = "Business tasks are not available in the MAUI client yet.";

      #region Meta's

      public Task<BusinessTaskInfo[]> GetMeta_BusinessTasks() => throw new NotImplementedException(NotAvailable);

      public Task<BusinessTaskInfo[]> GetMeta_UserBusinessTasks() => throw new NotImplementedException(NotAvailable);

      public Task<BusinessTaskInfo?> GetMeta_BusinessTask(string id) => throw new NotImplementedException(NotAvailable);

      public Task PostMeta_BusinessTaskCancel(string id) => throw new NotImplementedException(NotAvailable);

      public Task PostMeta_BusinessTaskClear(string id) => throw new NotImplementedException(NotAvailable);

      public Task<string> GetMeta_BusinessTaskJsonResult(string id) => throw new NotImplementedException(NotAvailable);

      public Task<Stream> GetMeta_BusinessTaskFileResult(string id) => throw new NotImplementedException(NotAvailable);

      public Task<BusinessTaskLimit> GetMeta_BusinessTaskLimit() => throw new NotImplementedException(NotAvailable);

      public Task PostMeta_BusinessTaskLimit(BusinessTaskLimit limit) => throw new NotImplementedException(NotAvailable);

      #endregion
   }
}
