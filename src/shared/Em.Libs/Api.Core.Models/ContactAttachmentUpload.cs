using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>Describes a file sent to add an attachment or replace its file.</summary>
public sealed class ContactAttachmentUpload
{
   /// <summary>Contact receiving a new attachment; ignored by a replace.</summary>
   public string ContactId { get; set; } = "";
   /// <summary>Attachment whose file is replaced; ignored when adding.</summary>
   public string AttachmentId { get; set; } = "";
   /// <summary>Last-read stamp of the replaced attachment.</summary>
   public DateTime Ustamp { get; set; }
   /// <summary>Original file name.</summary>
   public string FileName { get; set; } = "";
   /// <summary>Kind of a new attachment.</summary>
   public ContactAttachmentKind Kind { get; set; } = ContactAttachmentKind.Other;
}
