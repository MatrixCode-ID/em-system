using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Posisi sebuah kotak pada PDF dokumen, dalam milimeter dari sudut kiri atas halaman.
   /// </summary>
   /// <param name="X">Jarak dari tepi kiri.</param>
   /// <param name="Y">Jarak dari tepi atas.</param>
   /// <param name="Width">Lebar kotaknya.</param>
   /// <param name="Height">Tinggi kotaknya.</param>
   /// <param name="Page">Halaman tempat kotak ini berada, dimulai dari satu.</param>
   /// <remarks>
   /// Posisinya tetap dan ditulis developer modul pemilik dokumen, karena dialah yang tahu rancangan
   /// dokumennya. Ukurannya dibekukan ke dalam request saat pengajuan, sehingga perubahan rancangan hanya
   /// berlaku untuk request yang diajukan sesudahnya. Untuk mengukurnya, pakai action kalibrasi yang
   /// menggambar kotak-kotak ini di atas dokumen yang sebenarnya.
   /// </remarks>
   public record ApprovalSlot(double X, double Y, double Width = 60, double Height = 18, int Page = 1)
   {
      /// <summary>
      /// Membuat posisi kotak. Sama dengan constructor-nya, ditulis sebagai method supaya deklarasi alur
      /// yang penuh kotak tetap terbaca sebagai daftar posisi, bukan daftar <c>new</c>.
      /// </summary>
      /// <param name="x">Jarak dari tepi kiri.</param>
      /// <param name="y">Jarak dari tepi atas.</param>
      /// <param name="width">Lebar kotaknya.</param>
      /// <param name="height">Tinggi kotaknya.</param>
      /// <param name="page">Halaman tempat kotak ini berada, dimulai dari satu.</param>
      public static ApprovalSlot At(double x, double y, double width = 60, double height = 18, int page = 1) =>
         new(x, y, width, height, page);
   }

   /// <summary>
   /// Menandai sebuah properti record kunci sebagai bagian kunci dokumen, beserta urutannya.
   /// </summary>
   /// <param name="order">
   /// Urutan bagian ini di dalam kuncinya. Urutan itu yang menentukan bentuk kanonik kuncinya, jadi
   /// mengubahnya setelah ada request berarti mengubah arti kunci yang sudah tersimpan.
   /// </param>
   /// <remarks>
   /// Kunci dokumen warisan umumnya terdiri dari beberapa bagian. Modul mendeklarasikannya sebagai record
   /// bertipe dan menerima record itu di handler-nya; engine yang menerjemahkannya ke bentuk kanonik dan
   /// kembali, sehingga tidak ada kode modul yang memecah string kunci sendiri.
   /// </remarks>
   [AttributeUsage(AttributeTargets.Property)]
   public class KeyPartAttribute(int order) : Attribute
   {
      /// <summary>Urutan bagian ini di dalam kuncinya.</summary>
      public int Order { get; } = order;
   }

   /// <summary>
   /// Hasil pemeriksaan modul atas sebuah langkah: boleh diputuskan, atau terblokir beserta alasannya.
   /// </summary>
   /// <remarks>
   /// Blokir di sini bukan penolakan. Request tetap menunggu di langkah itu, dan pemeriksaannya dijalankan
   /// ulang setiap layar dibuka, jadi begitu syaratnya terpenuhi langkah itu terbuka sendiri. Blokir yang
   /// benar-benar menghalangi pekerjaan bisa ditembus pemegang claim yang modul tentukan sendiri, dan
   /// tanda tangan hasil penembusan diberi tanda khusus karena sifatnya darurat.
   /// </remarks>
   public class ApprovalGuard
   {
      private ApprovalGuard() { }

      /// <summary>Langkah ini boleh diputuskan.</summary>
      public static ApprovalGuard Allow { get; } = new() { IsAllowed = true };

      /// <summary>
      /// Langkah ini belum boleh diputuskan.
      /// </summary>
      /// <param name="reason">
      /// Alasannya, ditampilkan di samping tombol yang mati. Ditulis untuk dibaca penanda tangan, jadi
      /// sebutkan apa yang harus terjadi supaya terbuka.
      /// </param>
      /// <param name="overridableBy">
      /// Claim yang boleh menembus blokir ini, ditulis tanpa nama modulnya. Kosong berarti blokirnya tidak
      /// bisa ditembus siapa pun dan hanya bisa hilang kalau syaratnya terpenuhi.
      /// </param>
      public static ApprovalGuard Block(string reason, string? overridableBy = null) =>
         new() { IsAllowed = false, Reason = reason, OverridableBy = overridableBy };

      /// <summary>Langkah ini boleh diputuskan.</summary>
      public bool IsAllowed { get; private init; }

      /// <summary>Alasan blokirnya.</summary>
      public string? Reason { get; private init; }

      /// <summary>Claim yang boleh menembus blokir ini.</summary>
      public string? OverridableBy { get; private init; }
   }

   /// <summary>
   /// Keterangan yang tersedia saat engine menanyakan sesuatu kepada modul tentang sebuah request.
   /// </summary>
   /// <typeparam name="TServices">Service modul pemilik dokumen.</typeparam>
   /// <typeparam name="TKey">Record kunci dokumennya.</typeparam>
   public interface IApprovalContext<out TServices, out TKey>
   {
      /// <summary>Service modul pemilik dokumen, sudah siap dipakai.</summary>
      TServices Services { get; }

      /// <summary>Kunci dokumen yang sedang diproses, sudah dalam bentuk bertipe.</summary>
      TKey DocKey { get; }

      /// <summary>Versi dokumen yang sedang diproses.</summary>
      string DocVersion { get; }

      /// <summary>Id request yang sedang diproses.</summary>
      string ApprovalRequestId { get; }

      /// <summary>Pengaju request ini.</summary>
      string RequesterId { get; }

   }

   /// <summary>
   /// Keterangan tambahan saat modul diminta memutuskan sesuatu tentang satu langkah.
   /// </summary>
   /// <typeparam name="TServices">Service modul pemilik dokumen.</typeparam>
   /// <typeparam name="TKey">Record kunci dokumennya.</typeparam>
   public interface IApprovalStepContext<out TServices, out TKey> : IApprovalContext<TServices, TKey>
   {
      /// <summary>Nama langkah yang sedang diproses.</summary>
      string StepName { get; }

      /// <summary>Siapa yang sedang mengambil keputusan, atau kosong kalau belum ada.</summary>
      string? SignerId { get; }
   }

   /// <summary>
   /// Isian sebuah langkah, dilihat tanpa tipe isiannya. Dipakai engine untuk hal-hal yang sama bagi
   /// semua isian - mengetahui bahwa sebuah langkah memang meminta isian, dan mengambil posisi
   /// kotak-kotaknya.
   /// </summary>
   /// <remarks>
   /// Modul tidak mengimplementasikan antarmuka ini sendiri; yang ditulis modul adalah
   /// <see cref="StepInput{TServices,TKey,TPayload}"/>, yang sudah membawa tipe isiannya.
   /// </remarks>
   public interface IApprovalStepInput
   {
      /// <summary>Posisi setiap kotak isian pada PDF, berurutan seperti yang dideklarasikan modul.</summary>
      IReadOnlyList<ApprovalSlot> FieldSlots { get; }

      /// <summary>Jenis setiap kotak isian, sejajar dengan <see cref="FieldSlots"/>.</summary>
      IReadOnlyList<ApprovalInputFieldKind> FieldKinds { get; }
   }

   /// <summary>
   /// Isian sebuah langkah dilihat dari engine saat langkah itu diputuskan: memeriksa isian yang dikirim
   /// dan menuliskan akibatnya, tanpa engine perlu tahu tipe isiannya.
   /// </summary>
   /// <typeparam name="TServices">Service modul pemilik dokumen.</typeparam>
   /// <typeparam name="TKey">Record kunci dokumennya.</typeparam>
   /// <remarks>
   /// Dipanggil engine saja. Modul tidak mengimplementasikan atau memanggilnya; yang ditulis modul
   /// adalah <see cref="StepInput{TServices,TKey,TPayload}"/>, yang sudah membawa implementasinya.
   /// </remarks>
   public interface IApprovalStepInput<TServices, TKey> : IApprovalStepInput
   {
      /// <summary>
      /// Membaca isian yang dikirim, memeriksanya di server, lalu mengambil nilai setiap kotak isiannya
      /// untuk digambar di PDF.
      /// </summary>
      /// <param name="context">Keterangan langkah yang sedang diputuskan.</param>
      /// <param name="payloadJson">Isian yang dikirim penanda tangan, dalam bentuk JSON.</param>
      /// <returns>Nilai setiap kotak isian, sejajar dengan <see cref="IApprovalStepInput.FieldSlots"/>.</returns>
      /// <exception cref="Em.Shared.ActionException">
      /// 400 kalau isiannya tidak ada atau tidak terbaca; selebihnya apa pun yang dilempar pemeriksaan modul.
      /// </exception>
      Task<IReadOnlyList<ApprovalInputValue>> ValidateAsync(IApprovalStepContext<TServices, TKey> context,
         string? payloadJson);

      /// <summary>
      /// Menuliskan akibat isian itu ke dokumennya, di dalam transaksi keputusan.
      /// </summary>
      /// <param name="context">Keterangan langkah yang sedang diputuskan.</param>
      /// <param name="payloadJson">Isian yang dikirim penanda tangan, dalam bentuk JSON.</param>
      Task SignedAsync(IApprovalStepContext<TServices, TKey> context, string? payloadJson);
   }

   /// <summary>Nilai satu kotak isian yang digambar di PDF setelah langkahnya diputuskan.</summary>
   /// <param name="Text">Teks kotaknya, untuk kotak teks.</param>
   /// <param name="Checked">Apakah kotaknya tercentang, untuk kotak centang.</param>
   public record ApprovalInputValue(string? Text, bool Checked);

   /// <summary>
   /// Isian yang diminta sebuah langkah sebelum bisa diputuskan: apa yang ditampilkan, bagaimana
   /// memeriksanya, apa yang ditulis ke dokumen, dan di mana ia digambar pada PDF.
   /// </summary>
   /// <typeparam name="TServices">Service modul pemilik dokumen.</typeparam>
   /// <typeparam name="TKey">Record kunci dokumennya.</typeparam>
   /// <typeparam name="TPayload">Isian yang dikirim balik penanda tangan.</typeparam>
   public class StepInput<TServices, TKey, TPayload> : IApprovalStepInput<TServices, TKey>
   {
      /// <summary>
      /// Memeriksa isian yang dikirim, di server, sebelum keputusannya ditulis. Pemeriksaan di layar tidak
      /// menggantikan ini.
      /// </summary>
      public Func<IApprovalStepContext<TServices, TKey>, TPayload, Task>? Validate { get; set; }

      /// <summary>
      /// Menulis akibat isian itu ke dokumennya, di dalam transaksi keputusan. Kegagalan di sini
      /// membatalkan tanda tangannya juga, sehingga tidak pernah ada tanda tangan yang akibatnya tidak
      /// tertulis.
      /// </summary>
      public Func<IApprovalStepContext<TServices, TKey>, TPayload, Task>? OnSigned { get; set; }

      /// <summary>
      /// Di mana isian itu digambar pada PDF. Satu entri per kotak isian.
      /// </summary>
      public List<ApprovalInputField<TPayload>> Fields { get; } = [];

      /// <summary>
      /// Menambahkan satu kotak teks pada PDF, lalu mengembalikan isian ini sehingga kotak berikutnya
      /// bisa langsung disambung.
      /// </summary>
      /// <param name="slot">Di mana kotaknya digambar.</param>
      /// <param name="text">Cara mengambil teksnya dari isian yang dikirim.</param>
      public StepInput<TServices, TKey, TPayload> Text(ApprovalSlot slot, Func<TPayload, string?> text) {
         ArgumentNullException.ThrowIfNull(slot);
         ArgumentNullException.ThrowIfNull(text);
         Fields.Add(new ApprovalInputField<TPayload> {
            Kind = ApprovalInputFieldKind.Text,
            Slot = slot,
            Text = text
         });
         return this;
      }

      /// <summary>
      /// Menambahkan satu kotak centang pada PDF, lalu mengembalikan isian ini sehingga kotak berikutnya
      /// bisa langsung disambung.
      /// </summary>
      /// <param name="slot">Di mana kotaknya digambar.</param>
      /// <param name="checked">Cara menentukan kotaknya tercentang atau tidak.</param>
      public StepInput<TServices, TKey, TPayload> Check(ApprovalSlot slot, Func<TPayload, bool> @checked) {
         ArgumentNullException.ThrowIfNull(slot);
         ArgumentNullException.ThrowIfNull(@checked);
         Fields.Add(new ApprovalInputField<TPayload> {
            Kind = ApprovalInputFieldKind.Check,
            Slot = slot,
            Checked = @checked
         });
         return this;
      }

      private static readonly System.Text.Json.JsonSerializerOptions PayloadOptions = new() {
         PropertyNameCaseInsensitive = true
      };

      async Task<IReadOnlyList<ApprovalInputValue>> IApprovalStepInput<TServices, TKey>.ValidateAsync(
         IApprovalStepContext<TServices, TKey> context, string? payloadJson) {
         var payload = ReadPayload(payloadJson);

         if (Validate is not null) await Validate(context, payload);

         return [.. Fields.Select(field => new ApprovalInputValue(
            field.Kind == ApprovalInputFieldKind.Text ? field.Text?.Invoke(payload) : null,
            field.Kind == ApprovalInputFieldKind.Check && field.Checked is not null && field.Checked(payload)))];
      }

      async Task IApprovalStepInput<TServices, TKey>.SignedAsync(IApprovalStepContext<TServices, TKey> context,
         string? payloadJson) {
         if (OnSigned is null) return;

         await OnSigned(context, ReadPayload(payloadJson));
      }

      private static TPayload ReadPayload(string? payloadJson) {
         if (string.IsNullOrWhiteSpace(payloadJson)) {
            throw new Em.Shared.ActionException("This step asks for input, but none was sent.", 400);
         }

         try {
            return System.Text.Json.JsonSerializer.Deserialize<TPayload>(payloadJson, PayloadOptions) ??
                   throw new Em.Shared.ActionException("This step asks for input, but the input sent is empty.", 400);
         }
         catch (System.Text.Json.JsonException ex) {
            throw new Em.Shared.ActionException($"The input sent for this step cannot be read: {ex.Message}", 400);
         }
      }

      /// <inheritdoc />
      public IReadOnlyList<ApprovalSlot> FieldSlots => Fields.Select(r => r.Slot).ToArray();

      /// <inheritdoc />
      public IReadOnlyList<ApprovalInputFieldKind> FieldKinds => Fields.Select(r => r.Kind).ToArray();
   }

   /// <summary>Satu kotak isian pada PDF beserta cara mengambil nilainya dari isian yang dikirim.</summary>
   /// <typeparam name="TPayload">Isian yang dikirim penanda tangan.</typeparam>
   public class ApprovalInputField<TPayload>
   {
      /// <summary>Jenis kotaknya: tanda centang atau teks.</summary>
      public ApprovalInputFieldKind Kind { get; set; }

      /// <summary>Di mana kotaknya digambar.</summary>
      public ApprovalSlot Slot { get; set; } = new(0, 0);

      /// <summary>Cara mengambil nilai teks kotak ini dari isian yang dikirim.</summary>
      public Func<TPayload, string?>? Text { get; set; }

      /// <summary>Cara menentukan kotak centang ini tercentang atau tidak.</summary>
      public Func<TPayload, bool>? Checked { get; set; }
   }

   /// <summary>Jenis kotak isian pada PDF.</summary>
   public enum ApprovalInputFieldKind
   {
      /// <summary>Kotak centang.</summary>
      Check = 0,

      /// <summary>Kotak teks.</summary>
      Text = 1
   }
}
