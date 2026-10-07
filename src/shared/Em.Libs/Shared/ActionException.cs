namespace Em.Shared
{
   /// <summary>
   /// Failure of an action that already knows its own HTTP status. Used in both directions: thrown by a
   /// service on the server so the answer does not fall back to 500, then thrown again on the client with
   /// the same status when that answer is read - so UI callers can tell "wrong credentials" from "server
   /// unreachable" without matching message text.
   /// <para>
   /// Without it every exception from an action is wrapped as 500 by the dispatcher, so failures that
   /// happen normally - a wrong password, an expired token, a request that is not allowed - cannot be told
   /// apart from a server that is really broken.
   /// </para>
   /// <para>
   /// Deriving from <see cref="InvalidOperationException"/> follows the <c>SystemAccountException</c>
   /// pattern: older callers that catch exceptions generally keep their behavior.
   /// </para>
   /// </summary>
   public class ActionException : InvalidOperationException
   {
      /// <summary>
      /// Creates an action failure with the HTTP status to send to the caller.
      /// </summary>
      /// <param name="message">
      /// Explanation that reaches the caller as is. Do not put anything outsiders must not know here - the
      /// content is sent to the client.
      /// </param>
      /// <param name="statusCode">
      /// HTTP status of this answer. Common values: <c>400</c> the request itself is wrong, <c>401</c> the
      /// caller's identity is not proven, <c>403</c> the caller is known but not allowed, <c>404</c> the
      /// requested item does not exist.
      /// </param>
      public ActionException(string message, int statusCode) : base(message) {
         StatusCode = statusCode;
      }

      /// <summary>
      /// HTTP status carried by this failure.
      /// </summary>
      public int StatusCode { get; }
   }
}
