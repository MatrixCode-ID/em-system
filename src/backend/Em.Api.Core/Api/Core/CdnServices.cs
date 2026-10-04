using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Management side of the CDN. Serving files is not here - that is the public <c>/cdn</c> route in
   /// <see cref="EmApp.Run"/> - only browsing, uploading, creating folders, deleting, and creating and
   /// extracting zips, each behind <see cref="ICdnServices.CdnClaim"/>. All file-system work is delegated
   /// to <see cref="CdnStore"/>. Archive and extract run as global business tasks, so every holder of the
   /// CDN claim sees, cancels and clears them - whoever started them.
   /// </summary>
   [Module(Defaults.AdministrativeToolsModuleName)]
   public class CdnServices : ServicesBase, ICdnServices
   {
      private Em.Api.Core.Storage.ManagedStorageSettings Settings => GetService<Em.Api.Core.Storage.ManagedStorageSettings>()!;
      [GetAction(claim: ICdnServices.CdnClaim)]
      public Task<StorageFeatureStatus> GetMeta_CdnStatus() => Task.FromResult(Settings.Status(true));
      [GetAction(claim: ICdnServices.SettingsClaim)]
      public Task<StorageSettingsDetail> GetMeta_CdnSettings() => Task.FromResult(Settings.Detail(true));
      [PostAction(claim: ICdnServices.SettingsClaim)]
      public Task<StorageDirectoryValidation> PostGetMeta_CdnValidateDirectory(StorageFeatureSettings settings) => Task.FromResult(Settings.Validate(true, settings));
      [PostAction(claim: ICdnServices.SettingsClaim)]
      public Task<StorageSettingsDetail> PostGetMeta_CdnSettingsSave(StorageSettingsSave request) => Task.FromResult(Settings.Save(true, request));

      private CdnStore Store {
         get {
            var store = GetService<CdnStore>()!;
            store.EnsureEnabled();
            return store;
         }
      }

      #region Meta's

      [GetAction(claim: ICdnServices.CdnClaim)]
      public Task<CdnStorageInfo> GetMeta_CdnStorageSize() =>
         Task.FromResult(Store.StorageSize(AbortToken));

      [GetAction(claim: ICdnServices.CdnClaim)]
      public Task<CdnFolderContent> GetMeta_CdnFolder(string? path) =>
         Task.FromResult(Store.List(path));

      [GetAction(claim: ICdnServices.CdnClaim)]
      public Task<CdnItemCount> GetMeta_CdnItemCount(string path) =>
         Task.FromResult(Store.CountInside(path));

      [GetAction(claim: ICdnServices.CdnClaim)]
      public Task<CdnEntry[]> GetMeta_CdnTree(string path) =>
         Task.FromResult(Store.Tree(path));

      [PostAction(claim: ICdnServices.CdnClaim)]
      public Task<CdnEntry> PostGetMeta_CdnMove(string path, string? targetFolder, bool overwrite) =>
         Task.FromResult(Store.Move(path, targetFolder, overwrite));

      [PostAction(claim: ICdnServices.CdnClaim)]
      public Task<CdnEntry> PostGetMeta_CdnUpload(CdnUploadRequest request, Stream content) =>
         Store.UploadAsync(request.Path, request.FileName, content, request.Overwrite, AbortToken);

      [PostAction(claim: ICdnServices.CdnClaim)]
      public Task<CdnEntry> PostGetMeta_CdnCreateFolder(string? path, string folderName) =>
         Task.FromResult(Store.CreateFolder(path, folderName));

      [PostAction(claim: ICdnServices.CdnClaim)]
      public Task PostMeta_CdnDelete(string path) {
         Store.Delete(path);
         return Task.CompletedTask;
      }

      // One archive at a time across the whole server; extracts are keyed per zip, so several zips may be
      // extracted at once but never the same one twice.
      private const string ArchiveTaskKey = "cdn:archive";
      private const string ExtractTaskKeyPrefix = "cdn:extract:";

      private string ExtractTaskKey(string path) => ExtractTaskKeyPrefix + Store.NormalizePath(path);

      [PostAction(claim: ICdnServices.CdnClaim)]
      public Task<BusinessTaskInfo> PostGetMeta_CdnArchive(CdnArchiveRequest request) {
         ArgumentNullException.ThrowIfNull(request);
         Store.ValidateArchive(request.Folder, request.Names, request.ArchiveName, request.Overwrite);

         // Copied out of the request: the work runs after this action has returned.
         var folder = request.Folder;
         var names = request.Names.ToArray();
         var archiveName = request.ArchiveName;
         var overwrite = request.Overwrite;

         return Task.FromResult(StartBusinessTask(new BusinessTaskOptions {
            Key = ArchiveTaskKey,
            Scope = BusinessTaskScope.Global,
            Title = $"Archive {archiveName}"
         }, ctx => ctx.Services.GetRequiredService<CdnStore>()
            .ArchiveAsync(folder, names, archiveName, overwrite, ctx.Report, ctx.CancellationToken)));
      }

      [GetAction(claim: ICdnServices.CdnClaim)]
      public Task<BusinessTaskInfo?> GetMeta_CdnArchiveTask() {
         Store.EnsureEnabled();
         return Task.FromResult(FindBusinessTask(ArchiveTaskKey));
      }

      [PostAction(claim: ICdnServices.CdnClaim)]
      public Task PostMeta_CdnArchiveCancel() {
         Store.EnsureEnabled();
         CancelBusinessTask(ArchiveTaskKey);
         return Task.CompletedTask;
      }

      [PostAction(claim: ICdnServices.CdnClaim)]
      public Task PostMeta_CdnArchiveClear() {
         Store.EnsureEnabled();
         ClearBusinessTask(ArchiveTaskKey);
         return Task.CompletedTask;
      }

      [GetAction(claim: ICdnServices.CdnClaim)]
      public Task<string[]> GetMeta_CdnExtractConflicts(string path) =>
         Task.FromResult(Store.ReadExtractPlan(path).Conflicts);

      [PostAction(claim: ICdnServices.CdnClaim)]
      public Task<BusinessTaskInfo> PostGetMeta_CdnExtract(string path, bool overwrite) {
         var plan = Store.ReadExtractPlan(path);
         if (plan.Conflicts.Length > 0 && !overwrite) {
            throw new ActionException($"{plan.Conflicts.Length:N0} existing file(s) would be overwritten.", 409);
         }

         var zipPath = Store.NormalizePath(path);
         return Task.FromResult(StartBusinessTask(new BusinessTaskOptions {
            Key = ExtractTaskKeyPrefix + zipPath,
            Scope = BusinessTaskScope.Global,
            Title = $"Extract {Path.GetFileName(plan.ZipPath)}"
         }, ctx => ctx.Services.GetRequiredService<CdnStore>()
            .ExtractAsync(zipPath, overwrite, ctx.Report, ctx.CancellationToken)));
      }

      [GetAction(claim: ICdnServices.CdnClaim)]
      public Task<BusinessTaskInfo[]> GetMeta_CdnExtractTasks(string? folder) {
         var normalized = Store.NormalizePath(folder);
         var prefix = normalized.Length == 0 ? ExtractTaskKeyPrefix : $"{ExtractTaskKeyPrefix}{normalized}/";

         // The prefix also matches zips in subfolders; only the ones directly in this folder are wanted.
         return Task.FromResult(FindBusinessTasks(prefix)
            .Where(r => !r.Key[prefix.Length..].Contains('/'))
            .ToArray());
      }

      [PostAction(claim: ICdnServices.CdnClaim)]
      public Task PostMeta_CdnExtractCancel(string path) {
         CancelBusinessTask(ExtractTaskKey(path));
         return Task.CompletedTask;
      }

      [PostAction(claim: ICdnServices.CdnClaim)]
      public Task PostMeta_CdnExtractClear(string path) {
         ClearBusinessTask(ExtractTaskKey(path));
         return Task.CompletedTask;
      }

      #endregion
   }
}
