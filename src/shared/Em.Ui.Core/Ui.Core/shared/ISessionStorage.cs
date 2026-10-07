namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// A session that is stored so the user does not need to type the password again after the application
   /// is closed. Only the refresh token is stored, together with a note of its owner - the access token is
   /// not, because it lives for a few minutes and is certainly dead before the application is opened again.
   /// </summary>
   /// <param name="RefreshToken">The refresh token that is exchanged when the application is opened again.</param>
   /// <param name="cUserId">Owner of the session, used to load their identity without guessing from the account name.</param>
   /// <param name="cUserAccount">Account name of the session owner, used to fill the login screen when restoring fails.</param>
   public sealed record SavedSession(string RefreshToken, string cUserId, string cUserAccount);

   /// <summary>
   /// Where the stored session is deposited, one per connection profile. The contract is here while its
   /// implementer lives in the layer that knows the operating system: whoever stores a refresh token must
   /// bind it to the machine account that is running, and how to do that differs per platform.
   /// </summary>
   public interface ISessionStorage
   {
      /// <summary>Stores (or overwrites) the session of a connection profile.</summary>
      /// <param name="profileName">Name of the connection profile that owns the session.</param>
      /// <param name="session">The session to store.</param>
      void Save(string profileName, SavedSession session);

      /// <summary>
      /// Reads the stored session of a connection profile; <c>null</c> when there is none, or when what is
      /// stored can no longer be opened.
      /// </summary>
      /// <param name="profileName">Name of the connection profile whose session is being looked for.</param>
      SavedSession? Load(string profileName);

      /// <summary>Discards the stored session of a connection profile.</summary>
      /// <param name="profileName">Name of the connection profile whose session is discarded.</param>
      void Clear(string profileName);
   }
}
