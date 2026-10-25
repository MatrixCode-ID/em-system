using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// The Prepare stage of Release Manager: checks the .NET SDK, reads the list of host projects from the
   /// <c>.slnx</c>, safely empties the local publish folder, then runs a self-contained <c>win-x64</c>
   /// <c>dotnet publish</c> with arguments that make the result deterministic.
   /// </summary>
   public static class ReleaseBuilder
   {
      /// <summary>The runtime identifier of the publish result.</summary>
      public const string RuntimeIdentifier = "win-x64";

      /// <summary>
      /// Extra arguments that make the same build produce the same bytes: no commit hash in the
      /// informational version, normalized source paths, deterministic compilation, and no single-file.
      /// Only used by Prepare; daily builds are unchanged.
      /// </summary>
      public static readonly IReadOnlyList<string> DeterminismArguments = [
         "-p:IncludeSourceRevisionInInformationalVersion=false",
         "-p:ContinuousIntegrationBuild=true",
         "-p:Deterministic=true",
         "-p:PublishSingleFile=false"
      ];

      /// <summary>
      /// The first installed .NET SDK 10.x version according to <c>dotnet --list-sdks</c>, or <c>null</c> when
      /// there is none (or <c>dotnet</c> itself is not found).
      /// </summary>
      public static async Task<string?> FindSdkAsync(CancellationToken token) {
         try {
            using var process = Process.Start(new ProcessStartInfo("dotnet", "--list-sdks") {
               RedirectStandardOutput = true,
               RedirectStandardError = true,
               UseShellExecute = false,
               CreateNoWindow = true
            });
            if (process is null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            return output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
               .Where(r => r.StartsWith("10.", StringComparison.Ordinal))
               .Select(r => r.Split(' ')[0])
               .FirstOrDefault();
         }
         catch (Exception x) when (x is System.ComponentModel.Win32Exception or InvalidOperationException) {
            return null;
         }
      }

      /// <summary>
      /// The projects in the <c>.slnx</c> that produce an application (<c>OutputType</c> <c>WinExe</c> or
      /// <c>Exe</c>), with their paths as written in the <c>.slnx</c> (relative to its folder).
      /// </summary>
      public static IReadOnlyList<string> ReadHostProjects(string solutionPath) =>
         ReadHostProjects(solutionPath, null, CancellationToken.None);

      /// <summary>
      /// The projects in the <c>.slnx</c> that produce an application, reporting each project as it is
      /// checked. A project without a declared <c>OutputType</c> is evaluated with MSBuild, which takes a
      /// few seconds each, so call this off the UI thread.
      /// </summary>
      public static IReadOnlyList<string> ReadHostProjects(string solutionPath, IProgress<string>? progress, CancellationToken token, ReleasePlatform? platform = null) {
         var folder = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
         var projects = XDocument.Load(solutionPath)
            .Descendants()
            .Where(r => r.Name.LocalName == "Project")
            .Select(r => (string?)r.Attribute("Path"))
            .OfType<string>()
            .ToArray();
         var hosts = new List<string>();
         for (var i = 0; i < projects.Length; i++) {
            token.ThrowIfCancellationRequested();
            progress?.Report($"Checking project {i + 1} of {projects.Length}: {Path.GetFileName(projects[i])}");
            var projectPath = Path.Combine(folder, projects[i]);
            if (IsApplication(projectPath) && (platform is null || ReadHostPlatforms(projectPath).Contains(platform.Value))) hosts.Add(projects[i]);
         }
         return hosts;

         static bool IsApplication(string projectPath) {
            if (!File.Exists(projectPath)) return false;
            try {
               var declared = XDocument.Load(projectPath).Descendants().Where(r => r.Name.LocalName == "OutputType").Select(r => r.Value.Trim()).ToArray();
               if (declared.Length > 0) return declared.Any(type => type.Equals("WinExe", StringComparison.OrdinalIgnoreCase) || type.Equals("Exe", StringComparison.OrdinalIgnoreCase));
               var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
               foreach (var arg in new[] { "msbuild", projectPath, "-nologo", "-getProperty:OutputType" }) start.ArgumentList.Add(arg);
               using var process = Process.Start(start)!;
               var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd(); process.WaitForExit();
               return process.ExitCode == 0 && output.Trim() is "Exe" or "WinExe";
            }
            catch (Exception x) when (x is System.Xml.XmlException or IOException) {
               return false;
            }
         }
      }

      public static IReadOnlyList<ReleasePlatform> ReadHostPlatforms(string projectPath) {
         var values = XDocument.Load(projectPath).Descendants().Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks").SelectMany(e => e.Value.Split(';')).ToArray();
         var platforms = new List<ReleasePlatform>();
         if (values.Any(f => f.EndsWith("-android", StringComparison.Ordinal))) platforms.Add(ReleasePlatform.Android);
         if (values.Length == 0 || values.Any(f => f.Contains("-windows", StringComparison.Ordinal) || !f.Contains('-'))) platforms.Add(ReleasePlatform.Windows);
         return platforms;
      }

      /// <summary>
      /// The reason <paramref name="publishFolder"/> must not be emptied, or <c>null</c> when it is safe:
      /// a drive root, the <c>.slnx</c> folder or a folder above it, and the user's profile folder itself
      /// are refused.
      /// </summary>
      public static string? CheckPublishFolder(string publishFolder, string solutionPath) {
         var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(publishFolder));
         if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(folder) ?? ""), folder,
                StringComparison.OrdinalIgnoreCase))
            return "it is the root of a drive";

         var solutionFolder = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
         if (string.Equals(solutionFolder, folder, StringComparison.OrdinalIgnoreCase) ||
             solutionFolder.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return "it contains the solution";

         var profile = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
         if (string.Equals(profile, folder, StringComparison.OrdinalIgnoreCase))
            return "it is the user profile folder";

         return null;
      }

      /// <summary>The number of files and folders inside <paramref name="folder"/>, down to the deepest.</summary>
      public static int CountItems(string folder) =>
         Directory.Exists(folder)
            ? Directory.EnumerateFileSystemEntries(folder, "*", new EnumerationOptions {
               RecurseSubdirectories = true, AttributesToSkip = 0
            }).Count()
            : 0;

      /// <summary>Deletes all content of <paramref name="folder"/> without deleting the folder itself.</summary>
      public static Task ClearFolderAsync(string folder, CancellationToken token) =>
         Task.Run(() => {
            var root = new DirectoryInfo(folder);
            if (!root.Exists) return;

            foreach (var entry in root.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 })) {
               token.ThrowIfCancellationRequested();
               if (entry is DirectoryInfo directory) {
                  foreach (var file in directory.EnumerateFiles("*", new EnumerationOptions {
                              RecurseSubdirectories = true, AttributesToSkip = 0
                           }))
                     file.Attributes = FileAttributes.Normal;
                  directory.Delete(recursive: true);
               }
               else {
                  entry.Attributes = FileAttributes.Normal;
                  entry.Delete();
               }
            }
         }, token);

      /// <summary>
      /// The complete <c>dotnet publish</c> arguments of Prepare for project <paramref name="projectPath"/>
      /// into <paramref name="publishFolder"/>.
      /// </summary>
      public static IReadOnlyList<string> BuildPublishArguments(string projectPath, string publishFolder) => [
         "publish", projectPath,
         "-c", "Release",
         "-r", RuntimeIdentifier,
         "--self-contained", "true",
         "-o", publishFolder,
         .. DeterminismArguments
      ];

      /// <summary>
      /// Runs <c>dotnet publish</c> of the host project into <paramref name="publishFolder"/>, with the
      /// <c>.slnx</c> folder as the working directory. Every output line is passed on to
      /// <paramref name="log"/>. Cancelling <paramref name="token"/> kills the whole process tree.
      /// </summary>
      /// <returns>The <c>dotnet publish</c> exit code; <c>0</c> means success.</returns>
      public static async Task<int> PublishAsync(string solutionPath, string hostProject, string publishFolder,
         IProgress<string> log, CancellationToken token, ReleaseProfile? profile = null) {
         var solutionFolder = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
         var projectPath = Path.GetFullPath(Path.Combine(solutionFolder, hostProject));
         var start = new ProcessStartInfo("dotnet") {
            WorkingDirectory = solutionFolder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
         };
         foreach (var argument in BuildPublishArguments(projectPath, Path.GetFullPath(publishFolder)))
            start.ArgumentList.Add(argument);
         if (profile is not null)
            start.ArgumentList.Add("-p:CustomBeforeMicrosoftCommonTargets=" + WriteIdentityTargets(profile, projectPath));
         // MSBuild worker nodes that outlive the build inherit the output pipes, and the wait below would
         // then last until they time out. Turning reuse off changes nothing in what is built.
         start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
         start.Environment["DOTNET_NOLOGO"] = "1";

         log.Report($"> dotnet {string.Join(' ', start.ArgumentList.Select(Quote))}");
         using var process = new Process { StartInfo = start };
         process.OutputDataReceived += (_, e) => {
            if (e.Data is not null) log.Report(e.Data);
         };
         process.ErrorDataReceived += (_, e) => {
            if (e.Data is not null) log.Report(e.Data);
         };

         process.Start();
         process.BeginOutputReadLine();
         process.BeginErrorReadLine();
         try {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
         }
         catch (OperationCanceledException) {
            try {
               process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) {
               // Already gone.
            }

            throw;
         }

         return process.ExitCode;

         static string Quote(string argument) => argument.Contains(' ') ? $"\"{argument}\"" : argument;
      }

      /// <summary>
      /// Writes the product identity of <paramref name="profile"/> into an MSBuild targets file that applies
      /// it to <paramref name="projectPath"/> only, and returns the file's path.
      /// </summary>
      /// <remarks>
      /// A <c>-p:</c> property is global: it reaches every referenced project too, so passing
      /// <c>AssemblyName</c> that way renames every library after the host (restore stops with an
      /// ambiguous project name) and stamps the product name, app id and logo on all of them. The file is
      /// passed through <c>CustomBeforeMicrosoftCommonTargets</c>, which every project imports after its own
      /// body, and its properties are conditioned on the host's path, so the other projects are untouched.
      /// </remarks>
      public static string WriteIdentityTargets(ReleaseProfile profile, string projectPath) {
         profile.Product.Validate();
         var assets = Path.Combine(Path.GetTempPath(), "EmReleaseAssets", profile.Id);
         Directory.CreateDirectory(assets);
         var properties = new List<(string Name, string Value)> {
            ("Product", profile.Product.AppName), ("Company", profile.Product.Publisher), ("EmAppId", profile.Product.AppId),
            ("AssemblyName", Path.GetFileNameWithoutExtension(profile.Product.AppExe))
         };
         if (profile.Product.IconBase64.Length > 0) {
            var icon = Path.Combine(assets, "Product.ico"); File.WriteAllBytes(icon, Convert.FromBase64String(profile.Product.IconBase64));
            properties.Add(("ApplicationIcon", icon));
         }
         // An empty logo is written too, so a logo set by the project itself is cleared as the profile says.
         var logo = "";
         if (profile.Product.LogoBase64.Length > 0) { logo = Path.Combine(assets, "Product.png"); File.WriteAllBytes(logo, Convert.FromBase64String(profile.Product.LogoBase64)); }
         properties.Add(("EmAppLogo", logo));

         XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
         var group = new XElement(ns + "PropertyGroup",
            new XAttribute("Condition", $"'$(MSBuildProjectFullPath)' == '{EscapeMsBuild(Path.GetFullPath(projectPath))}'"),
            properties.Select(p => new XElement(ns + p.Name, EscapeMsBuild(p.Value))));
         var file = Path.Combine(assets, "Product.targets");
         new XDocument(new XElement(ns + "Project", group)).Save(file);
         return file;

         // MSBuild expands $(...), @(...), %(...) and splits on ';' in property text; escaping keeps the value literal.
         static string EscapeMsBuild(string value) => value.Replace("%", "%25").Replace("$", "%24").Replace("@", "%40")
            .Replace(";", "%3B").Replace("'", "%27").Replace("*", "%2A").Replace("?", "%3F");
      }
   }
}
