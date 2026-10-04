namespace Em.Api.Core.Models
{
   /// <summary>Ukuran payload file publik di seluruh root CDN, bukan pemakaian volume disk.</summary>
   public class CdnStorageInfo
   {
      public long TotalBytes { get; set; }
      public long FileCount { get; set; }
   }
}
