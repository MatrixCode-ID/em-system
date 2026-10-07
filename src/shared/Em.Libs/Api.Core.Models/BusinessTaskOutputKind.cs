namespace Em.Api.Core.Models
{
   /// <summary>Kind of result a business task leaves behind after success.</summary>
   public enum BusinessTaskOutputKind
   {
      /// <summary>
      /// No result to fetch: the work itself is the result (e.g. creating an archive). Such tasks
      /// disappear on their own shortly after success.
      /// </summary>
      None = 0,

      /// <summary>The result is JSON data, fetched through the JSON result action.</summary>
      Json = 1,

      /// <summary>The result is a file (e.g. Excel), downloaded through the file result action.</summary>
      File = 2
   }
}
