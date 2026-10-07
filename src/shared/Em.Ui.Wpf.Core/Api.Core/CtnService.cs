using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Core;

namespace Em.Api.Core
{
   /// <summary>Client-side implementation of the container registry management actions.</summary>
   [Module(Defaults.AdministrativeToolsModuleName)]
   public class CtnService(EmApp emApp) : ServiceWpfBase(emApp), ICtnServices
   {
      #region Meta's

      /// <inheritdoc />
      public Task<StorageFeatureStatus> GetMeta_CtnStatus() => GetAsync<StorageFeatureStatus>(nameof(GetMeta_CtnStatus));
      /// <inheritdoc />
      public Task<StorageSettingsDetail> GetMeta_CtnSettings() => GetAsync<StorageSettingsDetail>(nameof(GetMeta_CtnSettings));
      /// <inheritdoc />
      public Task<StorageDirectoryValidation> PostGetMeta_CtnValidateDirectory(StorageFeatureSettings settings) => PostAsync<StorageDirectoryValidation>(nameof(PostGetMeta_CtnValidateDirectory), settings);
      /// <inheritdoc />
      public Task<StorageSettingsDetail> PostGetMeta_CtnSettingsSave(StorageSettingsSave request) => PostAsync<StorageSettingsDetail>(nameof(PostGetMeta_CtnSettingsSave), request);

      /// <inheritdoc />
      public Task<CtnStorageInfo> GetMeta_CtnStorageSize() =>
         GetAsync<CtnStorageInfo>(nameof(GetMeta_CtnStorageSize));

      /// <inheritdoc />
      public Task<CtnRootInfo[]> GetMeta_CtnRoots() =>
         GetAsync<CtnRootInfo[]>(nameof(GetMeta_CtnRoots));

      /// <inheritdoc />
      public Task<CtnRootInfo> PostGetMeta_CtnRootCreate(string name, string? description) =>
         PostAsync<CtnRootInfo>(nameof(PostGetMeta_CtnRootCreate), name, description!);

      /// <inheritdoc />
      public Task PostMeta_CtnRootUpdate(string rootId, string? description, bool isActive) =>
         PostAsync(nameof(PostMeta_CtnRootUpdate), rootId, description!, isActive);

      /// <inheritdoc />
      public Task PostMeta_CtnRootDelete(string rootId) =>
         PostAsync(nameof(PostMeta_CtnRootDelete), rootId);

      /// <inheritdoc />
      public Task<CtnTree> GetMeta_CtnTree(string rootId) =>
         GetAsync<CtnTree>(nameof(GetMeta_CtnTree), rootId);

      /// <inheritdoc />
      public Task<CtnFolderInfo> PostGetMeta_CtnFolderCreate(string rootId, string? parentFolderId, string name) =>
         PostAsync<CtnFolderInfo>(nameof(PostGetMeta_CtnFolderCreate), rootId, parentFolderId!, name);

      /// <inheritdoc />
      public Task<CtnFolderInfo> PostGetMeta_CtnFolderRename(string folderId, string name) =>
         PostAsync<CtnFolderInfo>(nameof(PostGetMeta_CtnFolderRename), folderId, name);

      /// <inheritdoc />
      public Task<CtnFolderInfo> PostGetMeta_CtnFolderMove(string folderId, string? targetParentFolderId) =>
         PostAsync<CtnFolderInfo>(nameof(PostGetMeta_CtnFolderMove), folderId, targetParentFolderId!);

      /// <inheritdoc />
      public Task PostMeta_CtnFolderDelete(string folderId) =>
         PostAsync(nameof(PostMeta_CtnFolderDelete), folderId);

      /// <inheritdoc />
      public Task<CtnImageInfo> PostGetMeta_CtnImageCreate(string rootId, string? folderId, string name, string? description) =>
         PostAsync<CtnImageInfo>(nameof(PostGetMeta_CtnImageCreate), rootId, folderId!, name, description!);

      /// <inheritdoc />
      public Task<CtnImageInfo> PostGetMeta_CtnImageMove(string imageId, string? targetFolderId) =>
         PostAsync<CtnImageInfo>(nameof(PostGetMeta_CtnImageMove), imageId, targetFolderId!);

      /// <inheritdoc />
      public Task PostMeta_CtnImageUpdate(string imageId, string? description, bool isActive) =>
         PostAsync(nameof(PostMeta_CtnImageUpdate), imageId, description!, isActive);

