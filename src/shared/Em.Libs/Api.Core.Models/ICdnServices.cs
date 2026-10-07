using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// CDN content management: browsing folders, uploading files, creating folders, deleting, and creating
   /// and extracting zip files. Every action here requires claim <see cref="CdnClaim"/> in module
   /// <see cref="Defaults.AdministrativeToolsModuleName"/>, and answers 404 when the CDN is not enabled on
   /// the server. Downloads do not go through here but through the public address <c>/cdn/...</c>.
   /// </summary>
   /// <remarks>
   /// Every <c>path</c> parameter is a path relative to the CDN root folder, separated by <c>/</c>;
   /// <c>null</c> or an empty string means the root folder. Paths that try to leave the root folder, or
   /// that name an entry starting with a dot, are rejected with 400.
   /// <para>
   /// Creating and extracting zips can take a long time, so both run as global business tasks on the
   /// server: the action returns immediately, and the status is monitored through the status actions
   /// below. Every holder of the CDN claim - administrator or not - may view, cancel and clear those
   /// tasks, including ones started by other users. Only one archive may run on the whole server, and one
   /// zip file cannot be extracted twice at the same time.
   /// </para>
   /// </remarks>
   public interface ICdnServices : IServices
   {
      /// <summary>
      /// Claim name that opens CDN management, written without its module name.
      /// </summary>
      const string CdnClaim = "CDN Manager Access";

      /// <summary>Claim name that allows changing the CDN storage settings, written without its module name.</summary>
      const string SettingsClaim = "CDN Settings Manage";

      /// <summary>Whether the CDN is enabled, managed from the UI, and waiting for a restart.</summary>
      Task<StorageFeatureStatus> GetMeta_CdnStatus();

      /// <summary>Current CDN storage settings. Requires <see cref="SettingsClaim"/>.</summary>
      Task<StorageSettingsDetail> GetMeta_CdnSettings();

      /// <summary>Validates a storage directory without saving it. Requires <see cref="SettingsClaim"/>.</summary>
      Task<StorageDirectoryValidation> PostGetMeta_CdnValidateDirectory(StorageFeatureSettings settings);

      /// <summary>Saves the CDN storage settings; they take effect after the API restarts. Requires <see cref="SettingsClaim"/>.</summary>
      Task<StorageSettingsDetail> PostGetMeta_CdnSettingsSave(StorageSettingsSave request);

      #region Meta's

      /// <summary>Total size of the public files across the CDN. Ignores internal/temporary,
      /// hidden/system files and symlinks. Does not include filesystem overhead or free volume space.</summary>
      Task<CdnStorageInfo> GetMeta_CdnStorageSize();

      /// <summary>Contents of one folder plus its upload limit and public address.</summary>
      Task<CdnFolderContent> GetMeta_CdnFolder(string? path);

      /// <summary>Number of files and subfolders inside a folder, counted down to the deepest level.</summary>
      Task<CdnItemCount> GetMeta_CdnItemCount(string path);

      /// <summary>
      /// Everything inside a folder down to the deepest subfolder, as one flat list: every folder appears
      /// before its contents. Used to copy a whole folder out of the CDN.
      /// </summary>
      Task<CdnEntry[]> GetMeta_CdnTree(string path);

      /// <summary>
      /// Uploads one file to a folder. The content is sent as a stream, so its size is limited neither by
      /// memory nor by the request timeout - only by the CDN upload size limit. Rejected with 409 when the
      /// name exists and <see cref="CdnUploadRequest.Overwrite"/> is <c>false</c>, 400 when the name or
      /// folder is invalid, and 413 when it exceeds the upload size limit. Rejections for names or
      /// conflicts are answered before the content is read; an upload interrupted midway leaves nothing on
      /// the CDN.
      /// </summary>
      /// <param name="request">Target folder, file name and overwrite choice.</param>
      /// <param name="content">File content, read from start to end.</param>
      Task<CdnEntry> PostGetMeta_CdnUpload(CdnUploadRequest request, Stream content);

      /// <summary>
      /// Moves a file or folder to another folder under the same name. Rejected with 409 when the name is
      /// already used in the target folder - for files, unless <paramref name="overwrite"/> is
      /// <c>true</c>; folders are never overwritten. A folder cannot be moved into itself (400). Returns the
      /// CDN item at its new location.
      /// </summary>
      Task<CdnEntry> PostGetMeta_CdnMove(string path, string? targetFolder, bool overwrite);

      /// <summary>Creates a new subfolder; rejected with 409 when the name already exists.</summary>
      Task<CdnEntry> PostGetMeta_CdnCreateFolder(string? path, string folderName);

      /// <summary>
      /// Deletes a file, or a folder with all its contents. The root folder cannot be deleted (400); a
      /// missing item answers 404.
      /// </summary>
      Task PostMeta_CdnDelete(string path);

      /// <summary>
      /// Starts creating a zip file from the selected items. Names, sources and conflicts are checked
      /// before the task starts: 400 for invalid names, 404 for missing sources, and 409 when the zip name
      /// exists without <see cref="CdnArchiveRequest.Overwrite"/>, or when another archive is still running
      /// (the message names who started it). Dot folders inside the sources are skipped.
      /// </summary>
      /// <returns>Snapshot of the newly started task.</returns>
      Task<BusinessTaskInfo> PostGetMeta_CdnArchive(CdnArchiveRequest request);

      /// <summary>
      /// The archive task that is still alive, or that failed and has not been cleared; <c>null</c> when none.
      /// </summary>
      Task<BusinessTaskInfo?> GetMeta_CdnArchiveTask();

      /// <summary>Cancels the running archive. 404 when there is none, 409 when it has finished.</summary>
      Task PostMeta_CdnArchiveCancel();

      /// <summary>Clears a finished archive (usually a failed one). 409 while it is still running.</summary>
      Task PostMeta_CdnArchiveClear();

      /// <summary>
      /// Checks a zip file before extracting it into the folder that holds it, and returns the paths of
      /// the files that would be overwritten. Zips whose entries try to leave the folder, use dot or
      /// invalid names, have too many entries, or clash with existing folders are rejected with 400.
      /// </summary>
      /// <param name="path">Path of the zip file.</param>
      Task<string[]> GetMeta_CdnExtractConflicts(string path);

      /// <summary>
      /// Starts extracting a zip file into the folder that holds it. Checks are the same as
      /// <see cref="GetMeta_CdnExtractConflicts"/>; plus 409 when files would be overwritten but
      /// <paramref name="overwrite"/> is <c>false</c>, or when the same zip is already being extracted.
      /// Each entry is limited by the CDN upload size limit, and the total by ten times that limit. A
      /// canceled or failed extraction leaves nothing on the CDN.
      /// </summary>
      /// <returns>Snapshot of the newly started task.</returns>
      Task<BusinessTaskInfo> PostGetMeta_CdnExtract(string path, bool overwrite);

      /// <summary>
      /// Extraction tasks for zip files directly in <paramref name="folder"/> that are still alive, or that
      /// failed and have not been cleared. <c>null</c> or an empty string means the root folder.
      /// </summary>
      Task<BusinessTaskInfo[]> GetMeta_CdnExtractTasks(string? folder);

      /// <summary>Cancels extracting the zip file at <paramref name="path"/>.</summary>
      Task PostMeta_CdnExtractCancel(string path);

      /// <summary>Clears the finished extraction task of the zip file at <paramref name="path"/>.</summary>
      Task PostMeta_CdnExtractClear(string path);

      #endregion
   }
}
