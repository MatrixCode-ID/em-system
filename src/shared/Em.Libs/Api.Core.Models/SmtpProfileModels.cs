using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>Named SMTP management and sending, separate from the stable legacy contract.</summary>
public interface ISmtpProfileService : IServices
{
   /// <summary>Lists profiles without credentials and returns the global concurrency token.</summary>
   Task<SmtpProfileList> GetMeta_SmtpProfiles();
   /// <summary>Creates or updates one profile without changing the default selection.</summary>
   Task<SmtpProfileDetail> PostGetMeta_SmtpProfileSave(SmtpProfileSave request);
   /// <summary>Deletes a profile; deleting the default leaves no default.</summary>
   Task<SmtpProfileList> PostGetMeta_SmtpProfileDelete(string id, long expectedRevision);
   /// <summary>Selects the default, or clears it when id is null.</summary>
   Task<SmtpProfileList> PostGetMeta_SmtpDefault(string? id, long expectedRevision);
   /// <summary>Tests a saved profile by ID without sending email.</summary>
   Task<SmtpConnectionResult> PostGetMeta_SmtpProfileTestConnection(string id);
   /// <summary>Sends diagnostic email through the saved profile identified by ID.</summary>
   Task<SmtpSendResult> PostGetMeta_SmtpProfileTestEmail(string id, string recipient);
   /// <summary>Sends diagnostic email using an explicit sender through the saved profile identified by ID.</summary>
   Task<SmtpSendResult> PostGetMeta_SmtpProfileTestEmailFrom(string id, string sender, string recipient);
   /// <summary>Sends through the named profile, or the default when name is empty.</summary>
   Task<SmtpSendResult> PostGetMeta_SmtpSendByName(string? name, SmtpMessage message);
   /// <summary>Trusted server sending or authenticated UI sending by name.</summary>
   Task<SmtpSendResult> SendByNameAsync(string? name, SmtpMessage message);
}

/// <summary>Profile metadata and settings; never contains ciphertext or keys.</summary>
public sealed class SmtpProfileDetail
{
   /// <summary>Stable ULID used for editing and diagnostics.</summary>
   public string Id { get; set; } = "";
   /// <summary>Unique case-insensitive name used for application sending.</summary>
   public string Name { get; set; } = "";
   /// <summary>Optional manager note.</summary>
   public string? Note { get; set; }
   /// <summary>Whether unnamed calls select this profile.</summary>
   public bool IsDefault { get; set; }
   /// <summary>Global concurrency token, shared with legacy settings actions.</summary>
   public long Revision { get; set; }
   /// <summary>Saved settings.</summary>
   public SmtpSettings Settings { get; set; } = new();
   /// <summary>Whether a secret is stored.</summary>
   public bool HasPassword { get; set; }
}

/// <summary>One consistent snapshot of all configurations.</summary>
public sealed class SmtpProfileList
{
   /// <summary>Concurrency token for all subsequent mutations.</summary>
   public long Revision { get; set; }
   /// <summary>Profiles sorted by name.</summary>
   public SmtpProfileDetail[] Profiles { get; set; } = [];
}

/// <summary>Named profile update, keeping default selection as a separate operation.</summary>
public sealed class SmtpProfileSave
{
   /// <summary>Null for a new profile; otherwise the existing profile ID.</summary>
   public string? Id { get; set; }
   /// <summary>Unique profile name.</summary>
   public string Name { get; set; } = "";
   /// <summary>Optional note.</summary>
   public string? Note { get; set; }
   /// <summary>Settings to save.</summary>
   public SmtpSettings Settings { get; set; } = new();
   /// <summary>Last observed global concurrency token.</summary>
   public long ExpectedRevision { get; set; }
   /// <summary>Replacement secret; null preserves the stored secret.</summary>
   public string? Password { get; set; }
   /// <summary>Explicitly removes the saved secret.</summary>
   public bool ClearPassword { get; set; }
}
