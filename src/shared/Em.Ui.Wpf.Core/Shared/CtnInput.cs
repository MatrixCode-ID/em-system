using System.Text.RegularExpressions;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Name rules and the shape of docker commands for the Container Manager screen. The server remains the
   /// authority (it answers 400 for an invalid name); the rules here only exist so the dialog's confirm
   /// button does not turn on for a name that is clearly wrong. The constants are deliberately written
   /// again because the rule class on the server is <c>internal</c> and cannot be referenced from the
   /// client.
   /// </summary>
   public static partial class CtnInput
   {
      /// <summary>Maximum length of a root name.</summary>
      public const int MaxRootName = 64;

      /// <summary>Maximum length of a container name.</summary>
      public const int MaxImageName = 128;

      /// <summary>Maximum length of <c>root/name</c>.</summary>
      public const int MaxFullName = 255;

      /// <summary>Maximum length of a folder name.</summary>
      public const int MaxFolderName = 100;

      /// <summary>Maximum length of a robot name.</summary>
      public const int MaxRobotName = 100;

      /// <summary>Maximum length of a description.</summary>
      public const int MaxDescription = 500;

      /// <summary>The maximum folder depth that the server accepts.</summary>
      public const int MaxFolderDepth = 8;

      // An OCI name component: lowercase letters and digits, separated by ".", "_", "__", or "-" (may repeat).
      [GeneratedRegex(@"^[a-z0-9]+(?:(?:\.|_|__|-+)[a-z0-9]+)*$")]
      private static partial Regex NamePattern();

      [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]*$")]
      private static partial Regex RobotNamePattern();

      /// <summary>Whether <paramref name="name"/> is a valid root or container name with that maximum length.</summary>
      public static bool IsValidName(string? name, int maxLength) =>
         !string.IsNullOrEmpty(name) && name.Length <= maxLength && NamePattern().IsMatch(name);

      /// <summary>Whether <paramref name="name"/> is a valid robot name.</summary>
      public static bool IsValidRobotName(string? name) =>
         !string.IsNullOrEmpty(name) && name.Length <= MaxRobotName && RobotNamePattern().IsMatch(name);

      /// <summary>Whether <paramref name="name"/> is a valid folder name (only about length; its shape is free).</summary>
      public static bool IsValidFolderName(string? name) =>
         !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= MaxFolderName;

      /// <summary>
      /// Takes <c>host[:port]</c> from the server address: the scheme, path, and trailing slash are removed,
      /// because <c>docker</c> does not accept a scheme. Empty when the address cannot be read.
      /// </summary>
      public static string RegistryHost(string? serverAddress) {
         if (string.IsNullOrWhiteSpace(serverAddress)) return "";

         var text = serverAddress.Trim();
         if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)) {
            return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
         }

         var schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
         if (schemeEnd >= 0) text = text[(schemeEnd + 3)..];
         var slash = text.IndexOf('/');
         return (slash >= 0 ? text[..slash] : text).TrimEnd('/');
      }

      /// <summary>
      /// Whether Docker will refuse <c>docker login</c> to this address without extra configuration: Docker
      /// only accepts plain HTTP for <c>localhost</c> and loopback addresses.
      /// </summary>
      public static bool IsInsecureRemote(string? serverAddress) {
         if (string.IsNullOrWhiteSpace(serverAddress)) return false;
         if (!Uri.TryCreate(serverAddress.Trim(), UriKind.Absolute, out var uri)) return false;
         if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) return false;

         return !(uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
      }

      /// <summary>
      /// Time from the server as UTC. A value without a <see cref="DateTimeKind"/> is taken as UTC - that is
      /// how the registry DTOs are written - not as local time, which would shift the hour if converted with
      /// <see cref="DateTime.ToUniversalTime"/>.
      /// </summary>
      public static DateTime AsUtc(DateTime value) => value.Kind switch {
         DateTimeKind.Utc => value,
         DateTimeKind.Local => value.ToUniversalTime(),
         _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
      };

      /// <summary>The full pull name: <c>host/root/name</c>.</summary>
      public static string PullName(string host, string fullName) => host.Length == 0 ? fullName : $"{host}/{fullName}";

      /// <summary><c>docker pull</c> for a tag.</summary>
      public static string DockerPullTag(string host, string fullName, string tag) =>
         $"docker pull {PullName(host, fullName)}:{tag}";

      /// <summary><c>docker pull</c> for a digest.</summary>
      public static string DockerPullDigest(string host, string fullName, string digest) =>
         $"docker pull {PullName(host, fullName)}@{digest}";

      /// <summary><c>docker tag</c> and <c>docker push</c> to send a local image to this container.</summary>
      public static string DockerTagPush(string host, string fullName, string tag) =>
         $"docker tag <local-image> {PullName(host, fullName)}:{tag}{Environment.NewLine}" +
         $"docker push {PullName(host, fullName)}:{tag}";

      /// <summary><c>docker login</c> for a robot; the token is written as part of the command.</summary>
      public static string DockerLogin(string host, string robot, string token) =>
         $"docker login {host} -u {robot} -p {token}";

      /// <summary>The digest shortened for display: <c>sha256:</c> plus the first twelve characters.</summary>
      public static string ShortDigest(string digest) {
         const int Shown = 12;
         var colon = digest.IndexOf(':');
         if (colon < 0) return digest.Length <= Shown ? digest : digest[..Shown] + "…";

         var hash = digest[(colon + 1)..];
         return hash.Length <= Shown ? digest : $"{digest[..(colon + 1)]}{hash[..Shown]}…";
      }
   }
}
