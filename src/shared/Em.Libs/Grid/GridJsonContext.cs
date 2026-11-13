using System.Text.Json.Serialization;

namespace Em.Grid;

[JsonSourceGenerationOptions(WriteIndented = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(GridLayout))]
[JsonSerializable(typeof(string))]
internal sealed partial class GridJsonContext : JsonSerializerContext;
