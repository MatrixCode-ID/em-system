using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Em.Shared
{
   /// <summary>
   /// One parameter of a POST request to the <c>EmApp</c> dispatcher. The POST request body is a JSON
   /// array of these objects, one entry per parameter of the target method, matched by
   /// <see cref="ParameterOrdinal"/> (parameter position, not name).
   /// </summary>
   public class PostMethodPayload
   {
      /// <summary>
      /// Empty constructor, used when deserializing the request body.
      /// </summary>
      public PostMethodPayload() { }

      /// <summary>
      /// Creates a <see cref="PostMethodPayload"/> from a value, used on the client side to build the POST
      /// request body before sending it to the server.
      /// </summary>
      /// <typeparam name="T">Parameter value type.</typeparam>
      /// <param name="ordinal">Position of the parameter in the target method (starting at 0).</param>
      /// <param name="value">Parameter value. Must not be <c>null</c>.</param>
      /// <returns>A <see cref="PostMethodPayload"/> ready to be serialized to JSON.</returns>
      /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <c>null</c>.</exception>
      public static PostMethodPayload Build<T>(int ordinal, T value) {
         ArgumentNullException.ThrowIfNull(value);
         return new PostMethodPayload {
            ParameterType = typeof(T).FullName!,
            ParameterOrdinal = ordinal,
            ValueData = JsonSerializer.SerializeToNode(value, value.GetType()) ?? JsonValue.Create((string?)null)!
         };
      }

      /// <summary>
      /// Non-generic version of <see cref="Build{T}"/>, for callers whose argument is already typed as
      /// <see cref="object"/> - e.g. an element of a <c>params object[]</c>. If such a case used the generic
      /// version, <c>T</c> would always be inferred as <see cref="object"/> and <see cref="ParameterType"/>
      /// would be <c>System.Object</c>, which never matches the parameter type of the target method on the
      /// server. Here the type name is taken from the actual runtime type of <paramref name="value"/>, so it
      /// always matches the content of <see cref="ValueData"/>.
      /// </summary>
      /// <param name="ordinal">Position of the parameter in the target method (starting at 0).</param>
      /// <param name="value">Parameter value. Must not be <c>null</c>.</param>
      /// <returns>A <see cref="PostMethodPayload"/> ready to be serialized to JSON.</returns>
      /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <c>null</c>.</exception>
      public static PostMethodPayload Build(int ordinal, object value) {
         ArgumentNullException.ThrowIfNull(value);
         var valueType = value.GetType();
         return new PostMethodPayload {
            ParameterType = valueType.FullName!,
            ParameterOrdinal = ordinal,
            ValueData = JsonSerializer.SerializeToNode(value, valueType) ?? JsonValue.Create((string?)null)!
         };
      }

      /// <summary>
      /// Full type name of the parameter value, used to resolve the type when deserializing through
      /// <see cref="ConstructObject{T}"/>.
      /// </summary>
      public string ParameterType { get; init; } = "";

      /// <summary>
      /// Position of the parameter in the target method (starting at 0), used to match arguments by
      /// position rather than by parameter name.
      /// </summary>
      public int ParameterOrdinal { get; init; }

      /// <summary>
      /// Parameter value as a raw JSON node, deserialized to the target type when
      /// <see cref="ConstructObject{T}"/> is called.
      /// </summary>
      public JsonNode ValueData { get; init; } = null!;

      /// <summary>
      /// Tries to resolve <see cref="ParameterType"/> into the actual <see cref="Type"/>, first through
      /// <see cref="Type.GetType(string, bool, bool)"/>, then by searching every assembly loaded in
      /// <see cref="AppDomain.CurrentDomain"/>.
      /// </summary>
      /// <returns>The type found, or <c>null</c> when it is not found or <see cref="ParameterType"/> is empty.</returns>
      private Type? GetResultType() {
         if (string.IsNullOrWhiteSpace(ParameterType)) {
            return null;
         }

         var resolvedType = Type.GetType(ParameterType, throwOnError: false, ignoreCase: true);
         if (resolvedType is not null) {
            return resolvedType;
         }

         foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            resolvedType = assembly.GetType(ParameterType, throwOnError: false, ignoreCase: true);
            if (resolvedType is not null) {
               return resolvedType;
            }
         }

         return null;
      }

      /// <summary>
      /// Deserializes <see cref="ValueData"/> into a value of type <typeparamref name="T"/>, after checking
      /// that <see cref="ParameterType"/> is compatible with <typeparamref name="T"/>.
      /// </summary>
      /// <typeparam name="T">Expected type of the result.</typeparam>
      /// <param name="resultValue">The deserialized value on success; otherwise the default value.</param>
      /// <param name="errorMessage">Error message on failure; an empty string on success.</param>
      /// <returns><c>true</c> when deserialization succeeded; <c>false</c> when it failed (see <paramref name="errorMessage"/>).</returns>
      /// <remarks>
      /// Types are compared after removing <c>Nullable&lt;&gt;</c> from both sides. This is needed because a
      /// value boxed to <see cref="object"/> loses its nullability - <c>Build</c> only sees the underlying
      /// type (e.g. <c>ContactSearchType</c>), while the target method parameter may be declared as
      /// <c>ContactSearchType?</c>. Without this every nullable value type parameter would always be
      /// rejected.
      /// <para>
      /// Derived types are accepted too, not only the exact type: senders often hold a richer model than the
      /// target method asks for - e.g. a view row inheriting its table row - and <c>Build</c> records the
      /// value's actual type, not the target parameter type. The content is still read as
      /// <typeparamref name="T"/>, so extra columns of the derived type are ignored and the target method
      /// receives exactly the type it declares. The opposite direction is still rejected: a base type does
      /// not promise everything its derived type asks for.
      /// </para>
      /// </remarks>
      public bool ConstructObject<T>(out T resultValue, out string errorMessage) {
         resultValue = default!;
         errorMessage = "";
         var targetType = GetResultType();

         if (targetType is null) {
            errorMessage = "Type not found";
            return false;
         }

         var payloadType = Nullable.GetUnderlyingType(targetType) ?? targetType;
         var expectedType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

         // IsAssignableFrom rather than equality: a caller holding a subclass of what the target method
         // asks for is sending more than enough, and the deserialization below reads it back as the
         // declared type anyway. Array covariance comes along with it, which is what lets a batch of
         // rows go through the same way a single one does.
         if (!expectedType.IsAssignableFrom(payloadType)) {
            errorMessage = "Target type not supported";
            return false;
         }

         try {
            // Deliberately deserialized to typeof(T), not to the resolved type: for nullable parameters the
            // result must be Nullable<T> so it matches when assigned back to the method argument.
            var deserializedValue = JsonSerializer.Deserialize(ValueData.ToJsonString(), typeof(T));

            if (deserializedValue is T typedValue) {
               resultValue = typedValue;
               return true;
            }

            if (deserializedValue is null && default(T) is null) {
               resultValue = default!;
               return true;
            }

            errorMessage = "Invalid type";
            return false;
         }
         catch (Exception x) {
            errorMessage = x.Message;
            return false;
         }
      }
   }
}
