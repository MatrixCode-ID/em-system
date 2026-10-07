namespace Em.Api.Core.Models
{
   /// <summary>
   /// Contents of one CDN folder plus what its manager screen needs: the upload size limit and the
   /// folder's public address.
   /// </summary>
   public class CdnFolderContent
   {
      /// <summary>
      /// Path of this folder relative to the CDN root folder, separated by <c>/</c>; an empty string for the root.
      /// </summary>
      public string Path { get; set; } = "";

      /// <summary>Folder contents: subfolders first, then files, each sorted by name.</summary>
      public CdnEntry[] Entries { get; set; } = [];

      /// <summary>
      /// Size limit of one uploaded file, in bytes. Used by the client to reject files that are too large
      /// before sending them.
      /// </summary>
      public long MaxFileSize { get; set; }

      /// <summary>
      /// Public address of this folder relative to the server address, always ending with <c>/</c>
      /// (e.g. <c>cdn/installer/</c>). Combined with the server address to build a shareable link.
      /// </summary>
      public string PublicPath { get; set; } = "";
   }
}
