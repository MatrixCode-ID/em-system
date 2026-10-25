using Em.Ui.Core.Release.Format;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace Em.Ui.Wpf.Core.Release;

/// <summary>Product identity shared by the published application and its launcher.</summary>
public sealed class ReleaseProduct {
   /// <summary>Display name from Product.</summary>
   public string AppName { get; set; } = "";
   /// <summary>Publisher from Company.</summary>
   public string Publisher { get; set; } = "";
   /// <summary>Technical product identity from EmAppId.</summary>
   public string AppId { get; set; } = "";
   /// <summary>Application executable from AssemblyName.</summary>
   public string AppExe { get; set; } = "";
   /// <summary>ICO bytes encoded as Base64.</summary>
   public string IconBase64 { get; set; } = "";
   /// <summary>Transparent PNG bytes encoded as Base64.</summary>
   public string LogoBase64 { get; set; } = "";
   /// <summary>Whether the host references Em WPF.</summary>
   public bool IsEmHost { get; set; }
   public string ApplicationId { get; set; } = "";
   public string ApplicationVersion { get; set; } = "1";
   public string Version { get; set; } = "";
   public string AndroidTargetFramework { get; set; } = "";
   public string MinimumSupportedJavaVersion { get; set; } = "";
   public string LatestSupportedJavaVersion { get; set; } = "";

   /// <summary>Checks product identity before publishing.</summary>
   public void Validate(ReleasePlatform platform = ReleasePlatform.Windows) {
      if (!Regex.IsMatch(AppId, @"^[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)+$", RegexOptions.CultureInvariant) || AppId == "MatrixCode.EmSystem")
         throw new InvalidDataException("Use a product app_id with at least two alphanumeric segments; MatrixCode.EmSystem is reserved.");
      if (string.IsNullOrWhiteSpace(AppName) || string.IsNullOrWhiteSpace(Publisher))
         throw new InvalidDataException("Product name and publisher are required.");
      if (platform == ReleasePlatform.Android) {
         if (!ReleaseManifestSerializer.IsSemanticVersion(Version) || !AndroidTargetFramework.EndsWith("-android", StringComparison.Ordinal))
            throw new InvalidDataException("Android host requires a SemVer Version and an Android target framework.");
         return;
      }
      if (ReleaseManifestSerializer.CheckPath(AppExe) is not null || AppExe.Contains('/') || !AppExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
         throw new InvalidDataException("Application executable must be a file name ending in .exe.");
      var ico = Convert.FromBase64String(IconBase64);
      if (ico.Length < 6 || ico[0] != 0 || ico[1] != 0 || ico[2] != 1 || ico[3] != 0)
         throw new InvalidDataException("ApplicationIcon must be an ICO file.");
      if (LogoBase64.Length > 0) ReadLogo(LogoBase64);
   }

   /// <summary>Decodes and freezes a PNG for preview.</summary>
   public static BitmapSource ReadLogo(string base64) {
      var bytes = Convert.FromBase64String(base64);
      if (!bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
         throw new InvalidDataException("EmAppLogo must be a PNG image.");
      using var stream = new MemoryStream(bytes);
      var image = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
      image.Freeze(); return image;
   }

   /// <summary>Reads evaluated MSBuild properties and references, including imported values.</summary>
   public static async Task<ReleaseProduct> ReadAsync(string project, CancellationToken token, ReleasePlatform platform = ReleasePlatform.Windows) {
      var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
         RedirectStandardOutput = true, RedirectStandardError = true };
      foreach (var arg in new[] { "msbuild", project, "-nologo", "-getProperty:Product,Company,EmAppId,AssemblyName,ApplicationIcon,EmAppLogo,ApplicationTitle,ApplicationId,ApplicationVersion,Version,TargetFramework,TargetFrameworks,MinimumSupportedJavaVersion,LatestSupportedJavaVersion", "-getItem:ProjectReference,PackageReference,MauiIcon" }) start.ArgumentList.Add(arg);
      using var process = Process.Start(start) ?? throw new IOException("Cannot start MSBuild.");
      var output = process.StandardOutput.ReadToEndAsync(token); var errors = process.StandardError.ReadToEndAsync(token);
      await process.WaitForExitAsync(token);
      if (process.ExitCode != 0) throw new IOException("MSBuild identity evaluation failed: " + await errors);
      using var doc = JsonDocument.Parse(await output);
      var props = doc.RootElement.GetProperty("Properties");
      string Get(string name) => props.GetProperty(name).GetString() ?? "";
      string Asset(string name) {
         var file = Get(name); if (file.Length == 0) return "";
         return Convert.ToBase64String(File.ReadAllBytes(Path.GetFullPath(file, Path.GetDirectoryName(Path.GetFullPath(project))!)));
      }
      var android = platform == ReleasePlatform.Android;
      var framework = (Get("TargetFramework") + ";" + Get("TargetFrameworks")).Split(';').FirstOrDefault(f => f.EndsWith("-android", StringComparison.Ordinal)) ?? "";
      var references = doc.RootElement.GetProperty("Items").ToString();
      var logo = android ? "" : Asset("EmAppLogo");
      if (android && doc.RootElement.GetProperty("Items").TryGetProperty("MauiIcon", out var icons)) {
         var icon = icons.EnumerateArray().FirstOrDefault();
         if (icon.ValueKind == JsonValueKind.Object && icon.TryGetProperty("Identity", out var identity)) {
            var file = Path.GetFullPath(identity.GetString()!, Path.GetDirectoryName(Path.GetFullPath(project))!);
            if (File.Exists(file) && Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase)) logo = Convert.ToBase64String(File.ReadAllBytes(file));
         }
      }
      return new ReleaseProduct { AppName = android && Get("ApplicationTitle").Length > 0 ? Get("ApplicationTitle") : Get("Product"), Publisher = Get("Company"), AppId = Get("EmAppId"),
         AppExe = Get("AssemblyName") + ".exe", IconBase64 = android ? "" : Asset("ApplicationIcon"), LogoBase64 = logo,
         ApplicationId = Get("ApplicationId"), ApplicationVersion = Get("ApplicationVersion"), Version = Get("Version"), AndroidTargetFramework = framework,
         MinimumSupportedJavaVersion = Get("MinimumSupportedJavaVersion"), LatestSupportedJavaVersion = Get("LatestSupportedJavaVersion"),
         IsEmHost = references.Contains(android ? "Em.Ui.Maui.Core" : "Em.Ui.Wpf.Core", StringComparison.OrdinalIgnoreCase) ||
            references.Contains(android ? "EmSys.Ui.Maui.Core" : "EmSys.Ui.Wpf.Core", StringComparison.OrdinalIgnoreCase) };
   }
}

