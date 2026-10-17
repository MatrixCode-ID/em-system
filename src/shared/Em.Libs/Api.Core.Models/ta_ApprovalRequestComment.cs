using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>Persistent ApprovalRequestComment approval data.</summary>
[Table("ta_ApprovalRequestComment")]
public class ta_ApprovalRequestComment
{
   /// <summary>Stored cApprovalRequestCommentId value.</summary>
   [Key]
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestCommentId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestId value.</summary>
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestId { get; set; } = string.Empty;
   /// <summary>Stored cUserId value.</summary>
   [Column(TypeName = "char(26)")]
   public string cUserId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestCommentText value.</summary>
   [Column(TypeName = "nvarchar(1000)")]
   public string cApprovalRequestCommentText { get; set; } = string.Empty;
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
