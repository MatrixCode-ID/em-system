namespace Em.Api.Core.Models
{
   /// <summary>
   /// Isi satu folder CDN berikut keterangan yang dibutuhkan layar pengelolanya: batas ukuran unggahan
   /// dan alamat publik folder tersebut.
   /// </summary>
   public class CdnFolderContent
   {
      /// <summary>
      /// Path folder ini relatif terhadap folder akar CDN, dipisah <c>/</c>; string kosong untuk akar.
      /// </summary>
      public string Path { get; set; } = "";

      /// <summary>Isi folder: subfolder lebih dulu, lalu file, masing-masing urut nama.</summary>
      public CdnEntry[] Entries { get; set; } = [];

      /// <summary>
      /// Batas ukuran satu file yang boleh diunggah, dalam byte. Dipakai client untuk menolak file yang
      /// terlalu besar sebelum dikirim.
      /// </summary>
      public long MaxFileSize { get; set; }

      /// <summary>
      /// Alamat publik folder ini relatif terhadap alamat server, selalu diakhiri <c>/</c>
      /// (mis. <c>cdn/installer/</c>). Digabung dengan alamat server untuk menyusun tautan yang bisa
      /// dibagikan.
      /// </summary>
      public string PublicPath { get; set; } = "";
   }
}
