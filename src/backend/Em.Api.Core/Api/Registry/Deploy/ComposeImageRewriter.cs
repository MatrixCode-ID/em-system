using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Em.Api.Core.Registry.Deploy
{
   /// <summary>
   /// Reads and edits a compose file and its <c>.env</c> as text. YamlDotNet only locates the
   /// <c>image:</c> value of a service; the replacement is done on that character range, so comments,
   /// ordering and formatting of the rest of the file stay exactly as they were.
   /// </summary>
   internal static partial class ComposeImageRewriter
   {
      [GeneratedRegex("[^A-Z0-9]")]
      private static partial Regex NotVariableChar();

      [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
      private static partial Regex VariableName();

      /// <summary>Default image variable of a service: <c>EM_IMAGE_&lt;SERVICE&gt;</c>, upper case, other characters as <c>_</c>.</summary>
      public static string DefaultVariable(string service) =>
         "EM_IMAGE_" + NotVariableChar().Replace(service.ToUpperInvariant(), "_");

      /// <summary><c>true</c> for a name usable as an environment variable.</summary>
      public static bool IsValidVariable(string? name) => name is not null && VariableName().IsMatch(name);

      /// <summary>Names of the services in the compose file, in file order.</summary>
      /// <exception cref="InvalidDataException">The file is not valid YAML or has no <c>services</c> mapping.</exception>
      public static string[] Services(string compose) => [.. ReadServices(compose).Select(s => s.Name)];

      /// <summary>Current <c>image:</c> value of <paramref name="service"/>, or <c>null</c> when it has none.</summary>
      /// <exception cref="InvalidDataException">The service does not exist or the file cannot be read.</exception>
      public static string? ImageOf(string compose, string service) => Find(compose, service).Image?.Value;

      /// <summary><c>true</c> when the image of <paramref name="service"/> refers to <paramref name="variable"/>.</summary>
      public static bool UsesVariable(string compose, string service, string variable) =>
         ImageOf(compose, service) is { } image && ReferencesVariable(image, variable);

      /// <summary><c>true</c> when an image value contains <c>${VARIABLE}</c>, <c>${VARIABLE:-...}</c> or <c>$VARIABLE</c>.</summary>
      public static bool ReferencesVariable(string image, string variable) =>
         Regex.IsMatch(image, @"\$\{" + Regex.Escape(variable) + @"(?:[:?\-+}])|\$" + Regex.Escape(variable) + @"(?![A-Za-z0-9_])");

      /// <summary>
      /// Replaces the image of <paramref name="service"/> with <c>${VARIABLE}</c>. A value that already starts with
      /// <c>${</c> is left alone.
      /// </summary>
      /// <returns>The new text, whether it changed, and the old image value.</returns>
      /// <exception cref="InvalidDataException">The service does not exist or has no plain <c>image:</c> value.</exception>
      public static (string Content, bool Changed, string OldImage) Rewrite(string compose, string service, string variable) {
         var found = Find(compose, service);
         if (found.Image is not { } image) {
            throw new InvalidDataException($"Service '{service}' has no image: line. Add one (for example image: ${{{variable}}}) before deploying.");
         }

         if (image.Value.TrimStart().StartsWith("${", StringComparison.Ordinal)) return (compose, false, image.Value);

         var replaced = compose[..image.Start] + "${" + variable + "}" + compose[image.End..];
         return (replaced, true, image.Value);
      }

      /// <summary>Value of <paramref name="variable"/> in a <c>.env</c> text, without quotes; <c>null</c> when absent.</summary>
      public static string? ReadEnv(string? env, string variable) {
         foreach (var line in (env ?? "").Split('\n')) {
            if (EnvLine(line, variable) is { } value) return Unquote(value.Trim());
         }

         return null;
      }

      /// <summary>
      /// Sets <paramref name="variable"/> in a <c>.env</c> text: the existing line is replaced, other lines and
      /// the line ending style are kept, and a missing variable is appended.
      /// </summary>
      public static string SetEnv(string? env, string variable, string value) {
         var text = env ?? "";
         var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
         var lines = text.Length == 0 ? [] : text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
         if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

         var entry = variable + "=" + value;
         var index = lines.FindIndex(l => EnvLine(l, variable) is not null);
         if (index >= 0) lines[index] = entry;
         else lines.Add(entry);
         return string.Join(newline, lines) + newline;
      }

      /// <summary>Digest part (<c>sha256:...</c>) of an image reference, or <c>null</c> when it has none.</summary>
      public static string? DigestOf(string? reference) {
         var at = reference?.LastIndexOf('@') ?? -1;
         return at < 0 ? null : reference![(at + 1)..];
      }

      private static string? EnvLine(string line, string variable) {
         var trimmed = line.TrimStart();
         if (trimmed.StartsWith("export ", StringComparison.Ordinal)) trimmed = trimmed[7..].TrimStart();
         if (!trimmed.StartsWith(variable, StringComparison.Ordinal)) return null;

         var rest = trimmed[variable.Length..].TrimStart();
         return rest.StartsWith('=') ? rest[1..].TrimEnd('\r') : null;
      }

      private static string Unquote(string value) =>
         value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\'') ? value[1..^1] : value;

      private sealed record ImageValue(string Value, int Start, int End);

      private sealed record ServiceEntry(string Name, ImageValue? Image);

      private static ServiceEntry Find(string compose, string service) =>
         ReadServices(compose).FirstOrDefault(s => s.Name == service)
         ?? throw new InvalidDataException($"Service '{service}' was not found in the compose file.");

      private static List<ServiceEntry> ReadServices(string compose) {
         try {
            var parser = new Parser(new StringReader(compose));
            parser.Consume<StreamStart>();
            if (!parser.TryConsume<DocumentStart>(out _) || !parser.TryConsume<MappingStart>(out _)) {
               throw new InvalidDataException("The compose file is not a YAML mapping.");
            }

            List<ServiceEntry>? services = null;
            while (!parser.TryConsume<MappingEnd>(out _)) {
               if (parser.TryConsume<Scalar>(out var key) && key.Value == "services") {
                  services = ReadServiceMap(parser, compose);
               }
               else {
                  if (key is null) parser.SkipThisAndNestedEvents();
                  parser.SkipThisAndNestedEvents();
               }
            }

            return services ?? throw new InvalidDataException("The compose file has no services section.");
         }
         catch (YamlException x) {
            throw new InvalidDataException($"The compose file is not valid YAML: {x.Message}", x);
         }
      }

      private static List<ServiceEntry> ReadServiceMap(IParser parser, string compose) {
         var result = new List<ServiceEntry>();
         if (!parser.TryConsume<MappingStart>(out _)) {
            parser.SkipThisAndNestedEvents();
            return result;
         }

         while (!parser.TryConsume<MappingEnd>(out _)) {
            if (!parser.TryConsume<Scalar>(out var name)) {
               parser.SkipThisAndNestedEvents();
               parser.SkipThisAndNestedEvents();
               continue;
            }

            ImageValue? image = null;
            if (parser.TryConsume<MappingStart>(out _)) {
               while (!parser.TryConsume<MappingEnd>(out _)) {
                  if (parser.TryConsume<Scalar>(out var field) && field.Value == "image") {
                     if (parser.TryConsume<Scalar>(out var value)) {
                        image = new ImageValue(value.Value, (int)value.Start.Index, (int)value.End.Index);
                     }
                     else {
                        throw new InvalidDataException($"The image of service '{name.Value}' must be a plain value, not an alias or a list.");
                     }
                  }
                  else {
                     if (field is null) parser.SkipThisAndNestedEvents();
                     parser.SkipThisAndNestedEvents();
                  }
               }
            }
            else {
               parser.SkipThisAndNestedEvents();
            }

            result.Add(new ServiceEntry(name.Value, image));
         }

         return result;
      }

      /// <summary>Removes characters a compose service or container name cannot have; for the Create stack template.</summary>
      public static string SafeName(string name) {
         var builder = new StringBuilder();
         foreach (var c in name.ToLowerInvariant()) builder.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-');
         var result = builder.ToString().Trim('-', '.', '_');
         return result.Length == 0 ? "app" : result;
      }
   }
}
