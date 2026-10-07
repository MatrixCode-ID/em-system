using System.IO;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Core;

namespace Em.Api.Core
{
   /// <summary>Client-side implementation of the business task actions.</summary>
   [Module(Defaults.AdministrativeToolsModuleName)]
   public class BusinessTaskService(EmApp emApp) : ServiceWpfBase(emApp), IBusinessTaskServices
   {
      #region Meta's

      /// <inheritdoc />
      public Task<BusinessTaskInfo[]> GetMeta_BusinessTasks() =>
         GetAsync<BusinessTaskInfo[]>(nameof(GetMeta_BusinessTasks));

      /// <inheritdoc />
      public Task<BusinessTaskInfo[]> GetMeta_UserBusinessTasks() =>
         GetAsync<BusinessTaskInfo[]>(nameof(GetMeta_UserBusinessTasks));

      /// <inheritdoc />
      public Task<BusinessTaskInfo?> GetMeta_BusinessTask(string id) =>
         GetAsync<BusinessTaskInfo?>(nameof(GetMeta_BusinessTask), id);

      /// <inheritdoc />
      public Task PostMeta_BusinessTaskCancel(string id) => PostAsync(nameof(PostMeta_BusinessTaskCancel), id);

      /// <inheritdoc />
      public Task PostMeta_BusinessTaskClear(string id) => PostAsync(nameof(PostMeta_BusinessTaskClear), id);

      /// <inheritdoc />
      public Task<string> GetMeta_BusinessTaskJsonResult(string id) =>
         GetAsync<string>(nameof(GetMeta_BusinessTaskJsonResult), id);

      /// <inheritdoc />
      public Task<Stream> GetMeta_BusinessTaskFileResult(string id) =>
         GetStreamAsync(nameof(GetMeta_BusinessTaskFileResult), id);

      /// <inheritdoc />
      public Task<BusinessTaskLimit> GetMeta_BusinessTaskLimit() =>
         GetAsync<BusinessTaskLimit>(nameof(GetMeta_BusinessTaskLimit));

      /// <inheritdoc />
      public Task PostMeta_BusinessTaskLimit(BusinessTaskLimit limit) =>
         PostAsync(nameof(PostMeta_BusinessTaskLimit), limit);

      #endregion
   }
}
