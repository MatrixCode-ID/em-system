using System.Text;
using System.Text.Json;
using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>
/// Settings of one CDN folder, stored in a hidden settings file inside that folder. They apply to that
/// folder only; subfolders do not inherit them.
/// </summary>
public sealed class CdnFolderSettings {
   /// <summary>Format version; always <c>1</c>.</summary>
   public int Version { get; set; } = CdnFolderSettingsFormat.CurrentVersion;

   /// <summary>
   /// <c>false</c> makes the public listing of the folder answer 404 and marks its files with
   /// <c>X-Robots-Tag: noindex</c>. Direct links keep working.
   /// </summary>
   public bool PublicListing { get; set; } = true;

   /// <summary>
   /// Name patterns (<c>*</c> any run of characters, <c>?</c> one character) of files and subfolders directly
   /// in the folder that are left out of the public listing. Direct links keep working.
   /// </summary>
   public string[] HiddenFiles { get; set; } = [];

   /// <summary>Name patterns that stay listed even when they match <see cref="HiddenFiles"/>.</summary>
   public string[] ShownFiles { get; set; } = [];
}

/// <summary>
/// Validation, canonical serialization and pattern matching of <see cref="CdnFolderSettings"/>, shared by
/// the server and its clients so both accept exactly the same content.
/// </summary>
public static class CdnFolderSettingsFormat {
   /// <summary>Name of the settings file inside a folder.</summary>
   public const string FileName = ".meta";
   /// <summary>The only supported <see cref="CdnFolderSettings.Version"/>.</summary>
   public const int CurrentVersion = 1;
   /// <summary>Largest accepted settings file, in bytes.</summary>
   public const int MaxBytes = 64 * 1024;
   /// <summary>Most patterns per list.</summary>
   public const int MaxPatterns = 256;
   /// <summary>Longest accepted pattern, the same as the longest CDN name.</summary>
   public const int MaxPatternLength = 255;

   private static readonly StringComparison NameComparison =
      OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

   private static readonly char[] InvalidPatternChars =
      Path.GetInvalidFileNameChars().Where(c => c is not '*' and not '?').Concat(['/', '\\']).Distinct().ToArray();

   private static readonly JsonSerializerOptions CanonicalJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

   /// <summary><c>true</c> when <paramref name="name"/> is the settings file name, compared as the server's file system does.</summary>
   public static bool IsSettingsName(string? name) => string.Equals(name, FileName, NameComparison);

   /// <summary>Checks every rule of a settings object; throws <see cref="ActionException"/> (400) naming the offending key or pattern.</summary>
   public static void Validate(CdnFolderSettings settings) {
      ArgumentNullException.ThrowIfNull(settings);
      if (settings.Version != CurrentVersion)
         throw Invalid($"'version' must be {CurrentVersion}.");
      ValidatePatterns("hiddenFiles", settings.HiddenFiles);
      ValidatePatterns("shownFiles", settings.ShownFiles);
   }

   private static void ValidatePatterns(string key, string[]? patterns) {
      if (patterns is null) throw Invalid($"'{key}' must be an array of strings.");
      if (patterns.Length > MaxPatterns) throw Invalid($"'{key}' holds {patterns.Length} patterns; at most {MaxPatterns} are allowed.");
      for (var i = 0; i < patterns.Length; i++) {
         var pattern = patterns[i];
         if (string.IsNullOrWhiteSpace(pattern)) throw Invalid($"'{key}[{i}]' must not be empty.");
         if (pattern.Length > MaxPatternLength) throw Invalid($"'{key}[{i}]' is longer than {MaxPatternLength} characters.");
         if (pattern.IndexOfAny(InvalidPatternChars) >= 0)
            throw Invalid($"'{key}[{i}]' contains a path separator or a character that is not allowed in a file name.");
      }
   }

