using System.IO;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Core;

namespace Em.Api.Core
{
   [Module(Defaults.AdministrativeToolsModuleName)]
   public class BusinessTaskService(EmApp emApp) : ServiceWpfBase(emApp), IBusinessTaskServices
   {
      #region Meta's

      public Task<BusinessTaskInfo[]> GetMeta_BusinessTasks() =>
         GetAsync<BusinessTaskInfo[]>(nameof(GetMeta_BusinessTasks));

      public Task<BusinessTaskInfo[]> GetMeta_UserBusinessTasks() =>
         GetAsync<BusinessTaskInfo[]>(nameof(GetMeta_UserBusinessTasks));

      public Task<BusinessTaskInfo?> GetMeta_BusinessTask(string id) =>
         GetAsync<BusinessTaskInfo?>(nameof(GetMeta_BusinessTask), id);

      public Task PostMeta_BusinessTaskCancel(string id) => PostAsync(nameof(PostMeta_BusinessTaskCancel), id);

      public Task PostMeta_BusinessTaskClear(string id) => PostAsync(nameof(PostMeta_BusinessTaskClear), id);

      public Task<string> GetMeta_BusinessTaskJsonResult(string id) =>
         GetAsync<string>(nameof(GetMeta_BusinessTaskJsonResult), id);

      public Task<Stream> GetMeta_BusinessTaskFileResult(string id) =>
         GetStreamAsync(nameof(GetMeta_BusinessTaskFileResult), id);

      public Task<BusinessTaskLimit> GetMeta_BusinessTaskLimit() =>
         GetAsync<BusinessTaskLimit>(nameof(GetMeta_BusinessTaskLimit));

      public Task PostMeta_BusinessTaskLimit(BusinessTaskLimit limit) =>
         PostAsync(nameof(PostMeta_BusinessTaskLimit), limit);

      #endregion
   }
}
