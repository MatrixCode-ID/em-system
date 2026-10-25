using Em.Ui.Core.Release.Format;
using System.IO;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Em.Ui.Wpf.Core.Release;

/// <summary>Prepares a signed arm64 APK using an already installed Android environment.</summary>
public static class AndroidReleaseBuilder {
   public static int NextVersionCode(ReleaseManifest? previous, string initialVersion, string applicationId) {
      if (previous is null) return int.TryParse(initialVersion, out var initial) && initial is >= 1 and <= 2100000000 ? initial : 1;
      if (previous.Android is not { } android) throw new InvalidDataException("The destination contains a Windows release. Choose a separate Android release folder.");
      if (android.ApplicationId != applicationId) throw new InvalidDataException("The destination applicationId is permanent and differs from this profile.");
      if (android.VersionCode >= 2100000000) throw new InvalidDataException("Android versionCode is exhausted.");
      return android.VersionCode + 1;
   }

   public static IReadOnlyList<string> PublishArguments(string project, string output, string framework, string sdk, string java, string targets) => [
      "publish", project, "-c", "Release", "-f", framework, "-r", "android-arm64",
      "-p:AndroidSdkDirectory=" + sdk, "-p:JavaSdkDirectory=" + java,
      "-p:CustomBeforeMicrosoftCommonTargets=" + targets, "-o", output, .. ReleaseBuilder.DeterminismArguments ];

