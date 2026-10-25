namespace Em.Ui.Core.Release.Format;

/// <summary>Signed identity and minimum supported version of a single Android APK.</summary>
public sealed record ReleaseAndroidMetadata
{
   public required string FileName { get; init; }
   public required string ApplicationId { get; init; }
   public required int VersionCode { get; init; }
   public required string VersionName { get; init; }
   public int? MinVersionCode { get; init; }
}
