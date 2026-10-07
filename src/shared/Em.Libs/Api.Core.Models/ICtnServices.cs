using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Container registry management: roots, folders and containers. Every action here requires claim
   /// <see cref="CtnClaim"/> in module <see cref="Defaults.AdministrativeToolsModuleName"/>, and answers
   /// 404 when the registry is not enabled on the server. Image push and pull do not go through here but
   /// through the <c>/v2</c> endpoint.
   /// </summary>
   /// <remarks>
   /// A push never creates a root, folder or container name: they are all created through this service
   /// first. A pull name always has two segments (<c>host/root/name</c>); folders are pure grouping and do
   /// not appear in the pull name.
   /// <para>
   /// Robot identities and grants are managed through <see cref="IRobotServices"/> with claim
   /// User Manager Access.
   /// </para>
   /// </remarks>
   public interface ICtnServices : IServices
   {
      /// <summary>Claim name that opens registry management, written without its module name.</summary>
      const string CtnClaim = "Container Manager Access";

      /// <summary>Size of the unique stored blobs and manifests across the registry, including orphan blobs.
      /// Excludes temporary uploads and database/filesystem overhead; based on registry metadata.</summary>
      Task<CtnStorageInfo> GetMeta_CtnStorageSize();

      /// <summary>Claim name that allows changing the registry storage settings, written without its module name.</summary>
      const string SettingsClaim = "Container Registry Settings Manage";

      /// <summary>Whether the registry is enabled, managed from the UI, and waiting for a restart.</summary>
      Task<StorageFeatureStatus> GetMeta_CtnStatus();

      /// <summary>Current registry storage settings. Requires <see cref="SettingsClaim"/>.</summary>
      Task<StorageSettingsDetail> GetMeta_CtnSettings();

      /// <summary>Validates a storage directory without saving it. Requires <see cref="SettingsClaim"/>.</summary>
      Task<StorageDirectoryValidation> PostGetMeta_CtnValidateDirectory(StorageFeatureSettings settings);

      /// <summary>Saves the registry storage settings; they take effect after the API restarts. Requires <see cref="SettingsClaim"/>.</summary>
      Task<StorageSettingsDetail> PostGetMeta_CtnSettingsSave(StorageSettingsSave request);

      #region Root

      /// <summary>Every root with its folder and container counts.</summary>
      Task<CtnRootInfo[]> GetMeta_CtnRoots();

      /// <summary>Creates a root. 400 for an invalid name, 409 when the name is already used.</summary>
      Task<CtnRootInfo> PostGetMeta_CtnRootCreate(string name, string? description);

      /// <summary>Changes the description and active state of a root.</summary>
      Task PostMeta_CtnRootUpdate(string rootId, string? description, bool isActive);

      /// <summary>Deletes a root. 409 while it still has folders, containers or robot grants.</summary>
      Task PostMeta_CtnRootDelete(string rootId);

      #endregion

      #region Folders and containers

      /// <summary>Every folder and container of a root.</summary>
      Task<CtnTree> GetMeta_CtnTree(string rootId);

      /// <summary>Creates a folder in the root (<paramref name="parentFolderId"/> empty) or inside another folder.</summary>
      Task<CtnFolderInfo> PostGetMeta_CtnFolderCreate(string rootId, string? parentFolderId, string name);

      /// <summary>Renames a folder; 409 when a sibling already uses the name.</summary>
      Task<CtnFolderInfo> PostGetMeta_CtnFolderRename(string folderId, string name);

      /// <summary>
      /// Moves a folder with its contents to another folder in the same root (or to the root when
      /// <paramref name="targetParentFolderId"/> is empty). Pull names do not change.
      /// </summary>
      Task<CtnFolderInfo> PostGetMeta_CtnFolderMove(string folderId, string? targetParentFolderId);

      /// <summary>Deletes a folder; 409 while it still holds folders or containers.</summary>
      Task PostMeta_CtnFolderDelete(string folderId);

      /// <summary>
      /// Creates a named container in the target root and folder. Only then can it be pushed. 400 for an
      /// invalid or too long name, 409 when the name is already used in that root.
      /// </summary>
      Task<CtnImageInfo> PostGetMeta_CtnImageCreate(string rootId, string? folderId, string name, string? description);

      /// <summary>Moves a container to another folder in the same root. Does not copy blobs or change the pull name.</summary>
      Task<CtnImageInfo> PostGetMeta_CtnImageMove(string imageId, string? targetFolderId);

      /// <summary>Changes the description and active state of a container.</summary>
      Task PostMeta_CtnImageUpdate(string imageId, string? description, bool isActive);

      /// <summary>
      /// Deletes a container with its manifests, tags and blob links. Blob files on disk are cleaned up by
      /// <see cref="PostGetMeta_CtnGcRun"/>.
      /// </summary>
      Task PostMeta_CtnImageDelete(string imageId);

      /// <summary>Manifests of a container with their tags, newest first.</summary>
      Task<CtnManifestInfo[]> GetMeta_CtnImageManifests(string imageId);

      /// <summary>
      /// Deletes one tag. Its manifest stays and can still be pulled by digest. 404 when the tag does not exist.
      /// </summary>
      Task PostMeta_CtnTagDelete(string imageId, string tag);

      /// <summary>
      /// Deletes one manifest with the tags pointing at it. Metadata only; its blobs become garbage
      /// collection candidates. 404 when missing, 409 while a manifest list/index in the same container still references it.
      /// </summary>
      Task PostMeta_CtnManifestDelete(string imageId, string manifestId);

      #endregion

      #region Garbage collection

      /// <summary>Default grace period (hours) for garbage collection.</summary>
      const int GcDefaultGraceHours = 24;

      /// <summary>Largest accepted grace period (hours).</summary>
      const int GcMaxGraceHours = 720;

      /// <summary>
      /// Dry run: what garbage collection would delete with grace period <paramref name="graceHours"/>
      /// (1..<see cref="GcMaxGraceHours"/>, otherwise 400). Changes nothing.
      /// </summary>
      Task<CtnGcReport> GetMeta_CtnGcReview(int graceHours);

      /// <summary>
      /// Runs garbage collection: orphan blobs, stale uploads and files without metadata older than the
      /// grace period. The report lists what was actually deleted. 409 while another GC is running.
      /// </summary>
      Task<CtnGcReport> PostGetMeta_CtnGcRun(int graceHours);

      #endregion

      #region Deploy

      /// <summary>Largest number of runs <see cref="GetMeta_CtnDeployRuns"/> returns.</summary>
      const int DeployMaxRuns = 100;

      /// <summary>The deploy target of a container, or <c>null</c> when it has none. 404 when the container does not exist.</summary>
      Task<CtnDeployTargetInfo?> GetMeta_CtnDeployTarget(string imageId);

      /// <summary>
      /// Creates or updates the deploy target of <see cref="CtnDeployTargetSave.ImageId"/> (one per container).
      /// 400 for invalid fields, 404 when the container does not exist.
      /// </summary>
      Task<CtnDeployTargetInfo> PostGetMeta_CtnDeployTargetSave(CtnDeployTargetSave request);

      /// <summary>Deletes the deploy target of a container and its run history. 404 when there is none.</summary>
      Task PostMeta_CtnDeployTargetDelete(string imageId);

      /// <summary>
      /// Tests the connection described by <paramref name="request"/> without deploying anything. Secrets left
      /// <c>null</c> use the stored target's. With no pinned fingerprint the server's is returned in
      /// <see cref="CtnDeployTestResult.OfferedFingerprint"/> and nothing else is run.
      /// </summary>
      Task<CtnDeployTestResult> PostGetMeta_CtnDeployTest(CtnDeployTargetSave request);

      /// <summary>
      /// Adds the target's registry host to Portainer as a custom registry, with the target's registry login.
      /// 400 when the target is not a Portainer target or has no registry login.
      /// </summary>
      Task<CtnDeployTestResult> PostGetMeta_CtnDeployRegisterPortainerRegistry(string imageId);

      /// <summary>
      /// Creates the compose file (SSH) or the stack (Portainer) from <paramref name="composeContent"/>, then
      /// stores the stack and its first service in the target, which switches to Stack mode. 409 when the
      /// compose file already exists on the SSH host.
      /// </summary>
      Task<CtnDeployRunInfo> PostGetMeta_CtnDeployCreateStack(string imageId, string composeContent);

      /// <summary>Compose template for Create stack: one service with the image variable and the container name.</summary>
      Task<string> GetMeta_CtnDeployStackTemplate(string imageId);

      /// <summary>
      /// Called by the publisher after a successful push. Deploys when the container has an active target whose
      /// tag filter matches one of the pushed tags; otherwise answers <see cref="CtnDeployResult.Skipped"/>
      /// without storing a run. A failed deploy is answered with 200 and <see cref="CtnDeployResult.Failed"/>.
      /// </summary>
      Task<CtnDeployRunInfo> PostGetMeta_CtnDeployAfterPush(CtnDeployPushRequest request);

      /// <summary>
      /// Deploys <paramref name="digest"/> of the container now, ignoring the tag filter. 404 without a target or
      /// when the digest is not a manifest of the container, 409 while another deploy of the target runs.
      /// </summary>
      Task<CtnDeployRunInfo> PostGetMeta_CtnDeployRun(string imageId, string digest, string? tag);

      /// <summary>Deploys the digest of an earlier successful run again. 400 when that run did not succeed.</summary>
      Task<CtnDeployRunInfo> PostGetMeta_CtnDeployRollback(string imageId, string runId);

      /// <summary>Latest runs first, at most <paramref name="take"/> (1..<see cref="DeployMaxRuns"/>).</summary>
      Task<CtnDeployRunInfo[]> GetMeta_CtnDeployRuns(string imageId, int take);

      #endregion
   }
}
