using System.Text.Json;
using System.Text.Json.Nodes;
using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>Catatan penarikan kembali sebuah request: siapa, kapan, dan kenapa.</summary>
   /// <param name="UserId">User yang menarik kembali.</param>
   /// <param name="Date">Kapan ditarik.</param>
   /// <param name="Reason">Alasan penarikan.</param>
   internal record ApprovalCancelRecord(string UserId, DateTime Date, string Reason);

   /// <summary>
   /// Isi kolom data tambahan sebuah request: kolom ringkasan milik modul, ditambah catatan milik engine
   /// sendiri.
   /// </summary>
   /// <remarks>
   /// Satu objek JSON datar. Kolom ringkasan modul ditulis apa adanya sebagai pasangan nama dan nilai,
   /// sehingga daftar request bisa menyaring dan mengurutkannya langsung di database. Catatan milik
   /// engine memakai nama berawalan <see cref="ReservedPrefix"/> - awalan yang tidak boleh dipakai nama
   /// kolom ringkasan - supaya keduanya tidak pernah bertabrakan dan layar tidak ikut menampilkan
   /// catatan engine sebagai kolom.
   /// </remarks>
   internal static class ApprovalRequestJson
   {
      /// <summary>Awalan nama yang dicadangkan untuk catatan engine.</summary>
      public const string ReservedPrefix = "$";

      /// <summary>Nama catatan penarikan kembali.</summary>
      public const string CancelKey = "$cancel";

      // Names of the standard columns of a request in the list. A summary column with one of these names
      // could not be told apart from it when the list is sorted, so the declaration is refused.
      private static readonly HashSet<string> StandardColumns = new(
         typeof(ApprovalRequestInfo).GetProperties().Select(r => r.Name), StringComparer.OrdinalIgnoreCase);

      /// <summary>
      /// Kolom ringkasan modul saja, sebagai teks JSON. Catatan engine dibuang, karena yang tampil di
      /// layar hanyalah apa yang modul nyatakan sendiri.
      /// </summary>
      /// <param name="json">Isi kolom data tambahan request.</param>
      /// <returns>Objek JSON ringkasannya, atau <c>null</c> kalau modul tidak menyatakan satu pun.</returns>
      public static string? ReadSummary(string? json) {
         if (Parse(json) is not { } node) return null;

         var summary = new JsonObject();
         foreach (var (name, value) in node) {
            if (name.StartsWith(ReservedPrefix, StringComparison.Ordinal)) continue;
            summary[name] = value?.DeepClone();
         }

         return summary.Count == 0 ? null : summary.ToJsonString();
      }

      /// <summary>Catatan penarikan kembali, atau <c>null</c> kalau request ini tidak pernah ditarik.</summary>
      /// <param name="json">Isi kolom data tambahan request.</param>
      public static ApprovalCancelRecord? ReadCancel(string? json) {
         if (Parse(json)?[CancelKey] is not JsonObject cancel) return null;

         var userId = cancel["userId"]?.GetValue<string>();
         var reason = cancel["reason"]?.GetValue<string>();
         if (userId is null || reason is null || cancel["date"] is not { } date) return null;

         return new ApprovalCancelRecord(userId, date.GetValue<DateTime>(), reason);
      }

      /// <summary>
      /// Menyusun isi kolom data tambahan dari ringkasan modul dan catatan engine.
      /// </summary>
      /// <param name="summary">Kolom ringkasan milik modul, atau <c>null</c> kalau tidak ada.</param>
      /// <param name="cancel">Catatan penarikan kembali, atau <c>null</c>.</param>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau nama kolom ringkasan kosong, berawalan <see cref="ReservedPrefix"/>, atau sama dengan nama kolom standar daftar request.
      /// </exception>
      public static string? Compose(IReadOnlyDictionary<string, string?>? summary, ApprovalCancelRecord? cancel) {
         var node = new JsonObject();

         foreach (var (name, value) in summary ?? new Dictionary<string, string?>()) {
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith(ReservedPrefix, StringComparison.Ordinal)) {
               throw new InvalidOperationException(
                  $"Summary column '{name}' is not allowed: a name must not be empty or start with '{ReservedPrefix}'.");
            }

            if (StandardColumns.Contains(name)) {
               throw new InvalidOperationException(
                  $"Summary column '{name}' is not allowed: it is the name of a standard column of the request list.");
            }

            node[name] = value;
         }

         if (cancel is not null) {
            node[CancelKey] = CancelNode(cancel);
         }

         return node.Count == 0 ? null : node.ToJsonString();
      }

      /// <summary>Menambahkan atau mengganti catatan penarikan pada isi kolom yang sudah ada.</summary>
      /// <param name="json">Isi kolom data tambahan request sebelumnya.</param>
      /// <param name="cancel">Catatan penarikan yang ditulis.</param>
      public static string WithCancel(string? json, ApprovalCancelRecord cancel) {
         var node = Parse(json) ?? new JsonObject();
         node[CancelKey] = CancelNode(cancel);
         return node.ToJsonString();
      }

      private static JsonObject CancelNode(ApprovalCancelRecord cancel) => new() {
         ["userId"] = cancel.UserId,
         ["date"] = cancel.Date,
         ["reason"] = cancel.Reason
      };

      private static JsonObject? Parse(string? json) {
         if (string.IsNullOrWhiteSpace(json)) return null;

         try {
            return JsonNode.Parse(json) as JsonObject;
         }
         catch (JsonException) {
            // A malformed column must not take the whole list down; the row just has no summary.
            return null;
         }
      }
   }

   /// <summary>
   /// Salinan posisi kotak sebuah langkah, dibekukan saat request diajukan, beserta nilai isian yang
   /// diberikan penanda tangan. Ini isi kolom data tambahan sebuah langkah.
   /// </summary>
   internal class ApprovalStepSnapshot
   {
      private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

      /// <summary>Posisi kotak tanda tangan, atau <c>null</c> kalau langkah ini tidak punya kotak.</summary>
      public ApprovalSlotSnapshot? Slot { get; set; }

      /// <summary>Kotak isian langkah ini, berurutan seperti yang dideklarasikan modul.</summary>
      public List<ApprovalFieldSnapshot> Fields { get; set; } = [];

      /// <summary>Membaca salinan dari isi kolomnya. Kolom kosong atau rusak berarti tanpa kotak apa pun.</summary>
      /// <param name="json">Isi kolom data tambahan langkah.</param>
      public static ApprovalStepSnapshot Read(string? json) {
         if (string.IsNullOrWhiteSpace(json)) return new ApprovalStepSnapshot();

         try {
            return JsonSerializer.Deserialize<ApprovalStepSnapshot>(json, Options) ?? new ApprovalStepSnapshot();
         }
         catch (JsonException) {
            return new ApprovalStepSnapshot();
         }
      }

      /// <summary>Menulis salinan ke bentuk yang disimpan di kolomnya.</summary>
      public string Write() => JsonSerializer.Serialize(this, Options);
   }

   /// <summary>Posisi sebuah kotak, dalam milimeter dari sudut kiri atas halaman.</summary>
   internal record ApprovalSlotSnapshot(double X, double Y, double Width, double Height, int Page)
   {
      /// <summary>Membekukan posisi dari deklarasi modul.</summary>
      /// <param name="slot">Posisi menurut deklarasi.</param>
      public static ApprovalSlotSnapshot From(ApprovalSlot slot) =>
         new(slot.X, slot.Y, slot.Width, slot.Height, slot.Page);

      /// <summary>Kembali ke bentuk posisi yang dipahami penggambar PDF.</summary>
      public ApprovalSlot ToSlot() => new(X, Y, Width, Height, Page);
   }

   /// <summary>Satu kotak isian: jenis dan posisinya dibekukan saat pengajuan, nilainya terisi saat diputuskan.</summary>
   internal class ApprovalFieldSnapshot
   {
      /// <summary>Jenis kotaknya.</summary>
      public ApprovalInputFieldKind Kind { get; set; }

      /// <summary>Posisi kotaknya.</summary>
      public ApprovalSlotSnapshot Slot { get; set; } = new(0, 0, 0, 0, 1);

      /// <summary>Teks yang digambar, untuk kotak teks. Kosong sampai langkahnya diputuskan.</summary>
      public string? Text { get; set; }

      /// <summary>Apakah kotaknya tercentang, untuk kotak centang.</summary>
      public bool Checked { get; set; }
   }
}