   public static string WriteIdentityTargets(ReleaseProfile profile, string project, string folder, string keystore, int code, string address) {
      XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
      static string Escape(string value) => value.Replace("%", "%25").Replace("$", "%24").Replace("@", "%40").Replace(";", "%3B").Replace("'", "%27");
      var condition = new XAttribute("Condition", "'$(MSBuildProjectFullPath)' == '" + Escape(Path.GetFullPath(project)) + "'");
      var values = new Dictionary<string, string> {
         ["EmAppId"] = profile.Product.AppId, ["ApplicationId"] = profile.Android.ApplicationId,
         ["ApplicationVersion"] = code.ToString(System.Globalization.CultureInfo.InvariantCulture),
         ["ApplicationDisplayVersion"] = profile.Product.Version.Split('+')[0],
         ["AndroidPackageFormat"] = "apk", ["AndroidPackageFormats"] = "apk", ["RuntimeIdentifiers"] = "android-arm64", ["AndroidKeyStore"] = "true",
         ["AndroidSigningKeyStore"] = keystore, ["AndroidSigningKeyAlias"] = profile.Android.KeystoreAlias,
         ["AndroidSigningStorePass"] = "env:EM_ANDROID_STORE_PASS", ["AndroidSigningKeyPass"] = "env:EM_ANDROID_KEY_PASS"
      };
      using var key = ReleasePemKey.Open(profile.PrivateKeyPem);
      XElement Attribute(string name, string value) => new(ns + "AssemblyAttribute", new XAttribute("Include", "System.Reflection.AssemblyMetadataAttribute"),
         new XElement(ns + "_Parameter1", name), new XElement(ns + "_Parameter2", Escape(value)));
      var root = new XElement(ns + "Project",
         new XElement(ns + "PropertyGroup", condition, values.Select(v => new XElement(ns + v.Key, Escape(v.Value)))),
         new XElement(ns + "ItemGroup", new XAttribute(condition), Attribute("EmReleaseAddress", address), Attribute("EmReleasePublicKey", Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()))));
      var file = Path.Combine(folder, "Android.targets");
      new XDocument(root).Save(file);
      return file;
   }

   public static async Task PrepareAsync(ReleaseProfile profile, ReleaseTarget target, string address, string sdk, string java, IProgress<string> log, CancellationToken token) {
      var hostProject = Path.GetFullPath(profile.HostProject, Path.GetDirectoryName(Path.GetFullPath(profile.SolutionPath))!);
      var identity = await ReleaseProduct.ReadAsync(hostProject, token, ReleasePlatform.Android);
      profile.Product.Version = profile.ReleaseNoteLock && profile.ReleaseNoteVersion.Length > 0 ? profile.ReleaseNoteVersion : identity.Version;
      profile.Product.ApplicationVersion = identity.ApplicationVersion;
      profile.Product.AndroidTargetFramework = identity.AndroidTargetFramework;
      profile.Product.MinimumSupportedJavaVersion = identity.MinimumSupportedJavaVersion;
      profile.Product.LatestSupportedJavaVersion = identity.LatestSupportedJavaVersion;
      await Em.Ui.Wpf.Core.ReleaseNotes.ReleaseNoteGuard.RequireAsync(profile, target, token);
      profile.ValidateAndroidRelease();
      if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !address.EndsWith('/'))
         throw new InvalidDataException("Set the public CDN address before preparing an Android release.");
      var checks = await AndroidEnvironment.CheckAsync(sdk, java, profile.Product, token);
      foreach (var check in checks) log.Report(check);
      if (checks.Any(r => r.StartsWith("Missing:"))) throw new InvalidDataException("Prepare the missing Android environment components in Settings first.");
      var bytes = await target.ReadFileAsync(ReleaseLayout.ManifestFileName, token);
      var previous = bytes is null ? null : ReleaseManifestSerializer.Deserialize(bytes);
      if (previous is not null) {
         var signature = await target.ReadFileAsync(ReleaseLayout.SignatureFileName, token) ?? throw new InvalidDataException("The destination signature is missing.");
         using var key = ReleasePemKey.Open(profile.PrivateKeyPem);
         if (!ReleaseSignature.Verify(bytes!, ReleaseManifestSerializer.DeserializeSignature(signature), [key.ExportSubjectPublicKeyInfo()]))
            throw new InvalidDataException("The destination manifest signature does not match this profile.");
      }
      var code = NextVersionCode(previous, profile.Product.ApplicationVersion, profile.Android.ApplicationId);
      if (profile.Android.MinVersionCode > code) throw new InvalidDataException("Minimum version code cannot exceed the prepared version code.");
      if (ReleaseBuilder.CheckPublishFolder(profile.PublishFolder, profile.SolutionPath) is { } reason) throw new InvalidDataException("Unsafe publish folder: " + reason);
      var temporary = Path.Combine(Path.GetTempPath(), "EmAndroidRelease-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(temporary);
      try {
         var keystore = Path.Combine(temporary, "release.keystore");
         await File.WriteAllBytesAsync(keystore, Convert.FromBase64String(profile.Android.KeystoreBase64), token);
         var project = Path.GetFullPath(profile.HostProject, Path.GetDirectoryName(Path.GetFullPath(profile.SolutionPath))!);
         var targets = WriteIdentityTargets(profile, project, temporary, keystore, code, address);
         var output = Path.Combine(temporary, "publish");
         await AndroidProcess.CaptureAsync("dotnet", PublishArguments(project, output, profile.Product.AndroidTargetFramework, sdk, java, targets), AndroidKeystore.EnvironmentFor(profile.Android), token, log);
         var apks = Directory.GetFiles(output, profile.Android.ApplicationId + "-Signed.apk", SearchOption.TopDirectoryOnly);
         if (apks.Length != 1) throw new InvalidDataException("Publish must produce exactly one applicationId-Signed.apk.");
         await ReleaseBuilder.ClearFolderAsync(profile.PublishFolder, token);
         Directory.CreateDirectory(profile.PublishFolder);
         var destination = Path.Combine(profile.PublishFolder, profile.AndroidApkFileName);
         File.Copy(apks[0], destination);
         profile.Android.PreparedVersionCode = code;
         profile.Android.PreparedVersionName = profile.Product.Version;
         profile.Android.PreparedApkSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(destination, token)));
         await ReleaseArtifact.WriteBuildRecordAsync(profile, project, token);
         log.Report("Build provenance saved beside the publish folder (.build.json).");
      }
      finally { Directory.Delete(temporary, true); }
   }

   public static ReleaseAndroidMetadata Metadata(ReleaseProfile profile, IReadOnlyList<ReleaseFile> files) {
      profile.ValidateAndroidRelease();
      if (files.Count != 1 || files[0].Path != profile.AndroidApkFileName || files[0].Sha256 != profile.Android.PreparedApkSha256 || profile.Android.PreparedVersionCode < 1)
         throw new InvalidDataException("The publish folder no longer matches the prepared APK. Prepare again.");
      return new() { FileName = profile.AndroidApkFileName, ApplicationId = profile.Android.ApplicationId,
         VersionCode = profile.Android.PreparedVersionCode, VersionName = profile.Android.PreparedVersionName,
         MinVersionCode = profile.Android.MinVersionCode == 0 ? null : profile.Android.MinVersionCode };
   }
}
