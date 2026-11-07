using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>A file attached to a contact, shared by every tenant and account using that contact.</summary>
[Table("ta_ContactAttachment")]
public class ta_ContactAttachment
{
   /// <summary>Active record.</summary>
   public const int StateActive = 0;
   /// <summary>Removed record; the file and its versions stay in object storage.</summary>
   public const int StateInactive = -1;
   /// <summary>Attachment ULID.</summary>
   [Key] [MaxLength(26)] public string cContactAttachmentId { get; set; } = "";
   /// <summary>Contact owning the attachment; cannot change after creation.</summary>
   [MaxLength(26)] public string cContactId { get; set; } = "";
   /// <summary>Object holding the file in object storage.</summary>
   [MaxLength(26)] public string cStoreObjectId { get; set; } = "";
   /// <summary>Display name; starts as the file name.</summary>
   [MaxLength(255)] public string cContactAttachmentName { get; set; } = "";
   /// <summary>What the file is.</summary>
   public ContactAttachmentKind cContactAttachmentKind { get; set; } = ContactAttachmentKind.Other;
   /// <summary>Optional note.</summary>
   [MaxLength(500)] public string? cContactAttachmentNote { get; set; }
   /// <summary>Active or removed state.</summary>
   public int cContactAttachmentState { get; set; }
   /// <summary>Display order within the contact.</summary>
   public int cContactAttachmentOrder { get; set; }
   /// <summary>Last update time in UTC, used for optimistic concurrency.</summary>
   public DateTime ustamp { get; set; }
   /// <summary>Creation time in UTC.</summary>
   public DateTime datestamp { get; set; }
   /// <summary>Optional extension data.</summary>
   public string? json_object { get; set; }
}
