using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>Persistent ApprovalRequestAttachment approval data.</summary>
[Table("ta_ApprovalRequestAttachment")]
public class ta_ApprovalRequestAttachment
{
   /// <summary>Stored cApprovalRequestAttachmentId value.</summary>
   [Key]
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestAttachmentId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestId value.</summary>
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestCommentId value.</summary>
   [Column(TypeName = "char(26)")]
   public string? cApprovalRequestCommentId { get; set; }
   /// <summary>Stored cApprovalRequestAttachmentKey value.</summary>
   [Column(TypeName = "nvarchar(400)")]
   public string cApprovalRequestAttachmentKey { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestAttachmentVersion value.</summary>
   [Column(TypeName = "int")]
   public int cApprovalRequestAttachmentVersion { get; set; }
   /// <summary>Stored cApprovalRequestAttachmentName value.</summary>
   [Column(TypeName = "nvarchar(255)")]
   public string cApprovalRequestAttachmentName { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestAttachmentSize value.</summary>
   [Column(TypeName = "bigint")]
   public long cApprovalRequestAttachmentSize { get; set; }
   /// <summary>Stored cUserId value.</summary>
   [Column(TypeName = "char(26)")]
   public string cUserId { get; set; } = string.Empty;
   /// <summary>Stored ustamp value.</summary>
   [Column(TypeName = "datetime2")]
   public DateTime ustamp { get; set; }
   /// <summary>Stored datestamp value.</summary>
   [Column(TypeName = "datetime2")]
   public DateTime datestamp { get; set; }
   /// <summary>Stored json_object value.</summary>
   [Column(TypeName = "nvarchar(max)")]
   public string? json_object { get; set; }
}
