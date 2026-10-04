namespace Em.Api.Core.Models
{
   /// <summary>
   /// Satu isi folder CDN - sebuah file atau subfolder - seperti yang dilaporkan server kepada
   /// layar pengelola CDN.
   /// </summary>
   public class CdnEntry
   {
      /// <summary>Nama file atau folder, tanpa path.</summary>
      public string Name { get; set; } = "";

      /// <summary>
      /// Path lengkapnya relatif terhadap folder akar CDN, dipisah <c>/</c> (mis. <c>installer/app.msi</c>).
      /// Nilai inilah yang dikirim balik ke server saat hendak membuka atau menghapusnya.
      /// </summary>
      public string Path { get; set; } = "";

      /// <summary><c>true</c> untuk folder, <c>false</c> untuk file.</summary>
      public bool IsFolder { get; set; }

      /// <summary>Ukuran file dalam byte; selalu <c>0</c> untuk folder.</summary>
      public long Size { get; set; }

      /// <summary>Waktu terakhir file atau folder ini diubah di server.</summary>
      public DateTimeOffset LastModified { get; set; }
   }
}
