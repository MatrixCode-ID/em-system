using System.Text.Json;
using Em.Shared;

namespace Em.Api.Core;

internal sealed class FeatureServiceRegistry {
   private readonly Dictionary<string, FeatureServiceSwitch> _states = new(StringComparer.Ordinal);
   internal void Add(string feature, FeatureServiceSwitch state) => _states.Add(feature, state);
   internal FeatureServiceSwitch Get(string feature) => _states.TryGetValue(feature, out var state)
      ? state : throw new ActionException("Feature is not enabled on this server.", 404);
}

internal sealed class FeatureServiceSwitch(string name, Func<Task<bool>> load, Func<bool, Task> save) {
   private readonly SemaphoreSlim _gate = new(1, 1);
   private bool _loaded;
   private bool _enabled = true;
   internal async Task<bool> ReadAsync() {
      await _gate.WaitAsync();
      try {
         if (!_loaded) { _enabled = await load(); _loaded = true; }
         return _enabled;
      }
      finally { _gate.Release(); }
   }
   internal async Task SaveAsync(bool enabled) {
      await _gate.WaitAsync();
      try { await save(enabled); _enabled = enabled; _loaded = true; }
      finally { _gate.Release(); }
   }
   internal async Task EnsureAvailableAsync() {
      if (!await ReadAsync()) throw new ServiceDisabledException(name);
   }
   internal static FeatureServiceSwitch FileBacked(string name, string path) => new(name,
      () => Task.FromResult(!File.Exists(path) || JsonSerializer.Deserialize<bool>(File.ReadAllBytes(path))),
      enabled => {
         Directory.CreateDirectory(Path.GetDirectoryName(path)!);
         var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
         try { File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(enabled)); File.Move(temp, path, true); }
         finally { if (File.Exists(temp)) File.Delete(temp); }
         return Task.CompletedTask;
      });
}
