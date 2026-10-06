using System.Text.RegularExpressions;
using Em.Shared;

namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Aturan nama dan batas panjang registry. Angka mengikuti praktik <c>distribution/registry</c> dan
   /// Docker Hub; semuanya konstanta di sini, bukan bagian skema tabel.
   /// </summary>
   internal static partial class CtnNames
   {
      public const int MaxRootName = 64;
      public const int MaxImageName = 128;
      public const int MaxFullName = 255;
      public const int MaxTag = 128;
      public const int MaxFolderDepth = 8;
      public const int MaxFolderName = 100;
      public const int MaxRobotName = 100;
      public const int MaxDescription = 500;

      public const int StateDisabled = 0;
      public const int StateActive = 1;

      public const string ManifestDockerV2 = "application/vnd.docker.distribution.manifest.v2+json";
      public const string ManifestDockerList = "application/vnd.docker.distribution.manifest.list.v2+json";
      public const string ManifestOciImage = "application/vnd.oci.image.manifest.v1+json";
      public const string ManifestOciIndex = "application/vnd.oci.image.index.v1+json";

      public const string AccessRead = "R";
      public const string AccessWrite = "W";

      // Komponen nama OCI: huruf kecil dan angka, dipisah ".", "_", "__", atau "-" (boleh berulang).
      [GeneratedRegex(@"^[a-z0-9]+(?:(?:\.|_|__|-+)[a-z0-9]+)*$")]
      private static partial Regex NamePattern();

      [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9._-]{0,127}$")]
      private static partial Regex TagPattern();

      [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]*$")]
      private static partial Regex RobotNamePattern();

      [GeneratedRegex(@"^sha256:[a-f0-9]{64}$")]
      private static partial Regex DigestPattern();

      public static bool IsValidName(string? name, int maxLength) =>
         !string.IsNullOrEmpty(name) && name.Length <= maxLength && NamePattern().IsMatch(name);

      public static bool IsValidTag(string? tag) => !string.IsNullOrEmpty(tag) && TagPattern().IsMatch(tag);

      public static bool IsValidDigest(string? digest) => !string.IsNullOrEmpty(digest) && DigestPattern().IsMatch(digest);

      public static bool IsValidRobotName(string? name) =>
         !string.IsNullOrEmpty(name) && name.Length <= MaxRobotName && RobotNamePattern().IsMatch(name);

      public static string ValidateRootName(string? name) {
         if (!IsValidName(name, MaxRootName)) {
            throw new ActionException(
               $"Root name must be 1-{MaxRootName} characters: lowercase letters and digits, separated by '.', '_' or '-'.", 400);
         }

         return name!;
      }

      public static string ValidateImageName(string rootName, string? name) {
         if (!IsValidName(name, MaxImageName)) {
            throw new ActionException(
               $"Container name must be 1-{MaxImageName} characters: lowercase letters and digits, separated by '.', '_' or '-'.", 400);
         }

         if (rootName.Length + 1 + name!.Length > MaxFullName) {
            throw new ActionException($"'{rootName}/{name}' is longer than {MaxFullName} characters.", 400);
         }

         return name;
      }

      public static string ValidateFolderName(string? name) {
         var trimmed = name?.Trim();
         if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxFolderName) {
            throw new ActionException($"Folder name must be 1-{MaxFolderName} characters.", 400);
         }

         return trimmed;
      }

      public static string ValidateRobotName(string? name) {
         if (!IsValidRobotName(name)) {
            throw new ActionException(
               $"Robot name must be 1-{MaxRobotName} characters: lowercase letters, digits, '.', '_' or '-', starting with a letter or digit.", 400);
         }

         return name!;
      }

      public static string? ValidateDescription(string? description) {
         var trimmed = description?.Trim();
         if (string.IsNullOrEmpty(trimmed)) return null;
         if (trimmed.Length > MaxDescription) {
            throw new ActionException($"Description must not be longer than {MaxDescription} characters.", 400);
         }

         return trimmed;
      }

      public static string ValidateAccess(string? access) {
         var value = access?.Trim().ToUpperInvariant();
         return value is AccessRead or AccessWrite
            ? value
            : throw new ActionException("Access must be 'R' (pull) or 'W' (push).", 400);
      }
   }
}
