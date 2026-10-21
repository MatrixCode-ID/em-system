using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Em.Ui.Wpf.Core.Release;

/// <summary>Content-based, incremental product launcher builds.</summary>
public static class LauncherBuild {
   /// <summary>Reads a remote manifest's launcher metadata, if any.</summary>
   public static ReleaseLauncherMetadata? Remote(byte[]? bytes) => bytes is null ? null : ReleaseManifestSerializer.Deserialize(bytes).Launcher;

   /// <summary>Chooses a launcher version from the target's recorded version and input hash.</summary>
   public static string SelectVersion(ReleaseLauncher settings, string hash, ReleaseLauncherMetadata? remote) {
      if (!Regex.IsMatch(settings.BaseVersion, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")) throw new InvalidDataException("Launcher version must be MAJOR.MINOR.PATCH.");
      if (settings.Channel is not ("prealpha" or "alpha" or "beta" or "rc" or "release")) throw new InvalidDataException("Invalid launcher version channel.");
      if (remote?.InputHash == hash) return remote.Version;
      if (settings.Channel == "release") {
         if (remote?.Version == settings.BaseVersion) throw new InvalidDataException("This launcher release version has already been used. Increase MAJOR.MINOR.PATCH.");
         return settings.BaseVersion;
      }
      var prefix = settings.BaseVersion + "-" + settings.Channel + ".";
      var next = remote is not null && remote.Version.StartsWith(prefix, StringComparison.Ordinal) &&
         int.TryParse(remote.Version[prefix.Length..], out var n) ? checked(n + 1) : 1;
      return prefix + next;
   }

   /// <summary>Hashes source bytes and product inputs; timestamps and git commits are excluded.</summary>
   public static string InputHash(string source, ReleaseProfile profile, string defaultSource) {
      using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
      void Add(string name, byte[] bytes) {
         var key = Encoding.UTF8.GetBytes(name); hash.AppendData(BitConverter.GetBytes(key.Length)); hash.AppendData(key);
         hash.AppendData(BitConverter.GetBytes(bytes.LongLength)); hash.AppendData(bytes);
      }
      var paths = Directory.EnumerateFiles(Path.Combine(source, "src"), "*", SearchOption.AllDirectories)
         .Concat(new[] { "build.rs", "Cargo.toml", "Cargo.lock", "launcher.manifest", "build-dist.ps1", "rust-toolchain.toml", ".cargo/config.toml" }.Select(p => Path.Combine(source, p)))
         .OrderBy(p => Path.GetRelativePath(source, p).Replace('\\', '/'), StringComparer.Ordinal);
      foreach (var path in paths) Add(Path.GetRelativePath(source, path).Replace('\\', '/'), File.ReadAllBytes(path));
      Add("product", JsonSerializer.SerializeToUtf8Bytes(new { profile.Product, profile.Launcher.FileName, profile.Launcher.BaseVersion,
         profile.Launcher.Channel, profile.Launcher.UpdateBehavior, defaultSource, PublicKey = ReleasePemKey.Public(profile.PrivateKeyPem) }));
      return Convert.ToHexStringLower(hash.GetHashAndReset());
   }

   /// <summary>Gets the default product-specific output folder.</summary>
   public static string DefaultOutput(string appId) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Em", "ReleaseManager", "Launchers", appId);

   /// <summary>Explains why Prepare cannot obtain a launcher without attempting a build.</summary>
   public static string? BlockedReason(string source, string output, ReleaseProfile profile) {
      if (profile.PrivateKeyPem.Length == 0) return "Create or import the product signing PEM in Settings.";
      try { profile.Product.Validate(); ValidateSettings(profile.Launcher); }
      catch (Exception ex) { return ex.Message; }
      var exe = Path.Combine(output, profile.Launcher.FileName);
      if (!File.Exists(Path.Combine(source, "Cargo.toml")))
         return CacheMatches(exe, profile.Launcher.InputHash) ? null : "Choose the local launcher source folder in Settings; this product needs a launcher build.";
      return null;
   }

   private static bool CacheMatches(string exe, string hash) {
      try {
         var stamp = JsonSerializer.Deserialize<BuildStamp>(File.ReadAllText(Path.ChangeExtension(exe, ".build.json")));
         return File.Exists(exe) && stamp?.InputHash == hash && stamp.ExeHash == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(exe)));
      }
      catch (Exception ex) when (ex is IOException or JsonException) { return false; }
   }

