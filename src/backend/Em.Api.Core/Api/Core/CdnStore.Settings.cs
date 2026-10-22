using System.Text.Json;
using Em.Shared;

namespace Em.Api.Core;

internal sealed partial class CdnStore {
   internal const int MaximumFileSizeMb = 1_048_576;
   private string RuntimeSettingsPath => Path.Combine(StoragePath, "settings", "cdn.json");
   private sealed record RuntimeSettings(int MaxFileSizeMb);

   private void LoadRuntimeSettings() {
      if (!File.Exists(RuntimeSettingsPath)) return;
      var settings = JsonSerializer.Deserialize<RuntimeSettings>(File.ReadAllBytes(RuntimeSettingsPath), StorageJson)
         ?? throw new InvalidDataException("Invalid CDN runtime settings.");
      ValidateMaxFileSize(settings.MaxFileSizeMb);
      Interlocked.Exchange(ref _maxFileSize, settings.MaxFileSizeMb * 1024L * 1024L);
   }

   private static void ValidateMaxFileSize(int value) {
      if (value is < 1 or > MaximumFileSizeMb)
         throw new ActionException($"Maximum file size must be between 1 and {MaximumFileSizeMb:N0} MB.", 400);
   }

   internal void SetMaxFileSize(int maxFileSizeMb) {
      EnsureEnabled();
      ValidateMaxFileSize(maxFileSizeMb);
      lock (_gate) {
         var temp = Path.Combine(StoragePath, "settings", Guid.NewGuid().ToString("N") + ".tmp");
         try {
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(new RuntimeSettings(maxFileSizeMb), StorageJson));
            File.Move(temp, RuntimeSettingsPath, true);
            Interlocked.Exchange(ref _maxFileSize, maxFileSizeMb * 1024L * 1024L);
         }
         finally { if (File.Exists(temp)) File.Delete(temp); }
      }
   }
}
