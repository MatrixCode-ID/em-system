using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Em.Shared;

namespace Em.Api.Core;

internal sealed partial class CdnStore {
   internal const string SkinFolderName = ".skin";
   internal static bool IsReservedRootName(string? path) =>
      SplitSegments(path).FirstOrDefault()?.Equals(SkinFolderName, StringComparison.OrdinalIgnoreCase) == true;
   private static void EnsureNotReserved(string? path) {
      if (IsReservedRootName(path)) throw new ActionException("The root CDN name '.skin' is reserved for skin assets.", 400);
   }
   private bool IsSkinPhysical(string path) => IsReservedRootName(ToRelative(path));
   internal IReadOnlyDictionary<string, string> PrepareSkinAssets(EmSkinAssets? assets) {
      if (!IsEnabled) return ReadOnlyDictionary<string, string>.Empty;
      // ResolveInside also refuses a reparse point at the root or in the reserved folder.
      var target = ResolveInside(SkinFolderName, allowSkin: true);
      var expected = Path.GetFullPath(Path.Combine(RootPath, SkinFolderName));
      if (!string.Equals(target, expected, PathComparison) ||
          !string.Equals(Path.GetDirectoryName(target), Path.TrimEndingDirectorySeparator(RootPath), PathComparison))
         throw new InvalidOperationException("Invalid CDN skin asset directory.");
      try {
         // Validate every embedded path before clearing the previous assets.
         var resources = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
         if (assets is not null) {
            var prefix = assets.Prefix.Replace('\\', '/');
            if (prefix.Length == 0 || !prefix.EndsWith('/')) throw new InvalidOperationException("The skin resource prefix must end with '/'.");
            foreach (var name in assets.Assembly.GetManifestResourceNames()) {
               var normalized = name.Replace('\\', '/');
               if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) continue;
               var relative = normalized[prefix.Length..];
               if (relative.Length == 0 || relative.StartsWith('/')) throw new InvalidOperationException("Invalid skin resource path.");
               ResolveInside(SkinFolderName + "/" + relative, allowSkin: true);
               if (!resources.TryAdd(relative, EmbeddedFile.From(assets.Assembly, name).ReadBytes()))
                  throw new InvalidOperationException($"Duplicate skin resource path '{relative}'.");
            }
         }
         if (assets is not null && resources.Count == 0)
            throw new InvalidOperationException($"No embedded skin resources match prefix '{assets.Prefix}'.");
         if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
         if (File.Exists(target)) throw new IOException("The CDN skin directory is occupied by a file.");
         var urls = new Dictionary<string, string>(StringComparer.Ordinal);
         if (assets is not null) Directory.CreateDirectory(target);
         foreach (var (relative, bytes) in resources) {
            var path = ResolveInside(SkinFolderName + "/" + relative, allowSkin: true);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            urls.Add(relative, "/cdn/.skin/" + string.Join('/', relative.Split('/').Select(Uri.EscapeDataString)) + "?v=" + hash);
         }
         return new ReadOnlyDictionary<string, string>(urls);
      }
      catch (Exception ex) {
         throw new InvalidOperationException("Could not initialize CDN skin assets. Startup cannot continue.", ex);
      }
   }
}
