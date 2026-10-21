namespace Em.Ui.Wpf.Core.Release;

/// <summary>Signed metadata of the product launcher included in a release.</summary>
public sealed class ReleaseLauncherMetadata {
   /// <summary>Launcher executable in the binaries root.</summary>
   public string FileName { get; set; } = "";
   /// <summary>Independent semantic launcher version.</summary>
   public string Version { get; set; } = "";
   /// <summary>SHA-256 of the launcher build inputs.</summary>
   public string InputHash { get; set; } = "";
}
