namespace Em.Api.Core.Models
{
   /// <summary>
   /// One item of a CDN folder - a file or a subfolder - as reported by the server to the CDN manager
   /// screen.
   /// </summary>
   public class CdnEntry
   {
      /// <summary>File or folder name, without path.</summary>
      public string Name { get; set; } = "";

      /// <summary>
      /// Full path relative to the CDN root folder, separated by <c>/</c> (e.g. <c>installer/app.msi</c>).
      /// This value is sent back to the server to open or delete the item.
      /// </summary>
      public string Path { get; set; } = "";

      /// <summary><c>true</c> for a folder, <c>false</c> for a file.</summary>
      public bool IsFolder { get; set; }

      /// <summary>File size in bytes; always <c>0</c> for a folder.</summary>
      public long Size { get; set; }

      /// <summary>When this file or folder was last modified on the server.</summary>
      public DateTimeOffset LastModified { get; set; }
   }
}
