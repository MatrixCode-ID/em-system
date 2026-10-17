using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>Persistent ApprovalSignerSetting approval data.</summary>
[Table("ta_ApprovalSignerSetting")]
public class ta_ApprovalSignerSetting
{
   /// <summary>Stored cApprovalSignerSettingId value.</summary>
   [Key]
   [Column(TypeName = "char(26)")]
   public string cApprovalSignerSettingId { get; set; } = string.Empty;
   /// <summary>Stored cApprovalName value.</summary>
   [Column(TypeName = "varchar(50)")]
   public string cApprovalName { get; set; } = string.Empty;
   /// <summary>Stored cApprovalSignerSettingStep value.</summary>
   [Column(TypeName = "nvarchar(100)")]
   public string cApprovalSignerSettingStep { get; set; } = string.Empty;
   /// <summary>Stored cApprovalSignerSettingDynamic value.</summary>
   [Column(TypeName = "bit")]
   public bool cApprovalSignerSettingDynamic { get; set; }
   /// <summary>Stored cUserId value.</summary>
   [Column(TypeName = "char(26)")]
   public string? cUserId { get; set; }
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
