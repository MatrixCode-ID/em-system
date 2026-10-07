using System.Buffers.Text;
using System.Text.Json;

namespace Em.Shared
{
   /// <summary>
   /// Rules for building the content of header <see cref="Defaults.StreamPayloadHeader"/> - the payload
   /// accompanying a streaming action. Used by both sides: the client that builds it and the server that
   /// reads it must use exactly the same shape.
   /// </summary>
   /// <remarks>
   /// The shape is UTF-8 JSON of one object, then Base64Url-encoded so it is safe to send as any header
   /// value whatever its content (including non-ASCII characters and newlines). Serialization uses
   /// <see cref="Defaults.ResponseJsonOptions"/>, so JSON property names are exactly the C# property names.
   /// <para>
   /// The target type when reading never comes from the client: the server takes it from the parameter of
   /// the action that will receive it, so a caller cannot make the server create objects of an arbitrary
   /// type.
   /// </para>
   /// </remarks>
   public static class StreamPayloadProtocol
   {
      /// <summary>
      /// Maximum header length, counted after encoding. The header is meant for small information
      /// accompanying the stream (file name, target folder, overwrite choice), not for data - large data
      /// belongs in the stream itself. This limit is enforced on both sides: the client refuses before
      /// sending, the server answers 400.
      public const int MaxHeaderLength = 4096;

      /// <summary>
      /// Builds the header content from a payload object.
      /// </summary>
      /// <param name="payload">Object to send; serialized as its actual type.</param>
      /// <returns>Header content ready to send.</returns>
      /// <exception cref="InvalidOperationException">
      /// The result is longer than <see cref="MaxHeaderLength"/> - the payload is too large for a header
      /// and must be made smaller.
      /// </exception>
      public static string Encode(object payload) {
         ArgumentNullException.ThrowIfNull(payload);

         var json = JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType(), Defaults.ResponseJsonOptions);
         var encoded = Base64Url.EncodeToString(json);
         if (encoded.Length > MaxHeaderLength) {
            throw new InvalidOperationException(
               $"The stream payload is {encoded.Length:N0} characters once encoded; the limit is {MaxHeaderLength:N0}.");
         }

         return encoded;
      }

      /// <summary>
      /// Reads the header content back into an object of type <paramref name="type"/>.
      /// </summary>
      /// <param name="header">Header content as is.</param>
      /// <param name="type">Target type, taken from the parameter of the receiving action.</param>
      /// <param name="value">The resulting object, or <c>null</c> on failure (or when the JSON itself is <c>null</c>).</param>
      /// <param name="error">Reason for failure suitable to send back to the caller, or <c>null</c> on success.</param>
      /// <returns><c>true</c> when the header was read into the requested type.</returns>
      public static bool TryDecode(string header, Type type, out object? value, out string? error) {
         value = null;

         if (header.Length > MaxHeaderLength) {
            error = $"The {Defaults.StreamPayloadHeader} header is longer than {MaxHeaderLength:N0} characters.";
            return false;
         }

         byte[] json;
         try {
            json = Base64Url.DecodeFromChars(header.Trim());
         }
         catch (FormatException) {
            error = $"The {Defaults.StreamPayloadHeader} header is not valid Base64Url.";
            return false;
         }

         try {
            value = JsonSerializer.Deserialize(json, type, Defaults.ResponseJsonOptions);
         }
         catch (JsonException ex) {
            error = $"The {Defaults.StreamPayloadHeader} header does not hold a valid {type.Name}: {ex.Message}";
            return false;
         }

         error = null;
         return true;
      }
   }
}
