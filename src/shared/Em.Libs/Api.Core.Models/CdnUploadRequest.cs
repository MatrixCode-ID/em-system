namespace Em.Api.Core.Models
{
   /// <summary>
   /// Information accompanying one file upload to the CDN: which folder, which name, and whether it may
   /// overwrite. The file content is not here - it is sent separately as a stream.
   /// </summary>
   public class CdnUploadRequest
   {
      /// <summary>
      /// Target folder, relative to the CDN root folder and separated by <c>/</c>; <c>null</c> or an
      /// empty string means the root folder.
      /// </summary>
      public string? Path { get; set; }

      /// <summary>File name in the target folder, without path.</summary>
      public string FileName { get; set; } = "";

      /// <summary>
      /// <c>true</c> when a file with the same name in the target folder may be overwritten. <c>false</c>
      /// makes the upload fail with 409 before the file content is read.
      /// </summary>
      public bool Overwrite { get; set; }
   }
}
