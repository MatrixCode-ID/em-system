using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>Persistent ApprovalRequest approval data.</summary>
[Table("ta_ApprovalRequest")]
public class ta_ApprovalRequest
{
   /// <summary>Stored cApprovalRequestId value.</summary>
   [Key]
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestKind value.</summary>
   [Column(TypeName = "tinyint")]
   public ApprovalKind cApprovalRequestKind { get; set; }
   /// <summary>Stored cApprovalName value.</summary>
   [Column(TypeName = "varchar(50)")]
   public string cApprovalName { get; set; } = string.Empty;
   /// <summary>Stored cSubjectKey value.</summary>
   [Column(TypeName = "nvarchar(400)")]
   public string? cSubjectKey { get; set; }
   /// <summary>Stored cSummary value.</summary>
   [Column(TypeName = "nvarchar(400)")]
   public string cSummary { get; set; } = string.Empty;
   /// <summary>Optional second summary line; null shows the summary on one line.</summary>
   [Column(TypeName = "nvarchar(400)")]
   public string? cSubSummary { get; set; }
   /// <summary>Stored cApprovalRequestRequesterId value.</summary>
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestRequesterId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestStage value.</summary>
   [Column(TypeName = "int")]
   public ApprovalStage cApprovalRequestStage { get; set; }
   /// <summary>Stored cApprovalRequestLevel value.</summary>
   [Column(TypeName = "int")]
   public int cApprovalRequestLevel { get; set; }
   /// <summary>Stored cApprovalRequestCompletedDate value.</summary>
   [Column(TypeName = "datetime2")]
   public DateTime? cApprovalRequestCompletedDate { get; set; }
   /// <summary>Stored cReinstateOf value.</summary>
   [Column(TypeName = "char(26)")]
   public string? cReinstateOf { get; set; }
   /// <summary>Stored cApprovalRequestNote value.</summary>
   [Column(TypeName = "nvarchar(1000)")]
   public string? cApprovalRequestNote { get; set; }
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
