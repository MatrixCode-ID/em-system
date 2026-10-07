using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// One session row in a form that no longer names its source table. Its refresh token hash is
   /// deliberately left out: only the row lookup needs it, and that is the store's business, not anyone
   /// who receives this record.
   /// </summary>
   internal sealed record SessionRecord(
      string SessionId,
      string AccountId,
      SessionState State,
      DateTime Expiry,
      DateTime StartedAt,
      DateTime UpdatedAt);

   /// <summary>
   /// Place where sign-in sessions are stored. There are two: one for ordinary users who have a row in the
   /// user table, another for system accounts that do not. They are separated by table, not by branching
   /// inside the token issuer - this way <see cref="TokenServices"/> picks the store once up front, then
   /// treats anyone's session the same way.
   /// </summary>
   internal interface ISessionStore
   {
      /// <summary>Opens a new session in the active state.</summary>
      Task AddAsync(string sessionId, string accountId, string hash, DateTime expiry);

      /// <summary>Finds a session by its refresh token hash; <c>null</c> when there is none.</summary>
      Task<SessionRecord?> FindByHashAsync(string hash);

      /// <summary>
      /// Finds a session by its id; <c>null</c> when the session is not in this store - and that is what
      /// tells a session that truly does not exist apart from one belonging to the neighboring store, because
      /// the session id alone does not say which table it came from.
      /// </summary>
      Task<SessionRecord?> FindByIdAsync(string sessionId);

      /// <summary>
      /// Changes the status of one session. Returns <c>false</c> when the session is not in this store -
      /// that is what tells someone else's session apart from one belonging to the neighboring store, because
      /// the session id alone does not say which table it came from.
      /// </summary>
      Task<bool> SetStateAsync(string sessionId, SessionState state);

      /// <summary>
      /// Revokes one session and opens its replacement in a single save. Kept apart from
      /// <see cref="SetStateAsync"/> + <see cref="AddAsync"/> precisely because both must succeed or fail
      /// together: if the revocation succeeds but the replacement does not, the token holder loses their
      /// session for no reason; if the order is reversed, the old refresh token briefly lives alongside the
      /// new one.
      /// </summary>
      Task RotateAsync(string oldSessionId, string newSessionId, string accountId, string hash, DateTime expiry);

      /// <summary>Revokes every session that is still active for one account.</summary>
      Task RevokeAllAsync(string accountId);

      /// <summary>All sessions of one account, newest first.</summary>
      Task<SessionRecord[]> ListByAccountAsync(string accountId);

      /// <summary>
      /// Cleans up dead sessions in two stages: those whose validity period has passed but are still recorded
      /// as active are marked expired, then those that passed longer ago than
      /// <paramref name="retention"/> are discarded.
      /// </summary>
      Task PurgeAsync(TimeSpan retention);
   }
}
