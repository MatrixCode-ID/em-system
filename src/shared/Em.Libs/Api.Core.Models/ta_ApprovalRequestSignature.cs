using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>Persistent ApprovalRequestSignature approval data.</summary>
[Table("ta_ApprovalRequestSignature")]
public class ta_ApprovalRequestSignature
{
   /// <summary>Stored cApprovalRequestSignatureId value.</summary>
   [Key]
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestSignatureId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestId value.</summary>
   [Column(TypeName = "char(26)")]
   public string cApprovalRequestId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestStepId value.</summary>
   [Column(TypeName = "char(26)")]
   public string? cApprovalRequestStepId { get; set; }
   /// <summary>Stored cUserId value.</summary>
   [Column(TypeName = "char(26)")]
   public string cUserId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestSignatureApproved value.</summary>
   [Column(TypeName = "bit")]
   public bool cApprovalRequestSignatureApproved { get; set; }
   /// <summary>Stored cApprovalRequestSignatureNote value.</summary>
   [Column(TypeName = "nvarchar(1000)")]
   public string? cApprovalRequestSignatureNote { get; set; }
   /// <summary>Stored cApprovalRequestSignatureCode value.</summary>
   [Column(TypeName = "varchar(40)")]
   public string cApprovalRequestSignatureCode { get; set; } = string.Empty;
   /// <summary>Stored cApprovalRequestSignatureAdmin value.</summary>
   [Column(TypeName = "bit")]
   public bool cApprovalRequestSignatureAdmin { get; set; }
   /// <summary>Stored cApprovalRequestSignatureBackup value.</summary>
   [Column(TypeName = "bit")]
   public bool cApprovalRequestSignatureBackup { get; set; }
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
