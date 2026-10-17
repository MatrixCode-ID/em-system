using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>Persistent ApprovalSignerSettingBackup approval data.</summary>
[Table("ta_ApprovalSignerSettingBackup")]
public class ta_ApprovalSignerSettingBackup
{
   /// <summary>Stored cApprovalSignerSettingBackupId value.</summary>
   [Key]
   [Column(TypeName = "char(26)")]
   public string cApprovalSignerSettingBackupId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalSignerSettingId value.</summary>
   [Column(TypeName = "char(26)")]
   public string cApprovalSignerSettingId { get; set; } = string.Empty;
   /// <summary>Stored cUserId value.</summary>
   [Column(TypeName = "char(26)")]
   public string cUserId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalSignerSettingBackupOrder value.</summary>
   [Column(TypeName = "int")]
   public int cApprovalSignerSettingBackupOrder { get; set; }
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
