using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Core;

namespace Em.Api.Core
{
   [Module(Defaults.AdministrativeToolsModuleName)]
   public class CtnService(EmApp emApp) : ServiceWpfBase(emApp), ICtnServices
   {
      #region Meta's

      public Task<StorageFeatureStatus> GetMeta_CtnStatus() => GetAsync<StorageFeatureStatus>(nameof(GetMeta_CtnStatus));
      public Task<StorageSettingsDetail> GetMeta_CtnSettings() => GetAsync<StorageSettingsDetail>(nameof(GetMeta_CtnSettings));
      public Task<StorageDirectoryValidation> PostGetMeta_CtnValidateDirectory(StorageFeatureSettings settings) => PostAsync<StorageDirectoryValidation>(nameof(PostGetMeta_CtnValidateDirectory), settings);
      public Task<StorageSettingsDetail> PostGetMeta_CtnSettingsSave(StorageSettingsSave request) => PostAsync<StorageSettingsDetail>(nameof(PostGetMeta_CtnSettingsSave), request);

      public Task<CtnStorageInfo> GetMeta_CtnStorageSize() =>
         GetAsync<CtnStorageInfo>(nameof(GetMeta_CtnStorageSize));

      public Task<CtnRootInfo[]> GetMeta_CtnRoots() =>
         GetAsync<CtnRootInfo[]>(nameof(GetMeta_CtnRoots));

      public Task<CtnRootInfo> PostGetMeta_CtnRootCreate(string name, string? description) =>
         PostAsync<CtnRootInfo>(nameof(PostGetMeta_CtnRootCreate), name, description!);

      public Task PostMeta_CtnRootUpdate(string rootId, string? description, bool isActive) =>
         PostAsync(nameof(PostMeta_CtnRootUpdate), rootId, description!, isActive);

      public Task PostMeta_CtnRootDelete(string rootId) =>
         PostAsync(nameof(PostMeta_CtnRootDelete), rootId);

      public Task<CtnTree> GetMeta_CtnTree(string rootId) =>
         GetAsync<CtnTree>(nameof(GetMeta_CtnTree), rootId);

      public Task<CtnFolderInfo> PostGetMeta_CtnFolderCreate(string rootId, string? parentFolderId, string name) =>
         PostAsync<CtnFolderInfo>(nameof(PostGetMeta_CtnFolderCreate), rootId, parentFolderId!, name);

      public Task<CtnFolderInfo> PostGetMeta_CtnFolderRename(string folderId, string name) =>
         PostAsync<CtnFolderInfo>(nameof(PostGetMeta_CtnFolderRename), folderId, name);

      public Task<CtnFolderInfo> PostGetMeta_CtnFolderMove(string folderId, string? targetParentFolderId) =>
         PostAsync<CtnFolderInfo>(nameof(PostGetMeta_CtnFolderMove), folderId, targetParentFolderId!);

      public Task PostMeta_CtnFolderDelete(string folderId) =>
         PostAsync(nameof(PostMeta_CtnFolderDelete), folderId);

      public Task<CtnImageInfo> PostGetMeta_CtnImageCreate(string rootId, string? folderId, string name, string? description) =>
         PostAsync<CtnImageInfo>(nameof(PostGetMeta_CtnImageCreate), rootId, folderId!, name, description!);

      public Task<CtnImageInfo> PostGetMeta_CtnImageMove(string imageId, string? targetFolderId) =>
         PostAsync<CtnImageInfo>(nameof(PostGetMeta_CtnImageMove), imageId, targetFolderId!);

      public Task PostMeta_CtnImageUpdate(string imageId, string? description, bool isActive) =>
         PostAsync(nameof(PostMeta_CtnImageUpdate), imageId, description!, isActive);

      public Task PostMeta_CtnImageDelete(string imageId) =>
         PostAsync(nameof(PostMeta_CtnImageDelete), imageId);

      public Task<CtnManifestInfo[]> GetMeta_CtnImageManifests(string imageId) =>
         GetAsync<CtnManifestInfo[]>(nameof(GetMeta_CtnImageManifests), imageId);

      #endregion
   }
}
