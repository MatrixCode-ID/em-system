using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Api.Shared;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Everything about the built-in administrator account that has no user row: its switch, its password,
   /// and seeding the initial values of both. Gathered in one place because its three users - the token
   /// issuer, the credential actions, and application startup - come in through different doors, and this
   /// is all they have in common.
   /// </summary>
   internal static class AdminAccount
   {
      // Which metadata key holds what is engine business and stays on this side of the wire: a
      // client that knew these names would be one step away from asking for their values.
      private const string PasswordKey = "AdminPassword";
      private const string DefaultPasswordKey = "DefaultAdminFirstPassword";
      private const string EnabledKey = "AdminUserEnable";

      private const string PasswordDescription = "Hashed password of the built-in administrator account.";
      private const string DefaultPasswordDescription =
         "Password the built-in administrator account falls back to when its own password is missing.";
      private const string EnabledDescription =
         "Whether the built-in administrator account may sign in. Changed directly in the database.";

      /// <summary>
      /// True when the typed account name is the built-in administrator account name. The comparison is
      /// case-insensitive, like ordinary user account names.
      /// </summary>
      public static bool IsAdminAccount(string? cUserAccount) =>
         string.Equals(cUserAccount, Defaults.AdminUserAccount, StringComparison.OrdinalIgnoreCase);

      /// <summary>
      /// True when the built-in administrator account is currently allowed to sign in. Its switch can only
      /// be changed directly in the database - no action touches it.
      /// </summary>
      public static async Task<bool> IsEnabledAsync(ApiCoreContext ctx) =>
         bool.TryParse(await ReadAsync(ctx, EnabledKey), out var enabled) && enabled;

      /// <summary>
      /// Matches the typed password against the stored administrator account password.
      /// </summary>
      public static async Task<bool> IsPasswordValidAsync(ApiCoreContext ctx, IStringHasher hasher, string password) =>
         hasher.CompareHashValue(password, await GetPasswordHashAsync(ctx, hasher));

      /// <summary>
      /// Changes the built-in administrator account password. Only its hash is stored.
      /// </summary>
      /// <exception cref="ArgumentException">The given password is empty.</exception>
      public static Task SetPasswordAsync(ApiCoreContext ctx, IStringHasher hasher, string newPassword) {
         if (string.IsNullOrEmpty(newPassword)) {
            throw new ArgumentException("Password must not be empty.", nameof(newPassword));
         }

         return WriteAsync(ctx, PasswordKey, hasher.HashValue(newPassword), PasswordDescription);
      }

      /// <summary>
      /// Fills the initial values of the three administrator account metadata entries, each only when its
      /// key does not exist yet. Run once when the application starts, before the first request is served, so
      /// a freshly created database can still be signed into.
      /// </summary>
      /// <param name="ctx">The context used to read and write the metadata.</param>
      /// <param name="hasher">The hashing component used to store the default password.</param>
      /// <param name="firstTimePassword">The default password seeded when the database is still empty.</param>
      public static async Task SeedAsync(ApiCoreContext ctx, IStringHasher hasher, string firstTimePassword) {
         var defaultPassword = await ReadAsync(ctx, DefaultPasswordKey);
         if (string.IsNullOrWhiteSpace(defaultPassword)) {
            // Only checked when it is about to be used. Once the default is on file the builder
            // value never matters again, and refusing to start over it would be refusing over
            // something that is no longer read.
            if (string.IsNullOrWhiteSpace(firstTimePassword)) {
               throw new InvalidOperationException(
                  $"'{nameof(EmAppBuilder.FirstTimeAdminPassword)}' must not be empty: there is no stored default administrator password to fall back on.");
            }

            defaultPassword = firstTimePassword;
            await WriteAsync(ctx, DefaultPasswordKey, defaultPassword, DefaultPasswordDescription);
         }

         if (string.IsNullOrWhiteSpace(await ReadAsync(ctx, PasswordKey))) {
            await WriteAsync(ctx, PasswordKey, hasher.HashValue(defaultPassword), PasswordDescription);
         }

         // Off unless somebody turns it on by hand. A first start that silently opened an account
         // with a password written in the source of the host application would be a back door.
         if (await ReadAsync(ctx, EnabledKey) is null) {
            await WriteAsync(ctx, EnabledKey, bool.FalseString, EnabledDescription);
         }
      }

      // The key may be deleted; its value may not be left empty. A stored hash that matches nothing
      // would shut the account out with no way back in short of editing the database by hand, so an
      // empty one is filled back in from the default password instead of being used as it is.
      private static async Task<string> GetPasswordHashAsync(ApiCoreContext ctx, IStringHasher hasher) {
         var hash = await ReadAsync(ctx, PasswordKey);
         if (!string.IsNullOrWhiteSpace(hash)) return hash;

         var defaultPassword = await ReadAsync(ctx, DefaultPasswordKey);
         if (string.IsNullOrWhiteSpace(defaultPassword)) {
            throw new InvalidOperationException(
               "The administrator password is missing and there is no default password to restore it from.");
         }

         hash = hasher.HashValue(defaultPassword);
         await WriteAsync(ctx, PasswordKey, hash, PasswordDescription);
         return hash;
      }

      private static async Task<string?> ReadAsync(ApiCoreContext ctx, string key) =>
         (await ctx.ta_Metas.AsNoTracking().SingleOrDefaultAsync(r => r.cMetaKey == key))?.cMetaValue;

      private static async Task WriteAsync(ApiCoreContext ctx, string key, string value, string description) {
         // Tracked on purpose: an existing row is updated by editing it in place below, and
         // reads are no-tracking by default.
         var meta = await ctx.ta_Metas.AsTracking().SingleOrDefaultAsync(r => r.cMetaKey == key);

         if (meta is null) {
            ctx.ta_Metas.Add(new ta_Meta {
               cMetaKey = key,
               cMetaValue = value,
               cMetaDescription = description,
               ustamp = DateTime.UtcNow
            });
         }
         else {
            meta.cMetaValue = value;
            meta.ustamp = DateTime.UtcNow;
         }

         await ctx.SaveChangesAsync();
      }
   }
}