      /// <inheritdoc />
      public Task PostMeta_CtnImageDelete(string imageId) =>
         PostAsync(nameof(PostMeta_CtnImageDelete), imageId);

      /// <inheritdoc />
      public Task<CtnManifestInfo[]> GetMeta_CtnImageManifests(string imageId) =>
         GetAsync<CtnManifestInfo[]>(nameof(GetMeta_CtnImageManifests), imageId);

      /// <inheritdoc />
      public Task PostMeta_CtnTagDelete(string imageId, string tag) =>
         PostAsync(nameof(PostMeta_CtnTagDelete), imageId, tag);

      /// <inheritdoc />
      public Task PostMeta_CtnManifestDelete(string imageId, string manifestId) =>
         PostAsync(nameof(PostMeta_CtnManifestDelete), imageId, manifestId);

      #endregion

      #region Garbage collection

      /// <inheritdoc />
      public Task<CtnGcReport> GetMeta_CtnGcReview(int graceHours) =>
         GetAsync<CtnGcReport>(nameof(GetMeta_CtnGcReview), graceHours);

      /// <inheritdoc />
      public Task<CtnGcReport> PostGetMeta_CtnGcRun(int graceHours) =>
         PostAsync<CtnGcReport>(nameof(PostGetMeta_CtnGcRun), graceHours);

      #endregion

      #region Deploy

      // The server allows a deploy 15 minutes; the client waits a little longer so it hears the result.
      private static readonly TimeSpan DeployTimeout = TimeSpan.FromMinutes(16);

      /// <inheritdoc />
      public Task<CtnDeployTargetInfo?> GetMeta_CtnDeployTarget(string imageId) =>
         GetAsync<CtnDeployTargetInfo?>(nameof(GetMeta_CtnDeployTarget), imageId);

      /// <inheritdoc />
      public Task<CtnDeployTargetInfo> PostGetMeta_CtnDeployTargetSave(CtnDeployTargetSave request) =>
         PostAsync<CtnDeployTargetInfo>(nameof(PostGetMeta_CtnDeployTargetSave), request);

      /// <inheritdoc />
      public Task PostMeta_CtnDeployTargetDelete(string imageId) =>
         PostAsync(nameof(PostMeta_CtnDeployTargetDelete), imageId);

      /// <inheritdoc />
      public Task<CtnDeployTestResult> PostGetMeta_CtnDeployTest(CtnDeployTargetSave request) =>
         PostAsync<CtnDeployTestResult>(DeployTimeout, nameof(PostGetMeta_CtnDeployTest), request);

      /// <inheritdoc />
      public Task<CtnDeployTestResult> PostGetMeta_CtnDeployRegisterPortainerRegistry(string imageId) =>
         PostAsync<CtnDeployTestResult>(DeployTimeout, nameof(PostGetMeta_CtnDeployRegisterPortainerRegistry), imageId);

      /// <inheritdoc />
      public Task<CtnDeployRunInfo> PostGetMeta_CtnDeployCreateStack(string imageId, string composeContent) =>
         PostAsync<CtnDeployRunInfo>(DeployTimeout, nameof(PostGetMeta_CtnDeployCreateStack), imageId, composeContent);

      /// <inheritdoc />
      public Task<string> GetMeta_CtnDeployStackTemplate(string imageId) =>
         GetAsync<string>(nameof(GetMeta_CtnDeployStackTemplate), imageId);

      /// <inheritdoc />
      public Task<CtnDeployRunInfo> PostGetMeta_CtnDeployAfterPush(CtnDeployPushRequest request) =>
         PostAsync<CtnDeployRunInfo>(DeployTimeout, nameof(PostGetMeta_CtnDeployAfterPush), request);

      /// <inheritdoc />
      public Task<CtnDeployRunInfo> PostGetMeta_CtnDeployRun(string imageId, string digest, string? tag) =>
         PostAsync<CtnDeployRunInfo>(DeployTimeout, nameof(PostGetMeta_CtnDeployRun), imageId, digest, tag!);

      /// <inheritdoc />
      public Task<CtnDeployRunInfo> PostGetMeta_CtnDeployRollback(string imageId, string runId) =>
         PostAsync<CtnDeployRunInfo>(DeployTimeout, nameof(PostGetMeta_CtnDeployRollback), imageId, runId);

      /// <inheritdoc />
      public Task<CtnDeployRunInfo[]> GetMeta_CtnDeployRuns(string imageId, int take) =>
         GetAsync<CtnDeployRunInfo[]>(nameof(GetMeta_CtnDeployRuns), imageId, take);

      #endregion
   }
}
