using System.IO;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>Peringatan tabrakan tujuan dan folder publish; tidak memblokir operasi.</summary>
   public static class ReleaseProfileConflicts
   {
      public static IReadOnlyList<string> Find(ReleaseProfile profile, IEnumerable<ReleaseProfile> others, bool checkTarget, bool checkPublishFolder) {
         var result = new List<string>();
         foreach (var other in others.Where(other => other.Id != profile.Id)) {
            try {
               if (checkPublishFolder && !string.IsNullOrWhiteSpace(profile.PublishFolder) && !string.IsNullOrWhiteSpace(other.PublishFolder) && SamePath(profile.PublishFolder, other.PublishFolder))
                  result.Add($"Profile '{other.Name}' uses the same local publish folder '{profile.PublishFolder}'.");
               if (!checkTarget || profile.TargetKind != other.TargetKind || string.IsNullOrWhiteSpace(profile.ReleaseFolder) || string.IsNullOrWhiteSpace(other.ReleaseFolder)) continue;
               var folder = ReleaseTarget.NormalizeReleaseFolder(profile.ReleaseFolder);
               var otherFolder = ReleaseTarget.NormalizeReleaseFolder(other.ReleaseFolder);
               if (profile.TargetKind == ReleaseTargetKind.Cdn && string.Equals(folder, otherFolder, StringComparison.OrdinalIgnoreCase))
                  result.Add($"Profile '{other.Name}' publishes to the same target (server CDN, folder '{folder}').");
               else if (profile.TargetKind == ReleaseTargetKind.Folder && !string.IsNullOrWhiteSpace(profile.TargetFolder) && !string.IsNullOrWhiteSpace(other.TargetFolder) &&
                        SamePath(Path.Combine(profile.TargetFolder, folder), Path.Combine(other.TargetFolder, otherFolder)))
                  result.Add($"Profile '{other.Name}' publishes to the same target folder.");
            }
            catch (Exception x) when (x is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException) { }
         }
         return result;
      }
      private static bool SamePath(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
   }
}
