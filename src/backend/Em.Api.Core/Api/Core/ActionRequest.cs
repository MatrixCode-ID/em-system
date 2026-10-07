using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Where the caller's identity is proven from.
   /// </summary>
   public enum CallerSource
   {
      /// <summary>Nothing proves anything - the request arrives without identity.</summary>
      None,

      /// <summary>The identity comes from the access token carried by the <c>Authorization</c> header.</summary>
      AccessToken,

      /// <summary>The identity comes from a developer's debug token.</summary>
      DebugToken
   }

   /// <summary>
   /// All information about one request being handled: who the caller is as far as can be proven, by
   /// which way they proved it, and which action it is aimed at. Composed once by the gate in
   /// <c>EmApp.ProcessRequest</c>, then read as-is - the pair of <see cref="ActionResult"/>: one goes in,
   /// one comes out.
   /// </summary>
   /// <remarks>
   /// This object is immutable, so it is safe to read from anywhere while the request runs. What is not
   /// allowed: storing it in a field of an object that outlives its request, or carrying it into
   /// background work. It is a snapshot of one request - used elsewhere, it answers a question that was
   /// never asked there.
   /// <para>
   /// How to get it: inside a module service through <c>ServicesBase.Request</c>; in a helper class
   /// created by DI by asking for it in the constructor; in code that has neither by receiving it as an
   /// ordinary parameter.
   /// </para>
   /// </remarks>
   public sealed record ActionRequest
   {
      /// <summary>
      /// The single instance for the "no request" state - used as the initial value of
      /// <c>ServicesBase.Request</c> and as the answer outside the request path (startup, seeding,
      /// background work). All its identity is empty, so every rights checker below answers 401: no request
      /// means nothing is proven.
      /// </summary>
      public static ActionRequest None { get; } = new();

      /// <summary>
      /// The user calling this action, according to what the request proved. <c>null</c> means no identity at
      /// all - which is normal for public actions.
      /// </summary>
      public string? cUserId { get; init; }

      /// <summary>
      /// The caller's account name, and only when the name is really proven from data. The access token path
      /// leaves it <c>null</c>: the token only carries the id, and translating it to an account name would
      /// cost one extra query on every request. The name attached in the header is never used to fill it -
      /// it comes from the caller, not from data.
      /// </summary>
      public string? cUserAccount { get; init; }

      /// <summary>
      /// The session that issued the caller's token - this is what gets ended when the user signs out of this
      /// device only. Always <c>null</c> on the debug token path, because that path does not open a session.
      /// </summary>
      public string? cUserSessionId { get; init; }

      /// <summary>
      /// Whether the caller has full rights as an administrator. Always <c>false</c> while
      /// <see cref="cUserId"/> is empty - no identity, no rights.
      /// </summary>
      public bool IsAdmin { get; init; }

      /// <summary>
      /// All rights that currently apply to the caller - granted directly or coming through roles, already
      /// filtered by validity period. Reloaded on every request, not carried by the token: rights change far
      /// more often than administrator status, and if they were stored in the token, every revocation would
      /// have to end all of its owner's sessions.
      /// </summary>
      /// <remarks>
      /// Empty for a caller without identity, and also for administrators and the debug token path - both
      /// skip the rights check entirely, so loading them would mean paying one query for an answer that is
      /// never read. Empty here therefore does not mean "has no rights at all"; check
      /// <see cref="IsAdmin"/> and <see cref="IsDebugRequest"/> first.
      /// <para>
      /// The name deliberately matches the client's <c>User.AvailableClaims</c>: one meaning, two sides.
      /// </para>
      /// </remarks>
      public ClaimAction[] Claims { get; init; } = [];

      /// <summary>
      /// By which way the identity above was proven.
      /// </summary>
      public CallerSource Source { get; init; }

      /// <summary><c>true</c> when this request arrived through a debug token.</summary>
      public bool IsDebugRequest => Source == CallerSource.DebugToken;

      /// <summary><c>true</c> when there is a caller whose identity is proven.</summary>
      public bool IsAuthenticated => cUserId is not null;

      /// <summary>
      /// Name of the debug key used by this request. Set only on the debug token path.
      /// </summary>
      public string? DebugKeyName { get; init; }

      /// <summary>
      /// <c>true</c> when the developer is impersonating another account instead of using the debugger
      /// account. Set only on the debug token path.
      /// </summary>
      public bool IsImpersonating { get; init; }

      /// <summary>
      /// The targeted action, in the form <c>{module}/{action}</c>.
      /// </summary>
      public string RouteLabel { get; init; } = string.Empty;

      /// <summary>
      /// Origin address of the request as far as the host knows, or <c>null</c> when unknown.
      /// </summary>
      public string? CallerAddress { get; init; }

      /// <summary>
      /// When the gate accepted this request, in UTC.
      /// </summary>
      public DateTime ReceivedAtUtc { get; init; }

      #region Permission checks

      /// <summary>
      /// <c>true</c> when <paramref name="cUserId"/> is the caller themselves.
      /// </summary>
      public bool IsSelf(string cUserId) => this.cUserId is { } caller && caller == cUserId;

      /// <summary>
      /// Requires a proven caller, then returns their id.
      /// </summary>
      /// <exception cref="ActionException">401 when the request carries no identity at all.</exception>
      public string RequireUserId() =>
         cUserId ?? throw new ActionException(NotSignedInMessage, 401);

      /// <summary>
      /// Requires a proven session, then returns its id. The debug token path has no session, so it is always
      /// refused here - and rightly so: there is no session that could be ended.
      /// </summary>
      /// <exception cref="ActionException">401 when the request carries no session.</exception>
      public string RequireSessionId() =>
         cUserSessionId ?? throw new ActionException(NotSignedInMessage, 401);

      /// <summary>
      /// Requires a caller with full rights as an administrator.
      /// </summary>
      /// <exception cref="ActionException">
      /// 401 when there is no identity yet; 403 when there is an identity but it is not an administrator.
      /// </exception>
      public void RequireAdmin() {
         RequireUserId();

         // 403, not 401: who the caller is has already been proven, and proving it again would not change the
         // answer. A client that refreshes its token on every 401 would keep chasing a new token for a request
         // that will never be permitted, if the second case were also answered 401.
         if (!IsAdmin) {
            throw new ActionException("Only an administrator may do this.", 403);
         }
      }

      /// <summary>
      /// Requires a caller acting on themselves, or an administrator when someone else is named. Without
      /// this check anyone could read another person's sessions or overwrite their password.
      /// </summary>
      /// <param name="cUserId">The user the action is aimed at.</param>
      /// <exception cref="ActionException">
      /// 401 when there is no identity yet; 403 when there is an identity but it is neither themselves nor
      /// an administrator - see the reason at <see cref="RequireAdmin"/>.
      /// </exception>
      public void RequireSelfOrAdmin(string cUserId) {
         RequireUserId();
         if (IsSelf(cUserId)) return;

         if (!IsAdmin) {
            throw new ActionException("Only an administrator may do this for another user.", 403);
         }
      }

      // One sentence for every way a request can arrive without identity, so the answer does not reveal
      // which part of the token is missing.
      private const string NotSignedInMessage = "This action requires a signed-in caller.";

      #endregion
   }
}
