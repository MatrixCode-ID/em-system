using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Result of checking an access token. An invalid token is always answered with <see cref="IsValid"/>
   /// <c>false</c> together with the reason, not with an exception - checking a token routinely fails
   /// (expired, wrong signature, forged), so the failure is part of the answer, not an exceptional event.
   /// </summary>
   /// <param name="IsValid">True when the token is valid and not yet expired.</param>
   /// <param name="cUserId">Owner of the token, set only when the token is valid.</param>
   /// <param name="cUserSessionId">Session that issued the token, set only when the token is valid.</param>
   /// <param name="IsAdmin">True when the token owner was an administrator when the token was issued.</param>
   /// <param name="Error">Reason the token was refused, set only when the token is invalid.</param>
   public sealed record TokenValidation(
      bool IsValid,
      string? cUserId,
      string? cUserSessionId,
      bool IsAdmin,
      string? Error);

   /// <summary>
   /// Issuer and checker of access tokens. An internal engine service: it has no actions and is never
   /// touched directly from outside - what calls it is the credential actions and, later, the request
   /// checking gate.
   /// </summary>
   internal interface ITokenServices
   {
      /// <summary>
      /// Issues a new pair of tokens for a user together with their session row.
      /// </summary>
      /// <param name="cUserId">The user whose session is opened.</param>
      /// <returns>The access token, the refresh token, and the lifetime of the access token in seconds.</returns>
      Task<TokenResult> IssueAsync(string cUserId);

      /// <summary>
      /// Checks an access token: its signature, validity period, and content.
      /// </summary>
      /// <param name="accessToken">The token sent by the caller.</param>
      /// <returns>The result of the check; see <see cref="TokenValidation"/>.</returns>
      Task<TokenValidation> ValidateAsync(string accessToken);

      /// <summary>
      /// Whether a session is still valid: recorded as active and its validity period has not passed. Looked
      /// up in both session stores, because the session id alone does not say where it came from.
      /// </summary>
      /// <param name="cUserSessionId">The session being asked about.</param>
      Task<bool> IsSessionActiveAsync(string cUserSessionId);

      /// <summary>
      /// Ends one session so its refresh token can no longer be used.
      /// </summary>
      /// <param name="cUserSessionId">The session to end.</param>
      Task RevokeAsync(string cUserSessionId);

      /// <summary>
      /// Ends all sessions of a user, on any device.
      /// </summary>
      /// <param name="cUserId">The user whose sessions are all ended.</param>
      Task RevokeAllAsync(string cUserId);

      /// <summary>
      /// All sessions of one account, newest first. Through here, not through a direct query, because system
      /// accounts keep their sessions in a different place from ordinary users - and which one is used is
      /// this service's business, not the caller's.
      /// </summary>
      /// <param name="cUserId">The account whose sessions are listed.</param>
      Task<SessionRecord[]> ListSessionsAsync(string cUserId);

      /// <summary>
      /// Exchanges a refresh token for a new pair of tokens. The refresh token that was exchanged dies
      /// immediately, so one refresh token can only be used once.
      /// </summary>
      /// <param name="refreshToken">The refresh token held by the caller.</param>
      /// <returns>The new pair of tokens.</returns>
      Task<TokenResult> RefreshAsync(string refreshToken);
   }
}
