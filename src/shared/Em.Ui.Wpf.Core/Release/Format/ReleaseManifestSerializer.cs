using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// Writes and reads <c>release.json</c> and <c>release.json.sig</c> according to
   /// <c>doc/release-format.md</c>. Writing always produces the same form for the same content (files
   /// sorted, UTF-8 without BOM, line ending <c>\n</c>); reading checks every rule of the format and
   /// refuses a manifest that breaks even one.
   /// </summary>
   public static class ReleaseManifestSerializer
   {
      private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

      // Relaxed escaping keeps names such as "a+b.dll" readable; any reader decodes both forms alike.
      private static readonly JsonWriterOptions WriterOptions = new() {
         Indented = true,
         NewLine = "\n",
         Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
      };

      /// <summary>
      /// Writes the manifest as the <c>release.json</c> bytes. Files are sorted by path ordinally, and the
      /// issue time is written with second precision. Paths and hashes are checked first, so an invalid
      /// manifest is never written.
      /// </summary>
      /// <exception cref="ReleaseFormatException">A path or hash breaks the format rules.</exception>
      public static byte[] Serialize(ReleaseManifest manifest) {
         var files = manifest.Files.OrderBy(r => r.Path, StringComparer.Ordinal).ToArray();
         ValidateFiles(files);
         ValidateLauncher(manifest.Launcher, files);

         using var buffer = new MemoryStream();
         using (var writer = new Utf8JsonWriter(buffer, WriterOptions)) {
            writer.WriteStartObject();
            writer.WriteString("publishedAtUtc", ToUtc(manifest.PublishedAtUtc)
               .ToString(TimeFormat, CultureInfo.InvariantCulture));
            if (manifest.Launcher is { } launcher) {
               writer.WriteStartObject("launcher");
               writer.WriteString("fileName", launcher.FileName);
               writer.WriteString("version", launcher.Version);
               writer.WriteString("inputHash", launcher.InputHash);
               writer.WriteEndObject();
            }
            writer.WriteStartArray("files");
            foreach (var file in files) {
               writer.WriteStartObject();
               writer.WriteString("path", file.Path);
               writer.WriteNumber("size", file.Size);
               writer.WriteString("sha256", file.Sha256);
               writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
         }

         buffer.WriteByte((byte)'\n');
         return buffer.ToArray();
      }

      /// <summary>
      /// Reads the <c>release.json</c> bytes. Unknown fields are ignored; every rule for paths, sizes, and
      /// hashes is checked.
      /// </summary>
      /// <remarks>
      /// A reader that receives a manifest from outside must verify its signature first
      /// (<see cref="ReleaseSignature.Verify"/>), and only then call this method.
      /// </remarks>
      /// <exception cref="ReleaseFormatException">The manifest is not valid.</exception>
      public static ReleaseManifest Deserialize(ReadOnlySpan<byte> bytes) {
         try {
            using var document = JsonDocument.Parse(bytes.ToArray());
            var root = RequireKind(document.RootElement, JsonValueKind.Object, "release.json");

            var published = RequireString(root, "publishedAtUtc");
            if (!published.EndsWith('Z') || !DateTime.TryParse(published, CultureInfo.InvariantCulture,
                   DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var publishedAt)) {
               throw new ReleaseFormatException($"'publishedAtUtc' is not an ISO 8601 UTC time: '{published}'.");
            }

            var filesElement = RequireKind(RequireProperty(root, "files"), JsonValueKind.Array, "files");
            var files = new List<ReleaseFile>();
            foreach (var item in filesElement.EnumerateArray()) {
               RequireKind(item, JsonValueKind.Object, "files[]");
               var sizeElement = RequireKind(RequireProperty(item, "size"), JsonValueKind.Number, "size");
               if (!sizeElement.TryGetInt64(out var size) || size < 0) {
                  throw new ReleaseFormatException($"'size' must be a non-negative integer: {sizeElement.GetRawText()}.");
               }

               files.Add(new ReleaseFile {
                  Path = RequireString(item, "path"),
                  Size = size,
                  Sha256 = RequireString(item, "sha256")
               });
            }

            ValidateFiles(files);
            ReleaseLauncherMetadata? launcher = null;
            if (root.TryGetProperty("launcher", out var metadata)) {
               RequireKind(metadata, JsonValueKind.Object, "launcher");
               launcher = new() { FileName = RequireString(metadata, "fileName"), Version = RequireString(metadata, "version"), InputHash = RequireString(metadata, "inputHash") };
               ValidateLauncher(launcher, files);
            }
            return new ReleaseManifest { PublishedAtUtc = publishedAt, Files = files, Launcher = launcher };
         }
         catch (JsonException x) {
            throw new ReleaseFormatException($"release.json is not valid JSON: {x.Message}", x);
         }
      }

      /// <summary>Writes the content of <c>release.json.sig</c> (UTF-8 without BOM, line ending <c>\n</c>).</summary>
      public static byte[] SerializeSignature(ReleaseSignatureFile signature) {
         using var buffer = new MemoryStream();
         using (var writer = new Utf8JsonWriter(buffer, WriterOptions)) {
            writer.WriteStartObject();
            writer.WriteString("keyId", signature.KeyId);
            writer.WriteString("signature", signature.Signature);
            writer.WriteEndObject();
         }

         buffer.WriteByte((byte)'\n');
         return buffer.ToArray();
      }

      /// <summary>Reads the content of <c>release.json.sig</c>. Unknown fields are ignored.</summary>
      /// <exception cref="ReleaseFormatException">The JSON is corrupt or a required field is missing.</exception>
      public static ReleaseSignatureFile DeserializeSignature(ReadOnlySpan<byte> bytes) {
         try {
            using var document = JsonDocument.Parse(bytes.ToArray());
            var root = RequireKind(document.RootElement, JsonValueKind.Object, "release.json.sig");
            return new ReleaseSignatureFile {
               KeyId = RequireString(root, "keyId"),
               Signature = RequireString(root, "signature")
            };
         }
         catch (JsonException x) {
            throw new ReleaseFormatException($"release.json.sig is not valid JSON: {x.Message}", x);
         }
      }

      /// <summary>
      /// The reason <paramref name="path"/> is not valid as a release file path (<c>doc/release-format.md</c>
      /// section 2.3), or <c>null</c> when it is valid. Uniqueness is not checked here.
      /// </summary>
      public static string? CheckPath(string path) {
         if (path.Length == 0) return "the path is empty";
         if (path.Contains('\\')) return "the path contains '\\'; use '/'";
         if (path.StartsWith('/')) return "the path is absolute";
         if (path.Contains(':')) return "the path contains ':'";

         foreach (var segment in path.Split('/')) {
            if (segment.Length == 0) return "the path has an empty segment";
            if (segment.StartsWith('.')) return $"the segment '{segment}' starts with '.'";
         }

         return null;
      }

      /// <summary><c>true</c> when <paramref name="hash"/> is 64 lowercase hex characters.</summary>
      public static bool IsValidSha256(string hash) =>
         hash.Length == 64 && hash.All(r => r is >= '0' and <= '9' or >= 'a' and <= 'f');

      private static void ValidateFiles(IEnumerable<ReleaseFile> files) {
         var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
         foreach (var file in files) {
            if (CheckPath(file.Path) is { } reason) {
               throw new ReleaseFormatException($"Invalid path '{file.Path}': {reason}.");
            }

            if (!seen.Add(file.Path)) {
               throw new ReleaseFormatException($"The path '{file.Path}' is listed more than once (ignoring case).");
            }

            if (file.Size < 0) {
               throw new ReleaseFormatException($"'{file.Path}' has a negative size.");
            }

            if (!IsValidSha256(file.Sha256)) {
               throw new ReleaseFormatException($"'{file.Path}' has an invalid sha256 '{file.Sha256}'.");
            }
         }
      }

      private static void ValidateLauncher(ReleaseLauncherMetadata? launcher, IEnumerable<ReleaseFile> files) {
         if (launcher is null) return;
         if (CheckPath(launcher.FileName) is not null || launcher.FileName.Contains('/') ||
             launcher.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !launcher.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
             !files.Any(f => string.Equals(f.Path, launcher.FileName, StringComparison.OrdinalIgnoreCase)))
            throw new ReleaseFormatException("launcher.fileName must name a listed executable in the binaries root.");
         if (!IsValidSha256(launcher.InputHash)) throw new ReleaseFormatException("Invalid launcher.inputHash.");
         if (!System.Text.RegularExpressions.Regex.IsMatch(launcher.Version, @"^\d+\.\d+\.\d+(?:-(?:prealpha|alpha|beta|rc)\.[1-9]\d*)?$"))
            throw new ReleaseFormatException("Invalid launcher.version.");
      }

      private static DateTime ToUtc(DateTime value) => value.Kind switch {
         DateTimeKind.Utc => value,
         DateTimeKind.Local => value.ToUniversalTime(),
         _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
      };

      private static JsonElement RequireProperty(JsonElement element, string name) =>
         element.TryGetProperty(name, out var value)
            ? value
            : throw new ReleaseFormatException($"The field '{name}' is missing.");

      private static string RequireString(JsonElement element, string name) =>
         RequireKind(RequireProperty(element, name), JsonValueKind.String, name).GetString()!;

      private static JsonElement RequireKind(JsonElement element, JsonValueKind kind, string name) =>
         element.ValueKind == kind
            ? element
            : throw new ReleaseFormatException($"'{name}' must be a JSON {kind.ToString().ToLowerInvariant()}.");
   }
}
