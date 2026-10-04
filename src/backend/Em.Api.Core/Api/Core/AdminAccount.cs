using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Api.Shared;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Segala hal tentang akun administrator bawaan yang tidak punya baris pengguna: saklarnya,
   /// passwordnya, dan penyemaian nilai awal keduanya. Dikumpulkan di satu tempat karena ketiga
   /// pemakainya - penerbit token, action kredensial, dan startup aplikasi - masuk lewat pintu yang
   /// berbeda-beda, dan hanya ini yang mereka semua butuhkan.
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
      /// Benar kalau nama akun yang diketik adalah nama akun administrator bawaan. Perbandingannya
      /// tidak membedakan huruf besar-kecil, sama seperti nama akun pengguna biasa.
      /// </summary>
      public static bool IsAdminAccount(string? cUserAccount) =>
         string.Equals(cUserAccount, Defaults.AdminUserAccount, StringComparison.OrdinalIgnoreCase);

      /// <summary>
      /// Benar kalau akun administrator bawaan sedang dibolehkan masuk. Saklarnya hanya bisa diubah
      /// langsung di database - tidak ada satu pun action yang menyentuhnya.
      /// </summary>
      public static async Task<bool> IsEnabledAsync(ApiCoreContext ctx) =>
         bool.TryParse(await ReadAsync(ctx, EnabledKey), out var enabled) && enabled;

      /// <summary>
      /// Mencocokkan password yang diketik dengan password akun administrator yang tersimpan.
      /// </summary>
      public static async Task<bool> IsPasswordValidAsync(ApiCoreContext ctx, IStringHasher hasher, string password) =>
         hasher.CompareHashValue(password, await GetPasswordHashAsync(ctx, hasher));

      /// <summary>
      /// Mengganti password akun administrator bawaan. Yang disimpan hanya hash-nya.
      /// </summary>
      /// <exception cref="ArgumentException">Password yang diberikan kosong.</exception>
      public static Task SetPasswordAsync(ApiCoreContext ctx, IStringHasher hasher, string newPassword) {
         if (string.IsNullOrEmpty(newPassword)) {
            throw new ArgumentException("Password must not be empty.", nameof(newPassword));
         }

         return WriteAsync(ctx, PasswordKey, hasher.HashValue(newPassword), PasswordDescription);
      }

      /// <summary>
      /// Mengisi nilai awal ketiga metadata akun administrator, masing-masing hanya kalau key-nya
      /// belum ada. Dijalankan sekali saat aplikasi start, sebelum request pertama dilayani, supaya
      /// database yang baru dibuat tetap bisa dimasuki.
      /// </summary>
      /// <param name="ctx">Context yang dipakai membaca dan menulis metadata.</param>
      /// <param name="hasher">Komponen hashing untuk menyimpan password bawaan.</param>
      /// <param name="firstTimePassword">Password bawaan yang disemai saat database masih kosong.</param>
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
