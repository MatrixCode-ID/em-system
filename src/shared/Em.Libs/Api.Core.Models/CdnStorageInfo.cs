namespace Em.Api.Core.Models
{
   /// <summary>Size of the public file payload across all CDN roots, not disk volume usage.</summary>
   public class CdnStorageInfo
   {
      /// <summary>Total size of the files, in bytes.</summary>
      public long TotalBytes { get; set; }

      /// <summary>Number of files.</summary>
      public long FileCount { get; set; }
   }
}
