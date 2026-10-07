namespace Em.Api.Core.Models
{
   /// <summary>
   /// Thrown when a request is made to read or write a record belonging to a system account - the
   /// debugger account and the administrator account. Both only stand in for the signed-in user; no user
   /// row is ever written for them, so the request is refused on the spot, not answered empty as if the
   /// record once existed and was deleted.
   /// <para>
   /// It derives from <see cref="InvalidOperationException"/> so older callers that catch exceptions in
   /// general keep their behavior. A type of its own exists because some flows need to tell this refusal
   /// apart from other failures - the login screen, which must report it as "wrong credentials" rather
   /// than as a server disturbance - and telling it apart through the message text clearly cannot be
   /// relied on.
   /// </para>
   /// </summary>
   public class SystemAccountException : InvalidOperationException
   {
      /// <summary>
      /// Creates the system account refusal exception.
      /// </summary>
      /// <param name="message">Explanation of why the request was refused.</param>
      /// <param name="accountId">Id of the system account that was requested.</param>
      public SystemAccountException(string message, string accountId) : base(message) {
         AccountId = accountId;
      }

      /// <summary>
      /// Id of the system account that caused this request to be refused.
      /// </summary>
      public string AccountId { get; }
   }
}
