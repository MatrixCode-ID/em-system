using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Em.Api.Core.Models;

/// <summary>Database row for a named SMTP configuration. Credentials are never serialized.</summary>
[Table("ta_Smtp")]
public class ta_Smtp
{
   /// <summary>Stable profile ULID.</summary>
   [Key, MaxLength(26)] public string cSmtpId { get; set; } = "";
   /// <summary>Unique profile name.</summary>
   [MaxLength(255)] public string cSmtpName { get; set; } = "";
   /// <summary>Zero is disabled; one is enabled.</summary>
   public int cSmtpState { get; set; }
   /// <summary>Per-profile revision, distinct from the global compatibility token.</summary>
   public int cSmtpRevision { get; set; } = 1;
   /// <summary>Optional manager note.</summary>
   [MaxLength(500)] public string? cSmtpNote { get; set; }
   /// <summary>Whether unnamed sending selects this profile.</summary>
   public bool cSmtpDefault { get; set; }
   /// <summary>SMTP hostname.</summary>
   [MaxLength(255)] public string cSmtpHost { get; set; } = "";
   /// <summary>SMTP port.</summary>
   public int cSmtpPort { get; set; } = 587;
   /// <summary>Transport security mode.</summary>
   public int cSmtpSecurity { get; set; } = 1;
   /// <summary>Whether credentials are required.</summary>
   public bool cSmtpAuthenticate { get; set; } = true;
   /// <summary>Authentication username.</summary>
   [MaxLength(255)] public string cSmtpUsername { get; set; } = "";
   /// <summary>AES-GCM envelope, omitted from JSON.</summary>
   [JsonIgnore] public byte[]? cSmtpPassword { get; set; }
   /// <summary>Sender address.</summary>
   [MaxLength(320)] public string cSmtpFromAddress { get; set; } = "";
   /// <summary>Sender display name.</summary>
   [MaxLength(255)] public string cSmtpFromName { get; set; } = "";
   /// <summary>Total operation timeout.</summary>
   public int cSmtpTimeoutSeconds { get; set; } = 30;
   /// <summary>Last update time in UTC.</summary>
   public DateTime ustamp { get; set; }
   /// <summary>Creation time in UTC.</summary>
   public DateTime datestamp { get; set; }
   /// <summary>Optional extension data.</summary>
   public string? json_object { get; set; }
}
