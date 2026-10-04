using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// Tahap Prepare Release Manager: memeriksa .NET SDK, membaca daftar project host dari <c>.slnx</c>,
   /// mengosongkan local publish folder dengan aman, lalu menjalankan <c>dotnet publish</c>
   /// self-contained <c>win-x64</c> dengan argumen yang membuat hasilnya deterministik.
   /// </summary>
   public static class ReleaseBuilder
   {
      /// <summary>Runtime identifier hasil publish.</summary>
      public const string RuntimeIdentifier = "win-x64";

      /// <summary>
      /// Argumen tambahan yang membuat build yang sama menghasilkan byte yang sama: tanpa hash commit di
      /// versi informasional, path sumber yang dinormalkan, compile deterministik, dan tanpa single-file.
      /// Hanya dipakai Prepare; build harian tidak berubah.
      /// </summary>
      public static readonly IReadOnlyList<string> DeterminismArguments = [
         "-p:IncludeSourceRevisionInInformationalVersion=false",
         "-p:ContinuousIntegrationBuild=true",
         "-p:Deterministic=true",
         "-p:PublishSingleFile=false"
      ];

      /// <summary>
      /// Versi .NET SDK 10.x pertama yang terpasang menurut <c>dotnet --list-sdks</c>, atau <c>null</c> kalau
      /// tidak ada (atau <c>dotnet</c> sendiri tidak ditemukan).
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
      /// Project di <c>.slnx</c> yang menghasilkan aplikasi (<c>OutputType</c> <c>WinExe</c> atau <c>Exe</c>),
      /// path-nya seperti tertulis di <c>.slnx</c> (relatif terhadap foldernya).
      /// </summary>
      public static IReadOnlyList<string> ReadHostProjects(string solutionPath) {
         var folder = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
         return XDocument.Load(solutionPath)
            .Descendants()
            .Where(r => r.Name.LocalName == "Project")
            .Select(r => (string?)r.Attribute("Path"))
            .OfType<string>()
            .Where(r => IsApplication(Path.Combine(folder, r)))
            .ToArray();

         static bool IsApplication(string projectPath) {
            if (!File.Exists(projectPath)) return false;
            try {
               return XDocument.Load(projectPath).Descendants()
                  .Where(r => r.Name.LocalName == "OutputType")
                  .Any(r => r.Value.Trim() is { } type &&
                            (type.Equals("WinExe", StringComparison.OrdinalIgnoreCase) ||
                             type.Equals("Exe", StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception x) when (x is System.Xml.XmlException or IOException) {
               return false;
            }
         }
      }

      /// <summary>
      /// Alasan <paramref name="publishFolder"/> tidak boleh dikosongkan, atau <c>null</c> kalau aman:
      /// akar drive, folder <c>.slnx</c> atau folder di atasnya, dan folder profil user itu sendiri ditolak.
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

      /// <summary>Jumlah file dan folder di dalam <paramref name="folder"/>, sampai yang terdalam.</summary>
      public static int CountItems(string folder) =>
         Directory.Exists(folder)
            ? Directory.EnumerateFileSystemEntries(folder, "*", new EnumerationOptions {
               RecurseSubdirectories = true, AttributesToSkip = 0
            }).Count()
            : 0;

      /// <summary>Menghapus seluruh isi <paramref name="folder"/> tanpa menghapus folder itu sendiri.</summary>
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
      /// Argumen lengkap <c>dotnet publish</c> milik Prepare untuk project <paramref name="projectPath"/>
      /// ke <paramref name="publishFolder"/>.
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
      /// Menjalankan <c>dotnet publish</c> project host ke <paramref name="publishFolder"/>, dengan folder
      /// <c>.slnx</c> sebagai working directory. Setiap baris output diteruskan ke <paramref name="log"/>.
      /// Membatalkan <paramref name="token"/> membunuh seluruh pohon proses.
      /// </summary>
      /// <returns>Exit code <c>dotnet publish</c>; <c>0</c> berarti berhasil.</returns>
      public static async Task<int> PublishAsync(string solutionPath, string hostProject, string publishFolder,
         IProgress<string> log, CancellationToken token) {
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
   }
}
