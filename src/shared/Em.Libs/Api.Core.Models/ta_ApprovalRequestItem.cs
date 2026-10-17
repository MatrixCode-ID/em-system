using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>Persistent ApprovalRequestItem approval data.</summary>
[Table("ta_ApprovalRequestItem")]
public class ta_ApprovalRequestItem
{
   /// <summary>Stored cApprovalRequestItemId value.</summary>
   [Key]
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestItemId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestId value.</summary>
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestItemOrder value.</summary>
   [Column(TypeName = "int")]
   public int cApprovalRequestItemOrder { get; set; }
   /// <summary>Stored cApprovalRequestItemEntity value.</summary>
   [Column(TypeName = "nvarchar(400)")]
   public string cApprovalRequestItemEntity { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestItemOperation value.</summary>
   [Column(TypeName = "int")]
   public ApprovalItemOperation cApprovalRequestItemOperation { get; set; }
   /// <summary>Stored cSubjectKey value.</summary>
   [Column(TypeName = "nvarchar(400)")]
   public string? cSubjectKey { get; set; }
   /// <summary>Stored cParentItem value.</summary>
   [Column(TypeName = "char(26)")]
   public string? cParentItem { get; set; }
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
