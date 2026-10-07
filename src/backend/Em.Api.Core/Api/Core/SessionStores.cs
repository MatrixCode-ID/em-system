using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Sessions of users who have a row in the user table.
   /// </summary>
   internal sealed class UserSessionStore(ApiCoreContext ctx) : ISessionStore
   {
      public Task AddAsync(string sessionId, string accountId, string hash, DateTime expiry) {
         ctx.ta_UserSessions.Add(NewRow(sessionId, accountId, hash, expiry));
         return ctx.SaveChangesAsync();
      }

      public async Task<SessionRecord?> FindByHashAsync(string hash) {
         var row = await ctx.ta_UserSessions.SingleOrDefaultAsync(r => r.cUserSessionHash == hash);
         return row is null ? null : ToRecord(row);
      }

      public async Task<SessionRecord?> FindByIdAsync(string sessionId) {
         var row = await ctx.ta_UserSessions.AsNoTracking()
            .SingleOrDefaultAsync(r => r.cUserSessionId == sessionId);
         return row is null ? null : ToRecord(row);
      }

      public async Task<bool> SetStateAsync(string sessionId, SessionState state) {
         // Tracked on purpose: the row read here is edited in place and written by the save
         // below, and reads are no-tracking by default.
         var row = await ctx.ta_UserSessions.AsTracking()
            .SingleOrDefaultAsync(r => r.cUserSessionId == sessionId);
         if (row is null) return false;

         row.cUserSessionState = state;
         row.ustamp = DateTime.UtcNow;
         await ctx.SaveChangesAsync();
         return true;
      }

      public async Task RotateAsync(string oldSessionId, string newSessionId, string accountId, string hash,
         DateTime expiry) {
         // Tracked for the same reason as above: the old row is revoked by editing it here.
         var old = await ctx.ta_UserSessions.AsTracking()
            .SingleOrDefaultAsync(r => r.cUserSessionId == oldSessionId);
         if (old is not null) {
            old.cUserSessionState = SessionState.Revoked;
            old.ustamp = DateTime.UtcNow;
         }

         ctx.ta_UserSessions.Add(NewRow(newSessionId, accountId, hash, expiry));
         await ctx.SaveChangesAsync();
      }

      public async Task RevokeAllAsync(string accountId) {
         // Only the live ones are touched: a session that was already revoked or has expired stays
         // as it is, so the row keeps saying how it actually ended.
         var now = DateTime.UtcNow;
         await ctx.ta_UserSessions
            .Where(r => r.cUserId == accountId && r.cUserSessionState == SessionState.Active)
            .ExecuteUpdateAsync(s => s
               .SetProperty(r => r.cUserSessionState, SessionState.Revoked)
               .SetProperty(r => r.ustamp, now));
      }

      public async Task<SessionRecord[]> ListByAccountAsync(string accountId) {
         var rows = await ctx.ta_UserSessions
            .AsNoTracking()
            .Where(r => r.cUserId == accountId)
            .OrderByDescending(r => r.datestamp)
            .ToArrayAsync();
         return [.. rows.Select(ToRecord)];
      }

      public async Task PurgeAsync(TimeSpan retention) {
         var now = DateTime.UtcNow;

         // Nothing else ever moves a session to Expired: refreshing is the only thing that reads a
         // session at all, so one that simply runs out without being used again would keep saying
         // Active forever.
         await ctx.ta_UserSessions
            .Where(r => r.cUserSessionState == SessionState.Active && r.cUserSessionExpiry <= now)
            .ExecuteUpdateAsync(s => s
               .SetProperty(r => r.cUserSessionState, SessionState.Expired)
               .SetProperty(r => r.ustamp, now));

         var cutoff = now - retention;
         await ctx.ta_UserSessions
            .Where(r => r.cUserSessionExpiry <= cutoff)
            .ExecuteDeleteAsync();
      }

      private static ta_UserSession NewRow(string sessionId, string accountId, string hash, DateTime expiry) {
         var now = DateTime.UtcNow;
         return new ta_UserSession {
            cUserSessionId = sessionId,
            cUserId = accountId,
            cUserSessionHash = hash,
            cUserSessionState = SessionState.Active,
            cUserSessionExpiry = expiry,
            ustamp = now,
            datestamp = now,
            json_object = null
         };
      }

      private static SessionRecord ToRecord(ta_UserSession row) =>
         new(row.cUserSessionId, row.cUserId, row.cUserSessionState, row.cUserSessionExpiry, row.datestamp, row.ustamp);
   }

   /// <summary>
   /// Sessions of system accounts - accounts that have no user row at all, so their sessions cannot be
   /// placed in the table that requires a registered owner.
   /// </summary>
   internal sealed class SystemSessionStore(ApiCoreContext ctx) : ISessionStore
   {
      public Task AddAsync(string sessionId, string accountId, string hash, DateTime expiry) {
         ctx.ta_SystemSessions.Add(NewRow(sessionId, accountId, hash, expiry));
         return ctx.SaveChangesAsync();
      }

      public async Task<SessionRecord?> FindByHashAsync(string hash) {
         var row = await ctx.ta_SystemSessions.SingleOrDefaultAsync(r => r.cSystemSessionHash == hash);
         return row is null ? null : ToRecord(row);
      }

      public async Task<SessionRecord?> FindByIdAsync(string sessionId) {
         var row = await ctx.ta_SystemSessions.AsNoTracking()
            .SingleOrDefaultAsync(r => r.cSystemSessionId == sessionId);
         return row is null ? null : ToRecord(row);
      }

      public async Task<bool> SetStateAsync(string sessionId, SessionState state) {
         // Tracked on purpose: the row read here is edited in place and written by the save
         // below, and reads are no-tracking by default.
         var row = await ctx.ta_SystemSessions.AsTracking()
            .SingleOrDefaultAsync(r => r.cSystemSessionId == sessionId);
         if (row is null) return false;

         row.cSystemSessionState = state;
         row.ustamp = DateTime.UtcNow;
         await ctx.SaveChangesAsync();
         return true;
      }

      public async Task RotateAsync(string oldSessionId, string newSessionId, string accountId, string hash,
         DateTime expiry) {
         // Tracked for the same reason as above: the old row is revoked by editing it here.
         var old = await ctx.ta_SystemSessions.AsTracking()
            .SingleOrDefaultAsync(r => r.cSystemSessionId == oldSessionId);
         if (old is not null) {
            old.cSystemSessionState = SessionState.Revoked;
            old.ustamp = DateTime.UtcNow;
         }

         ctx.ta_SystemSessions.Add(NewRow(newSessionId, accountId, hash, expiry));
         await ctx.SaveChangesAsync();
      }

      public async Task RevokeAllAsync(string accountId) {
         var now = DateTime.UtcNow;
         await ctx.ta_SystemSessions
            .Where(r => r.cSystemSessionAccountId == accountId && r.cSystemSessionState == SessionState.Active)
            .ExecuteUpdateAsync(s => s
               .SetProperty(r => r.cSystemSessionState, SessionState.Revoked)
               .SetProperty(r => r.ustamp, now));
      }

      public async Task<SessionRecord[]> ListByAccountAsync(string accountId) {
         var rows = await ctx.ta_SystemSessions
            .AsNoTracking()
            .Where(r => r.cSystemSessionAccountId == accountId)
            .OrderByDescending(r => r.datestamp)
            .ToArrayAsync();
         return [.. rows.Select(ToRecord)];
      }

      public async Task PurgeAsync(TimeSpan retention) {
         var now = DateTime.UtcNow;

         await ctx.ta_SystemSessions
            .Where(r => r.cSystemSessionState == SessionState.Active && r.cSystemSessionExpiry <= now)
            .ExecuteUpdateAsync(s => s
               .SetProperty(r => r.cSystemSessionState, SessionState.Expired)
               .SetProperty(r => r.ustamp, now));

         var cutoff = now - retention;
         await ctx.ta_SystemSessions
            .Where(r => r.cSystemSessionExpiry <= cutoff)
            .ExecuteDeleteAsync();
      }

      private static ta_SystemSession NewRow(string sessionId, string accountId, string hash, DateTime expiry) {
         var now = DateTime.UtcNow;
         return new ta_SystemSession {
            cSystemSessionId = sessionId,
            cSystemSessionAccountId = accountId,
            cSystemSessionHash = hash,
            cSystemSessionState = SessionState.Active,
            cSystemSessionExpiry = expiry,
            ustamp = now,
            datestamp = now,
            json_object = null
         };
      }

      private static SessionRecord ToRecord(ta_SystemSession row) =>
         new(row.cSystemSessionId, row.cSystemSessionAccountId, row.cSystemSessionState, row.cSystemSessionExpiry,
            row.datestamp, row.ustamp);
   }
}
