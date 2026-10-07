namespace Em.Api.Core.Models
{
   /// <summary>Registry payload size based on metadata, not disk volume usage.</summary>
   public class CtnStorageInfo
   {
      /// <summary>Total size of the blobs, in bytes.</summary>
      public long BlobBytes { get; set; }

      /// <summary>Total size of the manifests, in bytes.</summary>
      public long ManifestBytes { get; set; }

      /// <summary>Sum of <see cref="BlobBytes"/> and <see cref="ManifestBytes"/>.</summary>
      public long TotalBytes => BlobBytes + ManifestBytes;
   }

   // Every time in the registry DTOs is UTC; screens convert it to local time for display.

   /// <summary>One container registry root namespace (e.g. <c>server</c>, <c>acme</c>).</summary>
   public class CtnRootInfo
   {
      /// <summary>Root ID.</summary>
      public string Id { get; set; } = "";

      /// <summary>Root name, the first part of the pull name (<c>host/root/name</c>).</summary>
      public string Name { get; set; } = "";

      /// <summary>Optional description.</summary>
      public string? Description { get; set; }

      /// <summary><c>false</c> = inactive: every push and pull in this root is rejected.</summary>
      public bool IsActive { get; set; }

      /// <summary>Number of folders in this root.</summary>
      public int FolderCount { get; set; }

      /// <summary>Number of containers in this root.</summary>
      public int ImageCount { get; set; }

      /// <summary>When the root was created.</summary>
      public DateTime CreatedAt { get; set; }
   }

   /// <summary>Grouping folder inside a root. Does not appear in the pull name.</summary>
   public class CtnFolderInfo
   {
      /// <summary>Folder ID.</summary>
      public string Id { get; set; } = "";

      /// <summary>Root this folder belongs to.</summary>
      public string RootId { get; set; } = "";

      /// <summary>Parent folder; <c>null</c> when directly under the root.</summary>
      public string? ParentId { get; set; }

      /// <summary>Folder name.</summary>
      public string Name { get; set; } = "";
   }

   /// <summary>One named container (<c>root/name</c>), a leaf of the tree.</summary>
   public class CtnImageInfo
   {
      /// <summary>Container ID.</summary>
      public string Id { get; set; } = "";

      /// <summary>Root this container belongs to.</summary>
      public string RootId { get; set; } = "";

      /// <summary>Name of the root.</summary>
      public string RootName { get; set; } = "";

      /// <summary>Folder holding it; <c>null</c> when directly under the root.</summary>
      public string? FolderId { get; set; }

      /// <summary>Container name within its root.</summary>
      public string Name { get; set; } = "";

      /// <summary>Pull name without host: <c>root/name</c>.</summary>
      public string FullName { get; set; } = "";

      /// <summary>Optional description.</summary>
      public string? Description { get; set; }

      /// <summary><c>false</c> = inactive: push and pull are rejected.</summary>
      public bool IsActive { get; set; }

      /// <summary>Number of tags.</summary>
      public int TagCount { get; set; }

      /// <summary>Number of manifests.</summary>
      public int ManifestCount { get; set; }

      /// <summary>When the container was created.</summary>
      public DateTime CreatedAt { get; set; }
   }

   /// <summary>Everything in a root's tree: folders and containers as flat lists.</summary>
   public class CtnTree
   {
      /// <summary>Every folder of the root.</summary>
      public CtnFolderInfo[] Folders { get; set; } = [];

      /// <summary>Every container of the root.</summary>
      public CtnImageInfo[] Images { get; set; } = [];
   }

   /// <summary>One manifest of a container plus the tags pointing at it.</summary>
   public class CtnManifestInfo
   {
      /// <summary>Manifest ID.</summary>
      public string Id { get; set; } = "";

      /// <summary>Manifest digest (<c>sha256:...</c>).</summary>
      public string Digest { get; set; } = "";

      /// <summary>Manifest media type.</summary>
      public string MediaType { get; set; } = "";

      /// <summary>Size of the manifest itself, not of its layers.</summary>
      public long Size { get; set; }

      /// <summary>When the manifest was pushed.</summary>
      public DateTime PushedAt { get; set; }

      /// <summary>Name of the robot that pushed it; <c>null</c> when the robot has been deleted.</summary>
      public string? PushedBy { get; set; }

      /// <summary>Tags pointing at this manifest.</summary>
      public string[] Tags { get; set; } = [];

      /// <summary>Number of blobs (config and layers) referenced by this manifest.</summary>
      public int BlobCount { get; set; }

      /// <summary>
      /// Number of blobs recorded in the database whose files are missing from server storage (or have a
      /// different size). A container with a value above zero cannot be pulled until the files are restored.
      /// </summary>
      public int MissingBlobCount { get; set; }
   }

   /// <summary>
   /// Result of a registry garbage collection review (dry run) or run. All sizes are in bytes;
   /// <see cref="Blobs"/> is limited to <see cref="MaxListedBlobs"/> rows, the actual count is in <see cref="BlobCount"/>.
   /// </summary>
   public class CtnGcReport
   {
      /// <summary>Maximum number of rows in <see cref="Blobs"/>.</summary>
      public const int MaxListedBlobs = 1000;

      /// <summary><c>true</c> = review only, nothing is deleted.</summary>
      public bool DryRun { get; set; }

      /// <summary>Grace period in hours used for this run.</summary>
      public int GraceHours { get; set; }

      /// <summary>UTC cutoff: only items older than this are deleted.</summary>
      public DateTime CutoffUtc { get; set; }

      /// <summary>Orphan blobs, at most <see cref="MaxListedBlobs"/> rows.</summary>
      public CtnGcBlob[] Blobs { get; set; } = [];

      /// <summary>Actual number of orphan blobs.</summary>
      public int BlobCount { get; set; }

      /// <summary>Total size of the orphan blobs.</summary>
      public long BlobBytes { get; set; }

      /// <summary>Number of abandoned uploads older than the cutoff.</summary>
      public int StaleUploadCount { get; set; }

      /// <summary>Total size of the abandoned uploads.</summary>
      public long StaleUploadBytes { get; set; }

      /// <summary>Files in the blob folder without a metadata row.</summary>
      public int OrphanBlobFileCount { get; set; }

      /// <summary>Total size of <see cref="OrphanBlobFileCount"/>.</summary>
      public long OrphanBlobFileBytes { get; set; }

      /// <summary>Files in the upload folder without a metadata row.</summary>
      public int OrphanUploadFileCount { get; set; }

      /// <summary>Total size of <see cref="OrphanUploadFileCount"/>.</summary>
      public long OrphanUploadFileBytes { get; set; }

      /// <summary>Total size reclaimed (or reclaimable on a dry run).</summary>
      public long TotalBytes => BlobBytes + StaleUploadBytes + OrphanBlobFileBytes + OrphanUploadFileBytes;

      /// <summary>Items skipped or not deleted, as short English sentences.</summary>
      public string[] Warnings { get; set; } = [];
   }

   /// <summary>One orphan blob in <see cref="CtnGcReport"/>.</summary>
   public class CtnGcBlob
   {
      /// <summary>Blob digest.</summary>
      public string Digest { get; set; } = "";

      /// <summary>Blob size in bytes.</summary>
      public long Size { get; set; }

      /// <summary>When the blob was recorded.</summary>
      public DateTime CreatedAt { get; set; }

      /// <summary>Containers (<c>root/name</c>) still linking it through an old link; empty when none.</summary>
      public string[] LinkedImages { get; set; } = [];
   }
}
