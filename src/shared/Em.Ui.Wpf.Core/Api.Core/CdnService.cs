using System.IO;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Core;

namespace Em.Api.Core
{
   /// <summary>Client-side implementation of the CDN management actions.</summary>
   [Module(Defaults.AdministrativeToolsModuleName)]
   public class CdnService(EmApp emApp) : ServiceWpfBase(emApp), ICdnServices
   {
      /// <inheritdoc />
      public Task<StorageFeatureStatus> GetMeta_CdnStatus() => GetAsync<StorageFeatureStatus>(nameof(GetMeta_CdnStatus));
      /// <inheritdoc />
      public Task<StorageSettingsDetail> GetMeta_CdnSettings() => GetAsync<StorageSettingsDetail>(nameof(GetMeta_CdnSettings));
      /// <inheritdoc />
      public Task<StorageDirectoryValidation> PostGetMeta_CdnValidateDirectory(StorageFeatureSettings settings) => PostAsync<StorageDirectoryValidation>(nameof(PostGetMeta_CdnValidateDirectory), settings);
      /// <inheritdoc />
      public Task<StorageSettingsDetail> PostGetMeta_CdnSettingsSave(StorageSettingsSave request) => PostAsync<StorageSettingsDetail>(nameof(PostGetMeta_CdnSettingsSave), request);

      /// <inheritdoc />
      public Task<CdnStorageInfo> GetMeta_CdnStorageSize() =>
         GetAsync<CdnStorageInfo>(nameof(GetMeta_CdnStorageSize));

      #region Meta's

      /// <inheritdoc />
      public Task<CdnFolderContent> GetMeta_CdnFolder(string? path) =>
         GetAsync<CdnFolderContent>(nameof(GetMeta_CdnFolder), path!);

      /// <inheritdoc />
      public Task<CdnItemCount> GetMeta_CdnItemCount(string path) =>
         GetAsync<CdnItemCount>(nameof(GetMeta_CdnItemCount), path);

      /// <inheritdoc />
      public Task<CdnEntry[]> GetMeta_CdnTree(string path) =>
         GetAsync<CdnEntry[]>(nameof(GetMeta_CdnTree), path);

      /// <inheritdoc />
      public Task<CdnEntry> PostGetMeta_CdnMove(string path, string? targetFolder, bool overwrite) =>
         PostAsync<CdnEntry>(nameof(PostGetMeta_CdnMove), path, targetFolder!, overwrite);

      /// <inheritdoc />
      public Task<CdnEntry> PostGetMeta_CdnUpload(CdnUploadRequest request, Stream content) =>
         PostStreamAsync<CdnEntry>(nameof(PostGetMeta_CdnUpload), content, request);

      /// <inheritdoc />
      public Task<CdnEntry> PostGetMeta_CdnCreateFolder(string? path, string folderName) =>
         PostAsync<CdnEntry>(nameof(PostGetMeta_CdnCreateFolder), path!, folderName);

      /// <inheritdoc />
      public Task PostMeta_CdnDelete(string path) => PostAsync(nameof(PostMeta_CdnDelete), path);

      /// <inheritdoc />
      public Task<BusinessTaskInfo> PostGetMeta_CdnArchive(CdnArchiveRequest request) =>
         PostAsync<BusinessTaskInfo>(nameof(PostGetMeta_CdnArchive), request);

      /// <inheritdoc />
      public Task<BusinessTaskInfo?> GetMeta_CdnArchiveTask() =>
         GetAsync<BusinessTaskInfo?>(nameof(GetMeta_CdnArchiveTask));

      /// <inheritdoc />
      public Task PostMeta_CdnArchiveCancel() => PostAsync(nameof(PostMeta_CdnArchiveCancel));

      /// <inheritdoc />
      public Task PostMeta_CdnArchiveClear() => PostAsync(nameof(PostMeta_CdnArchiveClear));

      /// <inheritdoc />
      public Task<string[]> GetMeta_CdnExtractConflicts(string path) =>
         GetAsync<string[]>(nameof(GetMeta_CdnExtractConflicts), path);

      /// <inheritdoc />
      public Task<BusinessTaskInfo> PostGetMeta_CdnExtract(string path, bool overwrite) =>
         PostAsync<BusinessTaskInfo>(nameof(PostGetMeta_CdnExtract), path, overwrite);

      /// <inheritdoc />
      public Task<BusinessTaskInfo[]> GetMeta_CdnExtractTasks(string? folder) =>
         GetAsync<BusinessTaskInfo[]>(nameof(GetMeta_CdnExtractTasks), folder!);

      /// <inheritdoc />
      public Task PostMeta_CdnExtractCancel(string path) => PostAsync(nameof(PostMeta_CdnExtractCancel), path);

      /// <inheritdoc />
      public Task PostMeta_CdnExtractClear(string path) => PostAsync(nameof(PostMeta_CdnExtractClear), path);

      #endregion
   }
}
