using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>Persistent ApprovalRequestStep approval data.</summary>
[Table("ta_ApprovalRequestStep")]
public class ta_ApprovalRequestStep
{
   /// <summary>Stored cApprovalRequestStepId value.</summary>
   [Key]
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestStepId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestId value.</summary>
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestStepLevel value.</summary>
   [Column(TypeName = "int")]
   public int cApprovalRequestStepLevel { get; set; }
   /// <summary>Stored cApprovalRequestStepOrder value.</summary>
   [Column(TypeName = "int")]
   public int cApprovalRequestStepOrder { get; set; }
   /// <summary>Stored cApprovalRequestStepName value.</summary>
   [Column(TypeName = "nvarchar(100)")]
   public string cApprovalRequestStepName { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestStepKind value.</summary>
   [Column(TypeName = "int")]
   public ApprovalStepKind cApprovalRequestStepKind { get; set; }
   /// <summary>Stored cApprovalRequestStepStage value.</summary>
   [Column(TypeName = "int")]
   public ApprovalStepStatus cApprovalRequestStepStage { get; set; }
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
