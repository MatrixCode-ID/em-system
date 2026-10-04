using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// Menulis dan membaca <c>release.json</c> serta <c>release.json.sig</c> sesuai
   /// <c>doc/release-format.md</c>. Penulisan selalu menghasilkan bentuk yang sama untuk isi yang sama
   /// (file diurutkan, UTF-8 tanpa BOM, akhir baris <c>\n</c>); pembacaan memeriksa setiap aturan format
   /// dan menolak manifest yang melanggar satu saja.
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
      /// Menulis manifest menjadi byte <c>release.json</c>. File diurutkan menurut path secara ordinal,
      /// dan waktu terbit ditulis dengan presisi detik. Path dan hash diperiksa dulu, jadi manifest yang
      /// tidak sah tidak pernah tertulis.
      /// </summary>
      /// <exception cref="ReleaseFormatException">Ada path atau hash yang melanggar aturan format.</exception>
      public static byte[] Serialize(ReleaseManifest manifest) {
         var files = manifest.Files.OrderBy(r => r.Path, StringComparer.Ordinal).ToArray();
         ValidateFiles(files);

         using var buffer = new MemoryStream();
         using (var writer = new Utf8JsonWriter(buffer, WriterOptions)) {
            writer.WriteStartObject();
            writer.WriteString("publishedAtUtc", ToUtc(manifest.PublishedAtUtc)
               .ToString(TimeFormat, CultureInfo.InvariantCulture));
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
      /// Membaca byte <c>release.json</c>. Field yang tidak dikenal diabaikan; setiap aturan path,
      /// ukuran, dan hash diperiksa.
      /// </summary>
      /// <remarks>
      /// Pembaca yang menerima manifest dari luar harus memverifikasi tanda tangannya lebih dulu
      /// (<see cref="ReleaseSignature.Verify"/>), baru memanggil method ini.
      /// </remarks>
      /// <exception cref="ReleaseFormatException">Manifest tidak sah.</exception>
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
            return new ReleaseManifest { PublishedAtUtc = publishedAt, Files = files };
         }
         catch (JsonException x) {
            throw new ReleaseFormatException($"release.json is not valid JSON: {x.Message}", x);
         }
      }

      /// <summary>Menulis isi <c>release.json.sig</c> (UTF-8 tanpa BOM, akhir baris <c>\n</c>).</summary>
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

      /// <summary>Membaca isi <c>release.json.sig</c>. Field yang tidak dikenal diabaikan.</summary>
      /// <exception cref="ReleaseFormatException">JSON-nya rusak atau field wajibnya tidak ada.</exception>
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
      /// Alasan <paramref name="path"/> tidak sah sebagai path file rilis (<c>doc/release-format.md</c>
      /// bagian 2.3), atau <c>null</c> kalau sah. Keunikan tidak ikut diperiksa di sini.
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

      /// <summary><c>true</c> kalau <paramref name="hash"/> berupa 64 karakter hex huruf kecil.</summary>
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
