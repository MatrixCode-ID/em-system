using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   // Derives from ServicesBase for one reason: the signing key lives in the core metadata and
   // GetServerRsaKeyAsync is the one place that reads it - and creates it on first use. That is not
   // worth a second copy here. It is still not a module service: it carries no [Module], no action,
   // and is registered by hand in EmApp rather than through EmAppBuilder.AddService.
   internal sealed class TokenServices : ServicesBase, ITokenServices
   {
      // Short on purpose. An access token cannot be taken back once handed out - the only thing
      // limiting the damage of a stolen one is how soon it stops working.
      private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

      // How long a session may keep renewing itself before the user has to sign in again.
      private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

      // A little slack for the clock difference between this server and whoever validates: without
      // it a token issued on a machine running a few seconds behind reads as already expired.
      private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

      private const int RefreshTokenByteLength = 32;

      private readonly ApiCoreContext _ctx;
      private readonly ISessionStore _userSessions;
      private readonly ISessionStore _systemSessions;

      public TokenServices(EmApp app, ApiCoreContext ctx) {
         App = app;
         _ctx = ctx;
         _userSessions = new UserSessionStore(ctx);
         _systemSessions = new SystemSessionStore(ctx);
      }

      public async Task<TokenResult> IssueAsync(string cUserId) {
         var now = DateTime.UtcNow;
         var sessionId = $"{Ulid.NewUlid()}";
         var refreshToken = CreateRefreshToken();

         await PurgeDeadSessionsAsync();

         // The session row is written before the access token is signed, so a token never leaves
         // here naming a session that failed to be stored.
         await StoreFor(cUserId).AddAsync(sessionId, cUserId, HashRefreshToken(refreshToken),
            now.Add(RefreshTokenLifetime));

         return await BuildTokenResultAsync(cUserId, sessionId, refreshToken, now);
      }

      public async Task<TokenValidation> ValidateAsync(string accessToken) {
         if (string.IsNullOrWhiteSpace(accessToken)) {
            return new TokenValidation(false, null, null, false, "No access token was presented.");
         }

         var key = await GetServerRsaKeyAsync();
         using var rsa = key.CreateRsa();

         var result = await new JsonWebTokenHandler().ValidateTokenAsync(accessToken, new TokenValidationParameters {
            // The issuer and the audience are this one server, and nothing else ever signs with its
            // key - there is no second party for either claim to tell apart, so neither is written
            // into the token and neither is checked here.
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = SigningKeyFor(rsa),
            ClockSkew = ClockSkew
         });

         if (!result.IsValid) {
            return new TokenValidation(false, null, null, false,
               result.Exception?.Message ?? "The access token is not valid.");
         }

         var claims = result.Claims;
         var cUserId = claims.TryGetValue(SubjectClaim, out var subject) ? subject as string : null;
         var cUserSessionId = claims.TryGetValue(SessionClaim, out var session) ? session as string : null;
         var isAdmin = claims.TryGetValue(AdminClaim, out var admin) && admin is true;

         if (string.IsNullOrEmpty(cUserId) || string.IsNullOrEmpty(cUserSessionId)) {
            return new TokenValidation(false, null, null, false, "The access token is missing its identity claims.");
         }

         // Checked here rather than at the gate, so that no caller can forget to. Without it signing
         // out would only take hold once the access token expired by itself - up to fifteen minutes
         // after the button was pressed. It costs one query per request that carries a token; the
         // debug token path has no session at all and never reaches this method.
         if (!await IsSessionActiveAsync(cUserSessionId)) {
            return new TokenValidation(false, null, null, false,
               "The session behind this access token is no longer active.");
         }

         return new TokenValidation(true, cUserId, cUserSessionId, isAdmin, null);
      }

      public async Task<bool> IsSessionActiveAsync(string cUserSessionId) {
         // Whichever store owns the id answers; the other finds nothing. Rotation is why this matters
         // on every request: refreshing replaces the session row, so the id in the access token that
         // went with the old refresh token now points at a row marked Revoked - and that old access
         // token is meant to die with it.
         var session = await _userSessions.FindByIdAsync(cUserSessionId)
            ?? await _systemSessions.FindByIdAsync(cUserSessionId);

         return session is { State: SessionState.Active } && session.Expiry > DateTime.UtcNow;
      }

      public async Task RevokeAsync(string cUserSessionId) {
         // A session id on its own does not say which table it came out of, and a session id is all
         // a sign-out has to go on. Whichever store owns it answers; the other finds nothing.
         if (await _userSessions.SetStateAsync(cUserSessionId, SessionState.Revoked)) return;
         await _systemSessions.SetStateAsync(cUserSessionId, SessionState.Revoked);
      }

      public Task RevokeAllAsync(string cUserId) => StoreFor(cUserId).RevokeAllAsync(cUserId);

      public Task<SessionRecord[]> ListSessionsAsync(string cUserId) => StoreFor(cUserId).ListByAccountAsync(cUserId);

      // One message for every way a refresh can fail, and 401 for all of them: whoever is holding
      // this token cannot get a new one out of it, and the only way forward is signing in again.
      private const string InvalidRefreshTokenMessage = "The refresh token is not valid.";

      public async Task<TokenResult> RefreshAsync(string refreshToken) {
         if (string.IsNullOrWhiteSpace(refreshToken)) {
            throw new ActionException(InvalidRefreshTokenMessage, 401);
         }

         var hash = HashRefreshToken(refreshToken);

         // Which store holds the session is not known until it is found: a refresh token says
         // nothing about whose it is, which is the whole point of it being random bytes.
         var store = _userSessions;
         var session = await _userSessions.FindByHashAsync(hash);
         if (session is null) {
            store = _systemSessions;
            session = await _systemSessions.FindByHashAsync(hash);
         }

         // One message for every way a refresh can fail - unknown token, revoked session, expired
         // session. Telling them apart would let whoever holds a dead token learn whether it was
         // ever real and whose session it belonged to.
         if (session is null || session.State != SessionState.Active) {
            throw new ActionException(InvalidRefreshTokenMessage, 401);
         }

         var now = DateTime.UtcNow;
         if (session.Expiry <= now) {
            await store.SetStateAsync(session.SessionId, SessionState.Expired);
            throw new ActionException(InvalidRefreshTokenMessage, 401);
         }

         // The switch is read again here, not only at sign-in: turning it off in the database then
         // takes hold within one access token's lifetime rather than one session's.
         if (session.AccountId == Defaults.AdminUserId && !await AdminAccount.IsEnabledAsync(_ctx)) {
            await store.SetStateAsync(session.SessionId, SessionState.Revoked);
            throw new ActionException(InvalidRefreshTokenMessage, 401);
         }

         await PurgeDeadSessionsAsync();

         // Rotation: the token just handed in dies here, whatever happens next. A refresh token
         // that survived its own use would still open a session after being copied off the wire.
         var newSessionId = $"{Ulid.NewUlid()}";
         var newRefreshToken = CreateRefreshToken();

         // The renewed session inherits the expiry of the one it replaces rather than starting
         // over: otherwise a client that keeps refreshing would never have to sign in again.
         await store.RotateAsync(session.SessionId, newSessionId, session.AccountId,
            HashRefreshToken(newRefreshToken), session.Expiry);

         return await BuildTokenResultAsync(session.AccountId, newSessionId, newRefreshToken, now);
      }

      #region Session stores

      // The one place the two kinds of session part ways. A system account has no row in the user
      // table, so its session cannot be kept in a table that insists on one.
      private ISessionStore StoreFor(string accountId) =>
         IsSystemAccount(accountId) ? _systemSessions : _userSessions;

      private static bool IsSystemAccount(string accountId) =>
         accountId == Defaults.AdminUserId || accountId == Defaults.DebuggerUserId;

      // Dead sessions are swept when one is created - at sign-in and at refresh - and never while a
      // token is being validated, which happens on every single request. Even that is more often
      // than the work is worth, so it is held to once every few minutes per process: a burst of
      // sign-ins should sweep once, not once each.
      private static readonly TimeSpan PurgeInterval = TimeSpan.FromMinutes(10);

      private static long _lastPurgeTicks;

      private async Task PurgeDeadSessionsAsync() {
         var now = DateTime.UtcNow.Ticks;
         var last = Interlocked.Read(ref _lastPurgeTicks);
         if (now - last < PurgeInterval.Ticks) return;

         // Whoever wins the exchange does the sweep; anyone else that arrived at the same moment
         // simply carries on, because sweeping is not what they were asked to do.
         if (Interlocked.CompareExchange(ref _lastPurgeTicks, now, last) != last) return;

         var retention = TimeSpan.FromHours(App.SessionTokenRetentionHour);
         await _userSessions.PurgeAsync(retention);
         await _systemSessions.PurgeAsync(retention);
      }

      #endregion

      #region Helpers

      // The three claims the access token carries, and nothing else. Whatever is put in here rides
      // along on every single request and goes stale the moment the stored row changes, with no way
      // to correct it before the token runs out.
      private const string SubjectClaim = "sub";
      private const string SessionClaim = "sid";
      private const string AdminClaim = "adm";

      private async Task<TokenResult> BuildTokenResultAsync(string cUserId, string cUserSessionId,
         string refreshToken, DateTime issuedAt) {
         // The administrator account is an administrator by definition: there is no row to read the
         // flag off, and the account exists for nothing else.
         var isAdmin = cUserId == Defaults.AdminUserId || await _ctx.ta_Users
            .Where(r => r.cUserId == cUserId)
            .Select(r => r.cUserIsAdmin)
            .SingleOrDefaultAsync();

         var key = await GetServerRsaKeyAsync();
         using var rsa = key.CreateRsa();

         var accessToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
            Claims = new Dictionary<string, object> {
               [SubjectClaim] = cUserId,
               [SessionClaim] = cUserSessionId,
               [AdminClaim] = isAdmin
            },
            IssuedAt = issuedAt,
            Expires = issuedAt.Add(AccessTokenLifetime),
            SigningCredentials = new SigningCredentials(SigningKeyFor(rsa), SecurityAlgorithms.RsaSha256)
         });

         return new TokenResult {
            cUserId = cUserId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = (int)AccessTokenLifetime.TotalSeconds
         };
      }

      // The RSA instance behind the key is rebuilt from the stored key material on every call and
      // thrown away again at the end of it, so the crypto provider cache must not be allowed to keep
      // hold of it: that cache is keyed by the key material, not by the instance, so the next call -
      // holding the same material in a new instance - would be handed back a provider still pointing
      // at the RSA the previous call has already disposed.
      private static RsaSecurityKey SigningKeyFor(RSA rsa) => new(rsa) {
         CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
      };

      // Base64Url rather than plain Base64: the token travels in JSON and in headers, and the two
      // characters plain Base64 adds are the two that have to be escaped in both.
      private static string CreateRefreshToken() =>
         Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(RefreshTokenByteLength));

      // SHA-256 rather than the Argon2 used for passwords, and deliberately so: a refresh token is
      // 32 random bytes this server generated, not something a person chose, so there is no small
      // set of likely values to try and nothing for a slow hash to buy. What is needed instead is a
      // hash the same token always lands on, so the row can be found by it on every refresh.
      private static string HashRefreshToken(string refreshToken) =>
         Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

      #endregion
   }
}