   /// <summary>
   /// Reads settings from file content: UTF-8 JSON (a byte order mark is allowed), at most
   /// <see cref="MaxBytes"/>, no unknown keys, <c>version</c> required. Throws <see cref="ActionException"/> (400).
   /// </summary>
   public static CdnFolderSettings Parse(ReadOnlySpan<byte> utf8) {
      if (utf8.Length > MaxBytes) throw Invalid($"The settings file is larger than {MaxBytes:N0} bytes.");
      if (utf8.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) utf8 = utf8[3..];
      JsonDocument document;
      try { document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 8 }); }
      catch (JsonException ex) { throw Invalid($"The settings file is not valid JSON: {ex.Message}"); }
      using (document) {
         var root = document.RootElement;
         if (root.ValueKind != JsonValueKind.Object) throw Invalid("The settings file must hold a JSON object.");
         var result = new CdnFolderSettings();
         var hasVersion = false;
         foreach (var property in root.EnumerateObject()) {
            switch (property.Name) {
               case "version":
                  if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var version))
                     throw Invalid("'version' must be a number.");
                  result.Version = version;
                  hasVersion = true;
                  break;
               case "publicListing":
                  if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                     throw Invalid("'publicListing' must be true or false.");
                  result.PublicListing = property.Value.GetBoolean();
                  break;
               case "hiddenFiles":
                  result.HiddenFiles = ReadPatterns(property);
                  break;
               case "shownFiles":
                  result.ShownFiles = ReadPatterns(property);
                  break;
               default:
                  throw Invalid($"Unknown key '{property.Name}'.");
            }
         }
         if (!hasVersion) throw Invalid("'version' is required.");
         Validate(result);
         return result;
      }
   }

   private static string[] ReadPatterns(JsonProperty property) {
      if (property.Value.ValueKind != JsonValueKind.Array) throw Invalid($"'{property.Name}' must be an array of strings.");
      var result = new List<string>();
      var index = 0;
      foreach (var item in property.Value.EnumerateArray()) {
         if (item.ValueKind != JsonValueKind.String) throw Invalid($"'{property.Name}[{index}]' must be a string.");
         result.Add(item.GetString()!);
         index++;
      }
      return result.ToArray();
   }

   /// <summary>Parses text, as written in the settings file or typed by a user.</summary>
   public static CdnFolderSettings Parse(string text) => Parse(Encoding.UTF8.GetBytes(text ?? ""));

   /// <summary>Canonical form (camelCase, indented) of valid settings; this is what the server stores and exports.</summary>
   public static string Serialize(CdnFolderSettings settings) {
      Validate(settings);
      return JsonSerializer.Serialize(new CdnFolderSettings {
         Version = settings.Version, PublicListing = settings.PublicListing,
         HiddenFiles = settings.HiddenFiles.ToArray(), ShownFiles = settings.ShownFiles.ToArray()
      }, CanonicalJson);
   }

   /// <summary>
   /// <c>true</c> when an entry named <paramref name="name"/> is left out of the public listing:
   /// it matches <see cref="CdnFolderSettings.HiddenFiles"/> and none of <see cref="CdnFolderSettings.ShownFiles"/>.
   /// </summary>
   public static bool IsHidden(CdnFolderSettings? settings, string name) =>
      settings is not null && settings.HiddenFiles.Any(p => Matches(p, name)) && !settings.ShownFiles.Any(p => Matches(p, name));

   /// <summary>Matches a whole name against a pattern with <c>*</c> and <c>?</c>.</summary>
   public static bool Matches(string pattern, string name) {
      var ignoreCase = NameComparison == StringComparison.OrdinalIgnoreCase;
      int p = 0, n = 0, star = -1, mark = 0;
      while (n < name.Length) {
         if (p < pattern.Length && (pattern[p] == '?' || CharEquals(pattern[p], name[n], ignoreCase))) { p++; n++; }
         else if (p < pattern.Length && pattern[p] == '*') { star = p++; mark = n; }
         else if (star >= 0) { p = star + 1; n = ++mark; }
         else return false;
      }
      while (p < pattern.Length && pattern[p] == '*') p++;
      return p == pattern.Length;
   }

   private static bool CharEquals(char a, char b, bool ignoreCase) =>
      a == b || (ignoreCase && char.ToUpperInvariant(a) == char.ToUpperInvariant(b));

   private static ActionException Invalid(string message) => new($"Invalid folder settings: {message}", 400);
}

/// <summary>Folder settings as shown by the CDN manager.</summary>
public sealed class CdnFolderSettingsDetail {
   /// <summary>Folder path relative to the CDN root.</summary>
   public string Path { get; set; } = "";
   /// <summary>Effective settings; defaults when the folder has none or its file is invalid.</summary>
   public CdnFolderSettings Settings { get; set; } = new();
   /// <summary><c>true</c> when the folder has a settings file.</summary>
   public bool Exists { get; set; }
   /// <summary>Why the existing settings file was ignored, or <c>null</c> when it is valid or missing.</summary>
   public string? Warning { get; set; }
   /// <summary><c>true</c> when CDN Manager cannot change the settings, such as in a managed folder.</summary>
   public bool IsReadOnly { get; set; }
}

/// <summary>Request to replace the settings of one folder.</summary>
public sealed class CdnFolderSettingsSave {
   /// <summary>Folder path relative to the CDN root; <c>null</c> or empty means the root.</summary>
   public string? Path { get; set; }
   /// <summary>New settings, replacing the previous ones as a whole.</summary>
   public CdnFolderSettings Settings { get; set; } = new();
}

/// <summary>Release Manager request to replace the settings of a folder inside its own managed folder.</summary>
public sealed class CdnReleaseFolderSettingsRequest {
   /// <summary>Owning product app_id.</summary>
   public string AppId { get; set; } = "";
   /// <summary>Folder path relative to the CDN root.</summary>
   public string Path { get; set; } = "";
   /// <summary>New settings, replacing the previous ones as a whole.</summary>
   public CdnFolderSettings Settings { get; set; } = new();
}

/// <summary>Request to copy a file or folder into another folder under the same name.</summary>
public sealed class CdnCopyRequest {
   /// <summary>Path of the file or folder to copy.</summary>
   public string Path { get; set; } = "";
   /// <summary>Destination folder; <c>null</c> or empty means the root.</summary>
   public string? TargetFolder { get; set; }
   /// <summary>
   /// <c>true</c> lets a copied file replace an existing file, which goes to the recycle bin. Folders are
   /// never overwritten.
   /// </summary>
   public bool Overwrite { get; set; }
}
