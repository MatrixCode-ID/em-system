namespace Em.Api.Core.Models
{
   /// <summary>
   /// Keterangan yang menyertai satu unggahan file ke CDN: ke folder mana, dengan nama apa, dan boleh
   /// menimpa atau tidak. Isi filenya sendiri tidak ada di sini - ia dikirim terpisah sebagai stream.
   /// </summary>
   public class CdnUploadRequest
   {
      /// <summary>
      /// Folder tujuan, relatif terhadap folder akar CDN dan dipisah <c>/</c>; <c>null</c> atau string
      /// kosong berarti folder akar.
      /// </summary>
      public string? Path { get; set; }

      /// <summary>Nama file di folder tujuan, tanpa path.</summary>
      public string FileName { get; set; } = "";

      /// <summary>
      /// <c>true</c> kalau file dengan nama yang sama di folder tujuan boleh ditimpa. <c>false</c> membuat
      /// unggahan ditolak 409 tanpa isi filenya sempat dibaca.
      /// </summary>
      public bool Overwrite { get; set; }
   }
}
