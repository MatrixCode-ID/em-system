using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.StaticFiles;

namespace Em.Api.Core;

/// <summary>An embedded file validated and read when the skin is initialized.</summary>
public sealed class EmbeddedFile {
   private readonly byte[] _bytes;
   private EmbeddedFile(Assembly assembly, string logicalName) {
      using var stream = assembly.GetManifestResourceStream(logicalName)
         ?? throw new InvalidOperationException($"Embedded skin resource '{logicalName}' was not found in '{assembly.GetName().Name}'.");
      using var buffer = new MemoryStream();
      stream.CopyTo(buffer);
      _bytes = buffer.ToArray();
      ContentType = new FileExtensionContentTypeProvider().TryGetContentType(logicalName, out var type)
         ? type : "application/octet-stream";
   }
   /// <summary>Opens a resource in the assembly containing the given type.</summary>
   public static EmbeddedFile From<T>(string logicalName) => new(typeof(T).Assembly, logicalName);
   internal static EmbeddedFile From(Assembly assembly, string logicalName) => new(assembly, logicalName);
   /// <summary>Returns the resource decoded as UTF-8 text.</summary>
   public string ReadText() => Encoding.UTF8.GetString(_bytes);
   /// <summary>Returns a copy of the embedded bytes.</summary>
   public byte[] ReadBytes() => (byte[])_bytes.Clone();
   /// <summary>MIME type inferred from the resource extension.</summary>
   public string ContentType { get; }
}

/// <summary>Embedded resources to copy into the reserved public CDN skin folder.</summary>
/// <param name="Assembly">Assembly holding the resources.</param>
/// <param name="Prefix">Logical resource prefix, with slash-separated relative paths.</param>
public sealed record EmSkinAssets(Assembly Assembly, string Prefix);