/// <summary>Launcher settings and last prepared build, independent of the application version.</summary>
public sealed class ReleaseLauncher {
   /// <summary>Installer and installed runner filename.</summary>
   public string FileName { get; set; } = "inventory-installer.exe";
   /// <summary>Developer-selected major.minor.patch.</summary>
   public string BaseVersion { get; set; } = "1.0.0";
   /// <summary>prealpha, alpha, beta, rc, or release.</summary>
   public string Channel { get; set; } = "beta";
   /// <summary>automatic or ask.</summary>
   public string UpdateBehavior { get; set; } = "automatic";
   /// <summary>Version used by the prepared binary.</summary>
   public string Version { get; set; } = "";
   /// <summary>Hash of all launcher source, identity and settings inputs.</summary>
   public string InputHash { get; set; } = "";
}

/// <summary>PKCS#8 P-256 signing key stored directly in the product profile.</summary>
public static class ReleasePemKey {
   /// <summary>Reads and validates a private PEM key.</summary>
   public static ECDsa Open(string pem) {
      var key = ECDsa.Create();
      try {
         if (!pem.Contains("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal)) throw new CryptographicException("Use an unencrypted PKCS#8 private PEM key.");
         key.ImportFromPem(pem);
         if (!ReleaseSignature.IsP256(key)) throw new CryptographicException("The release key must use P-256.");
         key.ExportPkcs8PrivateKey(); return key;
      }
      catch { key.Dispose(); throw; }
   }
   /// <summary>Creates a new unencrypted private key.</summary>
   public static string Create() { using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); return key.ExportPkcs8PrivateKeyPem(); }
   /// <summary>Gets the public PEM without private material.</summary>
   public static string Public(string pem) { using var key = Open(pem); return key.ExportSubjectPublicKeyInfoPem(); }
}
