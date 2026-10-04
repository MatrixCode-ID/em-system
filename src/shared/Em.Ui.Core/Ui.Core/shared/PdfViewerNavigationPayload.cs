namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Parameter untuk membuka viewer PDF bawaan aplikasi. Viewer ini bukan milik module mana pun dan
   /// tidak dijaga claim: ia hanya menampilkan isi yang diberikan <see cref="Loader"/>. Karena itu
   /// pemeriksaan hak ada di tempat isi PDF itu diambil - biasanya action server yang dipanggil
   /// <see cref="Loader"/> - bukan di viewer.
   /// </summary>
   /// <remarks>
   /// Viewer tidak menyimpan hasil <see cref="Loader"/> dalam bentuk lain: yang disimpan lewat "Save as"
   /// adalah byte yang dikembalikannya apa adanya, jadi PDF yang bertanda tangan digital tetap sah.
   /// </remarks>
   public class PdfViewerNavigationPayload : NavigationPayloadBase
   {
      private readonly string _title;

      /// <summary>
      /// Membuat parameter viewer PDF.
      /// </summary>
      /// <param name="title">
      /// Judul tab/layar viewer, sekaligus kunci unik entrinya - lihat <see cref="NavigationPayloadBase.Title"/>.
      /// Sertakan penanda unik dokumennya (mis. nomor dokumen), jangan hanya jenis dokumennya.
      /// </param>
      /// <param name="loader">
      /// Fungsi yang mengambil isi PDF. Dipanggil saat viewer dibuka dan setiap kali tombol Refresh ditekan,
      /// jadi harus bisa dipanggil berulang. Stream yang dikembalikan ditutup oleh viewer.
      /// </param>
      /// <param name="fileName">
      /// Nama file bawaan saat user menyimpan PDF-nya, atau <c>null</c> untuk memakai judulnya.
      /// </param>
      public PdfViewerNavigationPayload(string title, Func<CancellationToken, Task<Stream>> loader,
         string? fileName = null) : base(null) {
         ArgumentException.ThrowIfNullOrWhiteSpace(title);
         ArgumentNullException.ThrowIfNull(loader);
         _title = title;
         Loader = loader;
         FileName = fileName;
      }

      /// <summary>Judul viewer, sekaligus kunci unik entrinya di seluruh aplikasi.</summary>
      public override string Title => _title;

      /// <summary>
      /// Fungsi yang mengambil isi PDF. Token-nya dibatalkan kalau viewer ditutup sebelum isinya selesai
      /// diambil.
      /// </summary>
      public Func<CancellationToken, Task<Stream>> Loader { get; }

      /// <summary>Nama file bawaan saat PDF disimpan, atau <c>null</c> kalau mengikuti judulnya.</summary>
      public string? FileName { get; }
   }
}
