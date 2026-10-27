using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Em.Ui.Wpf.Core.Release;

public sealed record AndroidCommand(string Executable, IReadOnlyList<string> Arguments, bool Elevated = false) {
   public override string ToString() => Executable + " " + string.Join(" ", Arguments.Select(a => "\"" + a + "\""));
}

/// <summary>Detects machine-local tools without installing or changing user environment variables.</summary>
public static class AndroidEnvironment {
   public static IReadOnlyList<string> SdkCandidates(string preferred) => Candidates(new[] {
      preferred, Environment.GetEnvironmentVariable("ANDROID_HOME") ?? "",
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Android", "android-sdk") }
      .Concat(RiderPaths("android"))
      .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk")));

   public static IReadOnlyList<string> JavaCandidates(string preferred) {
      var microsoft = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft");
      var jetbrains = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JetBrains");
      return Candidates(new[] { preferred, Environment.GetEnvironmentVariable("JAVA_HOME") ?? "" }
         .Concat(Directories(microsoft, "jdk-*"))
         .Concat(Directories(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Android", "openjdk"), "jdk*"))
         .Concat(Directories(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Android", "openjdk"), "jdk*"))
         .Concat(RiderPaths("jdk"))
         .Concat(Directories(jetbrains, "*", true).Where(d => File.Exists(Path.Combine(d, "bin", "keytool.exe")))));
   }

   private static IEnumerable<string> RiderPaths(string kind) {
      var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JetBrains");
      var paths = new List<string>();
      foreach (var rider in Directories(root, "Rider*")) {
         var options = Path.Combine(rider, "options");
         foreach (var name in new[] { "android.xml", "jdk.table.xml" }) {
            var file = Path.Combine(options, name);
            if (!File.Exists(file)) continue;
            try {
               var xml = System.Xml.Linq.XDocument.Load(file);
               foreach (var attribute in xml.Descendants().Attributes("value")) {
                  var value = attribute.Value.Replace("$USER_HOME$", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                  if (kind == "jdk" ? File.Exists(Path.Combine(value, "bin", "keytool.exe")) : Directory.Exists(Path.Combine(value, "platform-tools"))) paths.Add(value);
               }
            } catch (Exception ex) when (ex is IOException or System.Xml.XmlException or ArgumentException) { }
         }
      }
      return paths;
   }

   public static IReadOnlyList<string> Candidates(IEnumerable<string> paths) => paths.Where(p => !string.IsNullOrWhiteSpace(p))
      .Select(Path.GetFullPath).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

   private static IEnumerable<string> Directories(string root, string pattern, bool recurse = false) {
      try { return Directory.Exists(root) ? Directory.GetDirectories(root, pattern, new EnumerationOptions { RecurseSubdirectories = recurse, IgnoreInaccessible = true }) : []; }
      catch (IOException) { return []; }
   }

   public static string BuildTool(string sdk, string tool) => Directories(Path.Combine(sdk, "build-tools"), "*")
      .OrderByDescending(p => Version.TryParse(Path.GetFileName(p), out var v) ? v : new Version())
      .Select(p => Path.Combine(p, tool)).FirstOrDefault(File.Exists) ?? "";

   public static async Task<IReadOnlyList<string>> CheckAsync(string sdk, string java, ReleaseProduct product, CancellationToken token) {
      var result = new List<string>();
      result.Add(Directory.Exists(Path.Combine(sdk, "platform-tools")) ? "OK: platform-tools" : "Missing: SDK platform-tools");
      result.Add(BuildTool(sdk, "apksigner.bat").Length > 0 && BuildTool(sdk, "aapt2.exe").Length > 0 ? "OK: build-tools" : "Missing: SDK build-tools (apksigner/aapt2)");
      var api = Regex.Match(product.AndroidTargetFramework, @"android(\d+)").Groups[1].Value;
      if (api.Length == 0) api = "36";
      result.Add(File.Exists(Path.Combine(sdk, "platforms", "android-" + api, "android.jar")) ? "OK: Android API " + api : "Missing: Android API " + api);
      if (!File.Exists(Path.Combine(java, "bin", "java.exe")) || !File.Exists(Path.Combine(java, "bin", "keytool.exe"))) result.Add("Missing: JDK java/keytool");
      else {
         var output = await AndroidProcess.CaptureAsync(Path.Combine(java, "bin", "java.exe"), ["-version"], null, token);
         var major = JavaMajorVersion(output);
         var minimum = int.TryParse(product.MinimumSupportedJavaVersion.Split('.')[0], out var min) ? min : 17;
         var maximum = int.TryParse(product.LatestSupportedJavaVersion.Split('.')[0], out var max) ? max : int.MaxValue;
         result.Add(major >= minimum && major <= maximum ? "OK: JDK " + major : $"Missing: supported JDK ({minimum}..{maximum}; detected {major})");
      }
      var workloads = await AndroidProcess.CaptureAsync("dotnet", ["workload", "list"], null, token);
      result.Add(HasAndroidWorkload(workloads) ? "OK: Android workload" : "Missing: maui-android workload");
      if (result.Any(r => r.StartsWith("Missing")) && sdk.Contains("Program Files", StringComparison.OrdinalIgnoreCase))
         result.Add("Use the SDK under LocalAppData/Android/Sdk to avoid administrator permissions.");
      return result;
   }

   // MAUI controls are supplied by the project's NuGet references; the Android workload supplies the SDK packs.
   public static bool HasAndroidWorkload(string output) => Regex.IsMatch(output, @"(?m)^\s*(?:android|maui(?:-android)?)\s+\d");

   public static int JavaMajorVersion(string output) => int.TryParse(Regex.Match(output, @"\bversion\s+""(\d+)").Groups[1].Value, out var major) ? major : 0;

   /// <summary>Runs an elevated installer with captured output, without changing the caller's environment.</summary>
   public static async Task RunElevatedAsync(AndroidCommand command, IProgress<string> log, CancellationToken token) {
      var temporary = Path.Combine(Path.GetTempPath(), "EmAndroidEnvironment-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(temporary);
      try {
         var output = Path.Combine(temporary, "install.log");
         var script = Path.Combine(temporary, "install.ps1");
         static string Literal(string value) => "'" + value.Replace("'", "''") + "'";
         await File.WriteAllTextAsync(script, "$ErrorActionPreference='Stop'\ntry { & " + Literal(command.Executable) + " " +
            string.Join(" ", command.Arguments.Select(Literal)) + " *> " + Literal(output) + "; exit $LASTEXITCODE } catch { $_ | Out-File -Append " + Literal(output) + "; exit 1 }", token);
         var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
         foreach (var arg in new[] { "-NoProfile", "-File", script }) start.ArgumentList.Add(arg);
         using var process = Process.Start(start) ?? throw new IOException("Cannot start elevated environment preparation.");
         var linesRead = 0;
         void ReadOutput() {
            if (!File.Exists(output)) return;
            try { var lines = File.ReadAllLines(output); foreach (var line in lines.Skip(linesRead)) log.Report(line); linesRead = lines.Length; }
            catch (IOException) { }
         }
         while (!process.HasExited) { ReadOutput(); await Task.Delay(250, CancellationToken.None); }
         ReadOutput();
         if (process.ExitCode != 0) throw new IOException("Environment preparation failed with exit code " + process.ExitCode);
         token.ThrowIfCancellationRequested();
      }
      finally { Directory.Delete(temporary, true); }
   }

   public static IReadOnlyList<AndroidCommand> PreparationCommands(IReadOnlyList<string> checks, string project, string tfm, string sdk, string java, string latestJava = "") {
      var commands = new List<AndroidCommand>();
      var dotnet = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(path => Path.Combine(path, "dotnet.exe")).FirstOrDefault(File.Exists);
      var elevated = dotnet?.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == true;
      if (checks.Any(r => r.Contains("Missing: maui"))) commands.Add(new("dotnet", ["workload", "install", "maui-android"], elevated));
      var version = int.TryParse(latestJava.Split('.')[0], out var v) ? v : 21;
      if (checks.Any(r => r.Contains("Missing: JDK") || r.Contains("Missing: supported JDK")))
         commands.Add(new("winget", ["install", "--id", "Microsoft.OpenJDK." + version, "--exact", "--accept-source-agreements"]));
      if (checks.Any(r => r.StartsWith("Missing: SDK") || r.StartsWith("Missing: Android"))) {
         if (!Directory.Exists(sdk) && sdk.Any(c => c > 127 || char.IsWhiteSpace(c))) throw new InvalidDataException("A new Android SDK path must contain only ASCII characters without spaces.");
         commands.Add(new("dotnet", ["build", project, "-t:InstallAndroidDependencies", "-f", tfm, "-p:AndroidSdkDirectory=" + sdk,
            "-p:JavaSdkDirectory=" + java, "-p:AcceptAndroidSDKLicenses=True"]));
      }
      return commands;
   }
}

internal static class AndroidProcess {
   public static async Task<string> CaptureAsync(string executable, IEnumerable<string> arguments, IReadOnlyDictionary<string, string>? environment,
      CancellationToken token, IProgress<string>? log = null) {
      var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
      foreach (var arg in arguments) start.ArgumentList.Add(arg);
      if (environment is not null) foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
      start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
      using var process = Process.Start(start) ?? throw new IOException("Cannot start " + executable);
      using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
      var output = new System.Text.StringBuilder();
      string Redact(string line) { if (environment is not null) foreach (var secret in environment.Values.Where(v => v.Length > 0)) line = line.Replace(secret, "[redacted]", StringComparison.Ordinal); return line; }
      async Task Drain(StreamReader reader) { while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line) { line = Redact(line); lock (output) output.AppendLine(line); log?.Report(line); } }
      await Task.WhenAll(Drain(process.StandardOutput), Drain(process.StandardError), process.WaitForExitAsync(token)).ConfigureAwait(false);
      if (process.ExitCode != 0) throw new IOException($"{Path.GetFileName(executable)} exited with {process.ExitCode}: " + output);
      return output.ToString();
   }
}
