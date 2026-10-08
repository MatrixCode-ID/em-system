using System.Reflection;
using System.Text.RegularExpressions;

namespace Em.Shared
{
   /// <summary>
   /// The product version of the running application, read from the informational version of the entry
   /// assembly.
   /// </summary>
   /// <remarks>
   /// A release pipeline passes the version from its release tag to the build (<c>-p:Version=1.3.0-alpha.1</c>);
   /// a build without a tag keeps the host's default <see cref="DevVersion"/> and is shown as <c>dev</c>.
   /// See <c>doc/engine/build.md#product-version</c>.
   /// </remarks>
   public static partial class AppVersion
   {
      /// <summary>The version a host declares for builds that do not come from a release tag.</summary>
      public const string DevVersion = "0.0.0-dev";

      /// <summary>The text shown for a development build.</summary>
      public const string DevText = "dev";

      private static readonly Lazy<string> _informational = new(ReadInformational);

      /// <summary>
      /// The full informational version of the entry assembly, including build metadata (<c>+commit</c>)
      /// when the build added it. <see cref="DevVersion"/> when there is no entry assembly.
      /// </summary>
      public static string Informational => _informational.Value;

      /// <summary>The product version without build metadata, e.g. <c>1.3.0-alpha.1</c>.</summary>
      public static string Current => StripMetadata(Informational);

      /// <summary>Whether the running build is a development build (see <see cref="IsDevVersion"/>).</summary>
      public static bool IsDev => IsDevVersion(Current);

      /// <summary>The version as shown to the user: <c>v1.3.0-alpha.1</c>, or <c>dev</c> for a development build.</summary>
      public static string Display => ToDisplay(Current);

      /// <summary>
      /// Whether <paramref name="version"/> marks a development build: empty, or <see cref="DevVersion"/>
      /// with or without build metadata.
      /// </summary>
      public static bool IsDevVersion(string? version) =>
         string.IsNullOrWhiteSpace(version) ||
         StripMetadata(version).Equals(DevVersion, StringComparison.OrdinalIgnoreCase);

      /// <summary>Formats <paramref name="version"/> for display: <c>v</c> + version, or <c>dev</c>.</summary>
      public static string ToDisplay(string? version) =>
         IsDevVersion(version) ? DevText : "v" + StripMetadata(version!);

      /// <summary>Removes semver build metadata (everything from <c>+</c>) from <paramref name="version"/>.</summary>
      public static string StripMetadata(string version) {
         var plus = version.IndexOf('+');
         return (plus < 0 ? version : version[..plus]).Trim();
      }

      /// <summary>
      /// Reads a release tag as a product version. Accepts <c>MAJOR.MINOR.PATCH</c> with an optional
      /// pre-release part, with or without a leading <c>v</c> (<c>v1.3.0-alpha.1</c> gives <c>1.3.0-alpha.1</c>).
      /// Floating tags such as <c>latest</c> or <c>beta</c> are not versions.
      /// </summary>
      /// <param name="tag">The release tag or container version tag.</param>
      /// <param name="version">The version without the leading <c>v</c>, or an empty string.</param>
      /// <returns><c>true</c> when the tag is a version.</returns>
      public static bool TryFromTag(string? tag, out string version) {
         version = "";
         if (string.IsNullOrWhiteSpace(tag)) return false;
         var text = tag.Trim();
         if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
         if (!SemVer().IsMatch(text)) return false;
         version = text;
         return true;
      }

      private static string ReadInformational() {
         var assembly = Assembly.GetEntryAssembly();
         var text = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
         return string.IsNullOrWhiteSpace(text) ? DevVersion : text.Trim();
      }

      [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$",
         RegexOptions.CultureInvariant)]
      private static partial Regex SemVer();
   }
}
