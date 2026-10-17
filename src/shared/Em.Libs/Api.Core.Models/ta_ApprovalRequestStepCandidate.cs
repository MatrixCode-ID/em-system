using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>Persistent ApprovalRequestStepCandidate approval data.</summary>
[Table("ta_ApprovalRequestStepCandidate")]
public class ta_ApprovalRequestStepCandidate
{
   /// <summary>Stored cApprovalRequestStepCandidateId value.</summary>
   [Key]
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestStepCandidateId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestStepId value.</summary>
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestStepId { get; set; } = string.Empty;
   /// <summary>Stored cUserId value.</summary>
   [Column(TypeName = "char(26)")]
   public string cUserId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestStepCandidateBackup value.</summary>
   [Column(TypeName = "bit")]
   public bool cApprovalRequestStepCandidateBackup { get; set; }
   /// <summary>Stored cApprovalRequestStepCandidateOrder value.</summary>
   [Column(TypeName = "int")]
   public int cApprovalRequestStepCandidateOrder { get; set; }
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
