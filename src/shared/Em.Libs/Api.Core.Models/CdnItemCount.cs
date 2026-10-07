namespace Em.Api.Core.Models
{
   /// <summary>
   /// Number of items in a CDN folder, counted down to the deepest subfolder. Used to say how much will
   /// be deleted before the user confirms deleting a folder.
   /// </summary>
   public class CdnItemCount
   {
      /// <summary>Number of files in the folder, including those in subfolders.</summary>
      public int Files { get; set; }

      /// <summary>Number of subfolders in the folder, including nested subfolders.</summary>
      public int Folders { get; set; }
   }
}
