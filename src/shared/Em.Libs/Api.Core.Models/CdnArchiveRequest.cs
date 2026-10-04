namespace Em.Api.Core.Models
{
   /// <summary>
   /// Permintaan membuat satu file zip dari beberapa file dan folder yang berada di satu folder CDN.
   /// </summary>
   public class CdnArchiveRequest
   {
      /// <summary>
      /// Folder tempat item sumber berada, sekaligus tempat file zip-nya ditulis. Relatif terhadap folder
      /// akar CDN dan dipisah <c>/</c>; <c>null</c> atau string kosong berarti folder akar.
      /// </summary>
      public string? Folder { get; set; }

      /// <summary>Nama file dan folder di <see cref="Folder"/> yang dimasukkan ke zip, tanpa path.</summary>
      public string[] Names { get; set; } = [];

      /// <summary>Nama file zip yang dibuat, harus berakhiran <c>.zip</c>.</summary>
      public string ArchiveName { get; set; } = "";

      /// <summary>
      /// <c>true</c> kalau file dengan nama <see cref="ArchiveName"/> yang sudah ada boleh ditimpa.
      /// <c>false</c> membuat permintaan ditolak 409 sebelum task-nya dimulai.
      /// </summary>
      public bool Overwrite { get; set; }
   }
}
