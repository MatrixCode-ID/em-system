using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>Release Manager access to application-owned CDN folders.</summary>
public interface ICdnReleaseServices : IServices {
   /// <summary>Release management permission in Administrative Tools.</summary>
   const string ReleaseClaim = "Release Manager Access";
   /// <summary>Lists public entries for the release folder picker.</summary>
   Task<CdnFolderContent> GetMeta_CdnReleaseFolder(string? path);
   /// <summary>Reads a managed binary tree after checking ownership.</summary>
   Task<CdnEntry[]> GetMeta_CdnReleaseTree(string path, string appId);
   /// <summary>Creates or adopts an empty folder; repeated selection by its owner is allowed.</summary>
   Task<CdnManagedFolder> PostGetMeta_CdnReleaseManage(string path, string appId);
   /// <summary>Releases a folder owned by the product.</summary>
   Task PostMeta_CdnReleaseUnmanage(string path, string appId);
   /// <summary>Checks the complete final Sync size before any mutation.</summary>
   Task PostMeta_CdnReleasePreflight(CdnSyncPlan plan);
   /// <summary>Creates a folder within the owned root.</summary>
   Task<CdnEntry> PostGetMeta_CdnReleaseCreateFolder(string? path, string name, string appId);
   /// <summary>Uploads within the owned root without the ordinary per-file limit.</summary>
   Task<CdnEntry> PostGetMeta_CdnReleaseUpload(CdnUploadRequest request, string appId, Stream content);
   /// <summary>Deletes owned content without retaining it in the recycle bin.</summary>
   Task PostMeta_CdnReleaseDelete(string path, string appId);
}
