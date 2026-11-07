namespace Em.Api.Core.Models;

/// <summary>An attachment with the file details read from object storage.</summary>
public sealed class ContactAttachmentInfo
{
   /// <summary>The attachment record.</summary>
   public ta_ContactAttachment Row { get; set; } = new();
   /// <summary>Original name of the current file; null when object storage has no such file.</summary>
   public string? FileName { get; set; }
   /// <summary>MIME type of the current file.</summary>
   public string? Mime { get; set; }
   /// <summary>Size of the current file in bytes.</summary>
   public long Size { get; set; }
   /// <summary>When the current file was uploaded, in UTC.</summary>
   public DateTime? UploadedAt { get; set; }
   /// <summary>Version of the current file; replacing the file adds a version.</summary>
   public int Version { get; set; }
}
