using System.Text;
using Em.Api.Core.Models;

namespace Em.Api.Core.Registry.Deploy
{
   /// <summary>A deploy target with its credentials decrypted; lives only inside one request.</summary>
   internal sealed class CtnDeployTarget
   {
      public CtnDeployKind Kind { get; init; }
      public CtnDeployMode Mode { get; init; }
      public string Host { get; init; } = "";
      public int? Port { get; init; }
      public string? User { get; init; }
      public CtnDeployAuth Auth { get; init; }
      public string? Secret { get; init; }
      public string? Passphrase { get; init; }
      public string? Fingerprint { get; init; }
      public int? EndpointId { get; init; }
      public string? Stack { get; init; }
      public int? StackId { get; init; }
      public string? Service { get; init; }
      public string? Container { get; init; }
      public string? ImageVariable { get; init; }
      public string RegistryHost { get; init; } = "";
      public string? RegistryUser { get; init; }
      public string? RegistrySecret { get; init; }
      public string? TagFilter { get; init; }

      /// <summary>Variable that holds the image: the configured one, or <c>EM_IMAGE_&lt;SERVICE&gt;</c>.</summary>
      public string Variable => string.IsNullOrWhiteSpace(ImageVariable)
         ? ComposeImageRewriter.DefaultVariable(Service ?? "")
         : ImageVariable.Trim();

      /// <summary><c>true</c> when a registry login is configured.</summary>
      public bool HasRegistryLogin => !string.IsNullOrEmpty(RegistryUser) && !string.IsNullOrEmpty(RegistrySecret);

      /// <summary>Every credential, so the log can mask them.</summary>
      public IEnumerable<string?> Secrets => [Secret, Passphrase, RegistrySecret];
   }

   /// <summary>
   /// Step log of a deploy. Every line passes through the mask first, so credentials (and their Base64 forms) never
   /// reach the history or the client.
   /// </summary>
   internal sealed class CtnDeployLog
   {
      private const int MaxCommandOutput = 4000;
      private readonly StringBuilder _text = new();
      private readonly List<string> _secrets = [];

      public CtnDeployLog(IEnumerable<string?>? secrets = null) {
         foreach (var secret in secrets ?? []) AddSecret(secret);
      }

      public void AddSecret(string? secret) {
         if (string.IsNullOrEmpty(secret)) return;

         _secrets.Add(secret);
         _secrets.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)));
         // A private key may be echoed line by line.
         foreach (var line in secret.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
            if (line.Length >= 16) _secrets.Add(line);
         }
      }

      public void Line(string text) {
         lock (_text) _text.Append('[').Append(DateTime.UtcNow.ToString("HH:mm:ss")).Append("] ").AppendLine(Mask(text));
      }

      public void Warn(string text) => Line("Warning: " + text);

      /// <summary>Logs the output of a command, keeping only its end when it is long.</summary>
      public void Output(string? output) {
         var text = (output ?? "").Trim();
         if (text.Length == 0) return;
         if (text.Length > MaxCommandOutput) text = "..." + text[^MaxCommandOutput..];
         foreach (var line in text.Split('\n')) Line("  " + line.TrimEnd('\r'));
      }

      public string Mask(string text) {
         foreach (var secret in _secrets.OrderByDescending(s => s.Length)) text = text.Replace(secret, "***", StringComparison.Ordinal);
         return text;
      }

      public override string ToString() {
         lock (_text) return _text.ToString();
      }
   }
}
