namespace Em.Api.Core.Models
{
   /// <summary>
   /// Jumlah isi sebuah folder CDN, dihitung sampai ke subfolder terdalam. Dipakai untuk menyebutkan
   /// seberapa banyak yang ikut terhapus sebelum pengguna mengonfirmasi penghapusan folder.
   /// </summary>
   public class CdnItemCount
   {
      /// <summary>Jumlah file di dalam folder, termasuk yang ada di subfolder.</summary>
      public int Files { get; set; }

      /// <summary>Jumlah subfolder di dalam folder, termasuk subfolder dari subfolder.</summary>
      public int Folders { get; set; }
   }
}
