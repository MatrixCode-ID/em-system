namespace Em.Api.Core.Models;

public sealed record StorageFeatureSettings
{
   public bool Enabled { get; init; }
   public string Directory { get; init; } = "";
   public int MaxUploadMb { get; init; } = 200;
}

// General status deliberately contains no server filesystem paths.
public sealed record StorageFeatureStatus(bool Managed, bool ActiveEnabled, bool SavedEnabled, bool RequiresRestart);
public sealed record StorageSettingsDetail(long Revision, StorageFeatureSettings Active,
   StorageFeatureSettings Saved, string ActiveAbsoluteDirectory, string SavedAbsoluteDirectory,
   bool Managed, bool RequiresRestart);
public sealed record StorageSettingsSave(long Revision, StorageFeatureSettings Settings);
public sealed record StorageDirectoryValidation(bool Valid, string AbsoluteDirectory, string Message);
