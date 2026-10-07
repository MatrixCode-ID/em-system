using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Translator of document keys: from the module's typed value to a single string that can be indexed
   /// and matched, and back again.
   /// </summary>
   /// <remarks>
   /// A document key often consists of several parts. The module declares it as a typed record whose
   /// parts are each marked with <see cref="KeyPartAttribute"/>, then receives that record back in its
   /// handler; no module code composes or splits the key string itself.
   /// <para>
   /// The canonical form is a list of values in the declared order, written as JSON text. The format of
   /// each value kind is fixed here - date-times in particular - so the same value always produces the
   /// same key, today and on another machine.
   /// </para>
   /// </remarks>
   public static class ApprovalKey
   {
      // The ordered parts of a key type, worked out once per type: every submit, every decision and
      // every hub query translates keys, and the attribute scan is the expensive half of that.
      private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PartCache = new();

      /// <summary>Canonical format of a date-time value inside a key.</summary>
      public const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fff";

      /// <summary>Canonical format of a date value inside a key.</summary>
      public const string DateFormat = "yyyy-MM-dd";

      /// <summary>Canonical format of a time value inside a key.</summary>
      public const string TimeFormat = "HH:mm:ss.fff";

      /// <summary>
      /// Converts a typed key into its canonical form.
      /// </summary>
      /// <typeparam name="TKey">Type of the document key.</typeparam>
      /// <param name="key">The key being translated.</param>
      /// <returns>The key in its canonical form.</returns>
      /// <exception cref="InvalidOperationException">
      /// Thrown when <typeparamref name="TKey"/> has no key part marked with
      /// <see cref="KeyPartAttribute"/> at all, or when two of its parts use the same order.
      /// </exception>
      public static string ToCanonical<TKey>(TKey key) where TKey : notnull {
         ArgumentNullException.ThrowIfNull(key);
         var parts = GetParts(key.GetType());
         var values = new string?[parts.Length];

         for (var i = 0; i < parts.Length; i++) {
            values[i] = FormatValue(parts[i].GetValue(key));
         }

         return JsonSerializer.Serialize(values);
      }

      /// <summary>
      /// Restores a key from its canonical form into a typed value.
      /// </summary>
      /// <typeparam name="TKey">Type of the document key.</typeparam>
      /// <param name="canonicalKey">The key in its canonical form.</param>
      /// <returns>The typed key.</returns>
      /// <exception cref="InvalidOperationException">
      /// Thrown when the canonical form cannot be read, the number of parts does not match
      /// <typeparamref name="TKey"/>, one of the values cannot be read as its part's type, or
      /// <typeparamref name="TKey"/> has no constructor that accepts its parts.
      /// </exception>
      public static TKey FromCanonical<TKey>(string canonicalKey) where TKey : notnull {
         var parts = GetParts(typeof(TKey));
         var values = ReadParts(canonicalKey);

         if (values.Length != parts.Length) {
            throw new InvalidOperationException(
               $"Key {canonicalKey} has {values.Length} part(s), but type {typeof(TKey).Name} declares {parts.Length}.");
         }

         var byPart = new object?[parts.Length];
         for (var i = 0; i < parts.Length; i++) {
            byPart[i] = ParseValue(values[i], parts[i].PropertyType, parts[i].Name, canonicalKey);
         }

         // A key type is written as a positional record, so the constructor taking one argument per
         // part is the way back in - and it is also what keeps a key whose parts are init-only
         // buildable at all.
         var constructor = typeof(TKey).GetConstructors()
            .FirstOrDefault(r => r.GetParameters().Length == parts.Length &&
                                 r.GetParameters().All(p => parts.Any(q =>
                                    string.Equals(q.Name, p.Name, StringComparison.OrdinalIgnoreCase))));

         if (constructor is null) {
            throw new InvalidOperationException(
               $"Key type {typeof(TKey).Name} has no constructor taking its {parts.Length} key part(s). " +
               "Declare it as a record whose positional parameters are the key parts.");
         }

         // The constructor may order its parameters differently from the declared part order.
         var constructorParameters = constructor.GetParameters();
         var arguments = new object?[constructorParameters.Length];
         for (var i = 0; i < constructorParameters.Length; i++) {
            var index = Array.FindIndex(parts, r =>
               string.Equals(r.Name, constructorParameters[i].Name, StringComparison.OrdinalIgnoreCase));
            arguments[i] = byPart[index];
         }

         return (TKey)constructor.Invoke(arguments);
      }

      /// <summary>
      /// Restores a key from its canonical form into a typed value, when the type is only known while the
      /// program runs.
      /// </summary>
      /// <param name="keyType">Type of the document key.</param>
      /// <param name="canonicalKey">The key in its canonical form.</param>
      /// <exception cref="InvalidOperationException">
      /// Thrown for the same causes as <see cref="FromCanonical{TKey}(string)"/>.
      /// </exception>
      public static object FromCanonical(Type keyType, string canonicalKey) {
         ArgumentNullException.ThrowIfNull(keyType);

         try {
            return typeof(ApprovalKey).GetMethod(nameof(FromCanonical), 1, [typeof(string)])!
               .MakeGenericMethod(keyType)
               .Invoke(null, [canonicalKey])!;
         }
         catch (TargetInvocationException ex) when (ex.InnerException is not null) {
            // The reflection wrapper says nothing the caller can use; what went wrong is inside it.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
         }
      }

      /// <summary>
      /// Composes the canonical form of a key from the values of its parts, whose order must be the same as
      /// the order of the parts declared by its key type.
      /// </summary>
      /// <param name="parts">Value of each key part in its canonical text form.</param>
      public static string FromParts(IReadOnlyList<string?> parts) {
         ArgumentNullException.ThrowIfNull(parts);
         return JsonSerializer.Serialize(parts.ToArray());
      }

      /// <summary>
      /// The value of each key part together with its name and order, used when the parts are stored one
      /// row per part.
      /// </summary>
      /// <typeparam name="TKey">Type of the document key.</typeparam>
      /// <param name="key">The key being read.</param>
      public static IReadOnlyList<ApprovalKeyPart> GetKeyParts<TKey>(TKey key) where TKey : notnull {
         ArgumentNullException.ThrowIfNull(key);
         var parts = GetParts(key.GetType());
         var result = new ApprovalKeyPart[parts.Length];

         for (var i = 0; i < parts.Length; i++) {
            result[i] = new ApprovalKeyPart(parts[i].Name, FormatValue(parts[i].GetValue(key)), i + 1);
         }

         return result;
      }

      /// <summary>
      /// Reads the canonical form of a key as a plain list of values, without types. Used when the key only
      /// needs to be displayed or matched, not handed to the module.
      /// </summary>
      /// <param name="canonicalKey">The key in its canonical form.</param>
      /// <exception cref="InvalidOperationException">Thrown when the canonical form cannot be read.</exception>
      public static string?[] ReadParts(string canonicalKey) {
         if (string.IsNullOrWhiteSpace(canonicalKey)) {
            throw new InvalidOperationException("A document key must not be empty.");
         }

         try {
            return JsonSerializer.Deserialize<string?[]>(canonicalKey) ??
                   throw new InvalidOperationException($"Key {canonicalKey} could not be read.");
         }
         catch (JsonException x) {
            throw new InvalidOperationException($"Key {canonicalKey} is not in the canonical key format.", x);
         }
      }

      /// <summary>
      /// A human-friendly form of a key, used in lists and screen titles: its parts are joined with hyphens.
      /// </summary>
      /// <param name="canonicalKey">The key in its canonical form.</param>
      public static string ToDisplay(string canonicalKey) =>
         string.Join(" - ", ReadParts(canonicalKey).Select(r => r ?? string.Empty));

      /// <summary>
      /// Names of the key parts of a type, in order. Used by the engine when checking a module's declaration.
      /// </summary>
      /// <param name="keyType">Type of the document key.</param>
      /// <exception cref="InvalidOperationException">
      /// Thrown when the type has no key part at all, or when two of its parts use the same order.
      /// </exception>
      public static IReadOnlyList<string> GetPartNames(Type keyType) =>
         GetParts(keyType).Select(r => r.Name).ToArray();

      private static PropertyInfo[] GetParts(Type keyType) =>
         PartCache.GetOrAdd(keyType, static type => {
            var parts = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
               .Select(r => (Property: r, Part: r.GetCustomAttribute<KeyPartAttribute>()))
               .Where(r => r.Part is not null)
               .OrderBy(r => r.Part!.Order)
               .ToArray();

            if (parts.Length == 0) {
               throw new InvalidOperationException(
                  $"Key type {type.Name} has no property marked with [{nameof(KeyPartAttribute)}]. " +
                  "A document key must declare its parts, because their order is what the stored key means.");
            }

            var duplicate = parts.GroupBy(r => r.Part!.Order).FirstOrDefault(r => r.Count() > 1);
            if (duplicate is not null) {
               throw new InvalidOperationException(
                  $"Key type {type.Name} uses order {duplicate.Key} for more than one key part " +
                  $"({string.Join(", ", duplicate.Select(r => r.Property.Name))}). Each part needs an order of its own.");
            }

            return parts.Select(r => r.Property).ToArray();
         });

      // Every value becomes text here, and the format of each kind is fixed, so that the same key
      // always produces the same string - a key that formatted itself by the current culture would
      // match nothing on a machine set to another one.
      private static string? FormatValue(object? value) => value switch {
         null => null,
         string s => s,
         DateTime d => d.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
         DateOnly d => d.ToString(DateFormat, CultureInfo.InvariantCulture),
         TimeOnly t => t.ToString(TimeFormat, CultureInfo.InvariantCulture),
         DateTimeOffset d => d.UtcDateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
         bool b => b ? "true" : "false",
         Guid g => g.ToString("D", CultureInfo.InvariantCulture),
         Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
         IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
         _ => value.ToString()
      };

      private static object? ParseValue(string? value, Type targetType, string partName, string canonicalKey) {
         var type = Nullable.GetUnderlyingType(targetType) ?? targetType;

         if (value is null) {
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null) {
               throw new InvalidOperationException(
                  $"Key {canonicalKey} has no value for part {partName}, but that part cannot be empty.");
            }

            return null;
         }

         try {
            if (type == typeof(string)) return value;

            if (type == typeof(DateTime)) {
               return DateTime.ParseExact(value, DateTimeFormat, CultureInfo.InvariantCulture);
            }

            if (type == typeof(DateOnly)) {
               return DateOnly.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);
            }

            if (type == typeof(TimeOnly)) {
               return TimeOnly.ParseExact(value, TimeFormat, CultureInfo.InvariantCulture);
            }

            if (type == typeof(DateTimeOffset)) {
               return new DateTimeOffset(
                  DateTime.ParseExact(value, DateTimeFormat, CultureInfo.InvariantCulture), TimeSpan.Zero);
            }

            if (type == typeof(Guid)) return Guid.ParseExact(value, "D");

            if (type.IsEnum) {
               return Enum.ToObject(type, Convert.ToInt64(value, CultureInfo.InvariantCulture));
            }

            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
         }
         catch (Exception x) when (x is FormatException or InvalidCastException or OverflowException or ArgumentException) {
            throw new InvalidOperationException(
               $"Key {canonicalKey} has the value {value} for part {partName}, which cannot be read as {type.Name}.", x);
         }
      }
   }

   /// <summary>One key part together with its name and order.</summary>
   /// <param name="Name">Name of the part, as declared by the module.</param>
   /// <param name="Value">Its value in canonical text form.</param>
   /// <param name="Order">Its order within the key, starting from one.</param>
   public record ApprovalKeyPart(string Name, string? Value, int Order);
}
