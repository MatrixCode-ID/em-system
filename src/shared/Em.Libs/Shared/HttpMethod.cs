namespace Em.Shared
{
   /// <summary>
   /// HTTP methods known to the <c>EmApp</c> dispatcher to decide how an action is reached. Currently an
   /// action can only be registered as <see cref="Get"/> (through <c>[GetAction]</c>) or
   /// <see cref="Post"/> (through <c>[PostAction]</c>); the other values are reserved for the future.
   /// </summary>
   public enum HttpMethod
   {
      /// <summary>Request sent over HTTP POST, with a JSON body.</summary>
      Post,

      /// <summary>Request sent over HTTP GET, with parameters in the query string.</summary>
      Get,

      /// <summary>HTTP PUT. Not used by the dispatcher yet.</summary>
      Put,

      /// <summary>HTTP DELETE. Not used by the dispatcher yet.</summary>
      Delete,

      /// <summary>HTTP HEAD. Not used by the dispatcher yet.</summary>
      Head,
   }
}