   /// <summary>Validates product launcher filename and update behavior.</summary>
   public static void ValidateSettings(ReleaseLauncher settings) {
      if (ReleaseManifestSerializer.CheckPath(settings.FileName) is not null || settings.FileName.Contains('/') ||
          settings.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !settings.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
         throw new InvalidDataException("Launcher name must be a single .exe filename.");
      if (settings.UpdateBehavior is not ("automatic" or "ask")) throw new InvalidDataException("Update behavior must be automatic or ask.");
   }

   /// <summary>Builds only if necessary, writes a marker and copies the launcher into the publish output.</summary>
   public static async Task PrepareAsync(string source, string output, ReleaseProfile profile, string defaultSource,
      ReleaseLauncherMetadata? remote, IProgress<string> log, CancellationToken token) {
      profile.Product.Validate(); ValidateSettings(profile.Launcher);
      Directory.CreateDirectory(output);
      var exe = Path.Combine(output, profile.Launcher.FileName);
      var hasSource = File.Exists(Path.Combine(source, "Cargo.toml"));
      var hash = hasSource ? InputHash(source, profile, defaultSource) : profile.Launcher.InputHash;
      if (hash.Length == 0) throw new InvalidDataException("A local launcher source folder is required for the first build.");
      var version = SelectVersion(profile.Launcher, hash, remote);
      if (!CacheMatches(exe, hash) || profile.Launcher.Version != version) {
         if (!hasSource) throw new InvalidDataException("The launcher inputs changed; select its local source folder and install Rust/MSVC.");
         var buildFolder = Path.Combine(output, ".build"); Directory.CreateDirectory(buildFolder);
         var icon = Path.Combine(buildFolder, "product.ico"); File.WriteAllBytes(icon, Convert.FromBase64String(profile.Product.IconBase64));
         var logo = Path.Combine(buildFolder, "product.png");
         if (profile.Product.LogoBase64.Length > 0) File.WriteAllBytes(logo, Convert.FromBase64String(profile.Product.LogoBase64));
         var engine = await EngineVersion(source, token);
         string Toml(string text) => JsonSerializer.Serialize(text);
         var product = new Dictionary<string, string> { ["app_name"] = profile.Product.AppName, ["publisher"] = profile.Product.Publisher,
            ["app_exe"] = profile.Product.AppExe, ["app_id"] = profile.Product.AppId, ["icon"] = icon.Replace('\\', '/'),
            ["logo"] = profile.Product.LogoBase64.Length > 0 ? logo.Replace('\\', '/') : "", ["public_key"] = ReleasePemKey.Public(profile.PrivateKeyPem),
            ["launcher_exe"] = profile.Launcher.FileName, ["launcher_version"] = version, ["engine_version"] = engine,
            ["default_source"] = defaultSource, ["update_behavior"] = profile.Launcher.UpdateBehavior };
         var productFile = Path.Combine(buildFolder, "product.toml");
         await File.WriteAllLinesAsync(productFile, product.Select(kv => kv.Key + " = " + Toml(kv.Value)), token);
         var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
         foreach (var arg in new[] { "-NoProfile", "-File", Path.Combine(source, "build-dist.ps1"), "-ProductFile", productFile,
            "-OutputDirectory", output, "-ExeName", profile.Launcher.FileName, "-TargetDirectory", Path.Combine(output, ".target", profile.Product.AppId) }) start.ArgumentList.Add(arg);
         using var process = Process.Start(start) ?? throw new IOException("Cannot start the launcher build. Install PowerShell 7, Rust and Visual Studio x64 tools.");
         var stdout = Pump(process.StandardOutput); var stderr = Pump(process.StandardError);
         try { await process.WaitForExitAsync(token); await Task.WhenAll(stdout, stderr); }
         catch (OperationCanceledException) { process.Kill(true); throw; }
         if (process.ExitCode != 0) throw new IOException("Launcher build failed. Install Rust/MSVC or fix the source. See the activity log.");
         var stamp = new BuildStamp(version, hash, engine, DateTimeOffset.UtcNow, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(exe))));
         await File.WriteAllTextAsync(Path.ChangeExtension(exe, ".build.json"), JsonSerializer.Serialize(stamp), token);
         async Task Pump(StreamReader reader) { while (await reader.ReadLineAsync(token) is { } line) log.Report(line); }
      }
      profile.Launcher.InputHash = hash; profile.Launcher.Version = version;
      File.Copy(exe, Path.Combine(profile.PublishFolder, profile.Launcher.FileName), true);
   }

   private static async Task<string> EngineVersion(string source, CancellationToken token) {
      try {
         var start = new ProcessStartInfo("git") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
         foreach (var arg in new[] { "describe", "--tags", "--always", "--dirty" }) start.ArgumentList.Add(arg);
         using var p = Process.Start(start)!; var result = p.StandardOutput.ReadToEndAsync(token); await p.WaitForExitAsync(token);
         return p.ExitCode == 0 ? (await result).Trim() : "unknown";
      }
      catch (System.ComponentModel.Win32Exception) { return "unknown"; }
   }
   private sealed record BuildStamp(string Version, string InputHash, string EngineVersion, DateTimeOffset BuiltAtUtc, string ExeHash);
}
