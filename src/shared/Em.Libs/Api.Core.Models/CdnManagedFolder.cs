namespace Em.Api.Core.Models;

/// <summary>A CDN folder owned by an application, with an optional byte quota.</summary>
public sealed class CdnManagedFolder {
   /// <summary>Canonical path relative to the public root.</summary>
   public string Path { get; set; } = "";
   /// <summary>Application owning the folder.</summary>
   public CdnFolderOwner Owner { get; set; } = new();
   /// <summary>Maximum live bytes, or null for unlimited.</summary>
   public long? QuotaBytes { get; set; }
   /// <summary>Current live bytes.</summary>
   public long UsedBytes { get; set; }
}

/// <summary>The kind and technical identity of a managed folder owner.</summary>
public sealed class CdnFolderOwner {
   /// <summary>Owner subsystem; currently release.</summary>
   public string Kind { get; set; } = "release";
   /// <summary>Product app_id.</summary>
   public string Id { get; set; } = "";
}

/// <summary>A deleted file or folder retained for manual recovery.</summary>
public sealed class CdnRecycleItem {
   /// <summary>Opaque recycle item identifier.</summary>
   public string Id { get; set; } = "";
   /// <summary>Original public relative path.</summary>
   public string Path { get; set; } = "";
   /// <summary>Deletion time.</summary>
   public DateTimeOffset DeletedAtUtc { get; set; }
   /// <summary>Authenticated deleting user.</summary>
   public string DeletedBy { get; set; } = "";
   /// <summary>Total payload bytes.</summary>
   public long Size { get; set; }
   /// <summary>Whether the payload is a folder.</summary>
   public bool IsFolder { get; set; }
}

/// <summary>Expected final files of a managed folder, for the preflight quota check.</summary>
public sealed class CdnSyncPlan {
   /// <summary>Managed root.</summary>
   public string Path { get; set; } = "";
   /// <summary>Owning product app_id.</summary>
   public string AppId { get; set; } = "";
   /// <summary>Final paths and sizes including the signed manifest and signature.</summary>
   public CdnSyncFile[] Files { get; set; } = [];
}

/// <summary>One final file of a Sync quota calculation.</summary>
public sealed class CdnSyncFile {
   /// <summary>Path within the managed folder.</summary>
   public string Path { get; set; } = "";
   /// <summary>Expected file size in bytes.</summary>
   public long Size { get; set; }
}
