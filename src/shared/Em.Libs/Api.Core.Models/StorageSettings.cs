namespace Em.Api.Core.Models;

/// <summary>Storage settings of one feature (CDN, registry or NuPak).</summary>
public sealed record StorageFeatureSettings
{
   /// <summary>The feature is enabled.</summary>
   public bool Enabled { get; init; }

   /// <summary>Storage directory, absolute or relative to the API content root.</summary>
   public string Directory { get; init; } = "";

   /// <summary>Largest accepted upload, in megabytes.</summary>
   public int MaxUploadMb { get; init; } = 200;
}

/// <summary>General storage status of a feature. Deliberately contains no server filesystem paths.</summary>
/// <param name="Managed">Settings are managed from the UI (stored in the database) rather than fixed by the host.</param>
/// <param name="ActiveEnabled">The feature is enabled in the running API.</param>
/// <param name="SavedEnabled">The feature is enabled in the saved settings.</param>
/// <param name="RequiresRestart">Saved settings differ from the active ones until the API restarts.</param>
public sealed record StorageFeatureStatus(bool Managed, bool ActiveEnabled, bool SavedEnabled, bool RequiresRestart);

/// <summary>Active and saved storage settings of a feature, with resolved directories.</summary>
/// <param name="Revision">Revision of the saved settings, for optimistic concurrency.</param>
/// <param name="Active">Settings used by the running API.</param>
/// <param name="Saved">Settings that apply after the next restart.</param>
/// <param name="ActiveAbsoluteDirectory">Absolute directory of the active settings.</param>
/// <param name="SavedAbsoluteDirectory">Absolute directory of the saved settings.</param>
/// <param name="Managed">Settings are managed from the UI.</param>
/// <param name="RequiresRestart">Saved settings differ from the active ones until the API restarts.</param>
public sealed record StorageSettingsDetail(long Revision, StorageFeatureSettings Active,
   StorageFeatureSettings Saved, string ActiveAbsoluteDirectory, string SavedAbsoluteDirectory,
   bool Managed, bool RequiresRestart);

/// <summary>Request to save storage settings.</summary>
/// <param name="Revision">Revision the caller edited; a different stored revision is rejected.</param>
/// <param name="Settings">New settings.</param>
public sealed record StorageSettingsSave(long Revision, StorageFeatureSettings Settings);

/// <summary>Result of validating a storage directory.</summary>
/// <param name="Valid">The directory can be used.</param>
/// <param name="AbsoluteDirectory">Resolved absolute directory.</param>
/// <param name="Message">Explanation shown to the user.</param>
public sealed record StorageDirectoryValidation(bool Valid, string AbsoluteDirectory, string Message);
