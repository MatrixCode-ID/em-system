using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Penerjemah kunci dokumen: dari nilai bertipe milik modul ke satu string yang bisa di-index dan
   /// dicocokkan, dan kembali lagi.
   /// </summary>
   /// <remarks>
   /// Kunci dokumen sering terdiri dari beberapa bagian. Modul mendeklarasikannya sebagai record
   /// bertipe yang setiap bagiannya ditandai <see cref="KeyPartAttribute"/>, lalu menerima record itu
   /// kembali di handler-nya; tidak ada kode modul yang menyusun atau memecah string kunci sendiri.
   /// <para>
   /// Bentuk bakunya adalah daftar nilai dalam urutan yang dideklarasikan, ditulis sebagai teks JSON.
   /// Format tiap jenis nilai dibakukan di sini - khususnya tanggal-jam - supaya nilai yang sama selalu
   /// menghasilkan kunci yang sama, hari ini maupun di mesin lain.
   /// </para>
   /// </remarks>
   public static class ApprovalKey
   {
      // The ordered parts of a key type, worked out once per type: every submit, every decision and
      // every hub query translates keys, and the attribute scan is the expensive half of that.
      private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PartCache = new();

      /// <summary>Format baku nilai tanggal-jam di dalam kunci.</summary>
      public const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fff";

      /// <summary>Format baku nilai tanggal di dalam kunci.</summary>
      public const string DateFormat = "yyyy-MM-dd";

      /// <summary>Format baku nilai jam di dalam kunci.</summary>
      public const string TimeFormat = "HH:mm:ss.fff";

      /// <summary>
      /// Mengubah kunci bertipe menjadi bentuk bakunya.
      /// </summary>
      /// <typeparam name="TKey">Tipe kunci dokumennya.</typeparam>
      /// <param name="key">Kunci yang diterjemahkan.</param>
      /// <returns>Kunci dalam bentuk bakunya.</returns>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau <typeparamref name="TKey"/> tidak punya satu pun bagian kunci bertanda
      /// <see cref="KeyPartAttribute"/>, atau kalau dua bagiannya memakai urutan yang sama.
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
      /// Mengembalikan kunci dari bentuk bakunya menjadi nilai bertipe.
      /// </summary>
      /// <typeparam name="TKey">Tipe kunci dokumennya.</typeparam>
      /// <param name="canonicalKey">Kunci dalam bentuk bakunya.</param>
      /// <returns>Kunci bertipe.</returns>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau bentuk bakunya tidak terbaca, jumlah bagiannya tidak sesuai
      /// <typeparamref name="TKey"/>, salah satu nilainya tidak bisa dibaca sebagai tipe bagiannya,
      /// atau <typeparamref name="TKey"/> tidak punya constructor yang menerima bagian-bagiannya.
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
      /// Mengembalikan kunci dari bentuk bakunya menjadi nilai bertipe, ketika tipenya baru diketahui saat
      /// program berjalan.
      /// </summary>
      /// <param name="keyType">Tipe kunci dokumennya.</param>
      /// <param name="canonicalKey">Kunci dalam bentuk bakunya.</param>
      /// <exception cref="InvalidOperationException">
      /// Dilempar dengan sebab yang sama seperti <see cref="FromCanonical{TKey}(string)"/>.
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
      /// Menyusun bentuk baku sebuah kunci dari nilai bagian-bagiannya, yang urutannya harus sama dengan
      /// urutan bagian yang dideklarasikan tipe kuncinya.
      /// </summary>
      /// <param name="parts">Nilai tiap bagian kunci dalam bentuk teks bakunya.</param>
      public static string FromParts(IReadOnlyList<string?> parts) {
         ArgumentNullException.ThrowIfNull(parts);
         return JsonSerializer.Serialize(parts.ToArray());
      }

      /// <summary>
      /// Nilai tiap bagian kunci beserta namanya dan urutannya, dipakai saat bagian-bagian itu disimpan
      /// satu baris per bagian.
      /// </summary>
      /// <typeparam name="TKey">Tipe kunci dokumennya.</typeparam>
      /// <param name="key">Kunci yang dibaca.</param>
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
      /// Membaca bentuk baku sebuah kunci sebagai daftar nilai apa adanya, tanpa tipe. Dipakai saat
      /// kuncinya hanya perlu ditampilkan atau dicocokkan, bukan diserahkan ke modul.
      /// </summary>
      /// <param name="canonicalKey">Kunci dalam bentuk bakunya.</param>
      /// <exception cref="InvalidOperationException">Dilempar kalau bentuk bakunya tidak terbaca.</exception>
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
      /// Bentuk kunci yang enak dibaca manusia, dipakai di daftar dan di judul layar: bagian-bagiannya
      /// disambung dengan tanda hubung.
      /// </summary>
      /// <param name="canonicalKey">Kunci dalam bentuk bakunya.</param>
      public static string ToDisplay(string canonicalKey) =>
         string.Join(" - ", ReadParts(canonicalKey).Select(r => r ?? string.Empty));

      /// <summary>
      /// Nama bagian kunci sebuah tipe, berurutan. Dipakai engine saat memeriksa deklarasi modul.
      /// </summary>
      /// <param name="keyType">Tipe kunci dokumennya.</param>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau tipenya tidak punya satu pun bagian kunci, atau dua bagiannya memakai urutan
      /// yang sama.
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

   /// <summary>Satu bagian kunci beserta namanya dan urutannya.</summary>
   /// <param name="Name">Nama bagiannya, seperti yang dideklarasikan modul.</param>
   /// <param name="Value">Nilainya dalam bentuk teks baku.</param>
   /// <param name="Order">Urutannya di dalam kuncinya, dimulai dari satu.</param>
   public record ApprovalKeyPart(string Name, string? Value, int Order);
}
