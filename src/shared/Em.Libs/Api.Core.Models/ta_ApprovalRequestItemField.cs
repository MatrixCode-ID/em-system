using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>Persistent ApprovalRequestItemField approval data.</summary>
[Table("ta_ApprovalRequestItemField")]
public class ta_ApprovalRequestItemField
{
   /// <summary>Stored cApprovalRequestItemFieldId value.</summary>
   [Key]
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestItemFieldId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestItemId value.</summary>
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestItemId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestItemFieldName value.</summary>
   [Column(TypeName = "nvarchar(128)")]
   public string cApprovalRequestItemFieldName { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestItemFieldOld value.</summary>
   [Column(TypeName = "nvarchar(max)")]
   public string? cApprovalRequestItemFieldOld { get; set; }
   /// <summary>Stored cApprovalRequestItemFieldNew value.</summary>
   [Column(TypeName = "nvarchar(max)")]
   public string? cApprovalRequestItemFieldNew { get; set; }
   /// <summary>Stored cApprovalRequestItemFieldCurrent value.</summary>
   [Column(TypeName = "nvarchar(max)")]
   public string? cApprovalRequestItemFieldCurrent { get; set; }
   /// <summary>Stored cApprovalRequestItemFieldConflict value.</summary>
   [Column(TypeName = "bit")]
   public bool cApprovalRequestItemFieldConflict { get; set; }
   /// <summary>Stored cApprovalRequestItemFieldOverrideReason value.</summary>
   [Column(TypeName = "nvarchar(1000)")]
   public string? cApprovalRequestItemFieldOverrideReason { get; set; }
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
