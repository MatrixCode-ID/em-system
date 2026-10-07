namespace Em.Api.Core.Models
{
   /// <summary>Request to create one zip file from several files and folders in one CDN folder.</summary>
   public class CdnArchiveRequest
   {
      /// <summary>
      /// Folder holding the source items, and where the zip file is written. Relative to the CDN root
      /// folder and separated by <c>/</c>; <c>null</c> or an empty string means the root folder.
      /// </summary>
      public string? Folder { get; set; }

      /// <summary>Names of the files and folders in <see cref="Folder"/> to put into the zip, without path.</summary>
      public string[] Names { get; set; } = [];

      /// <summary>Name of the zip file to create; must end with <c>.zip</c>.</summary>
      public string ArchiveName { get; set; } = "";

      /// <summary>
      /// <c>true</c> when an existing file named <see cref="ArchiveName"/> may be overwritten.
      /// <c>false</c> makes the request fail with 409 before its task starts.
      /// </summary>
      public bool Overwrite { get; set; }
   }
}
