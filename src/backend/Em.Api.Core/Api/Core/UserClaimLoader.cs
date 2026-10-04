using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Satu tempat untuk pertanyaan "hak apa saja yang berlaku untuk pengguna ini sekarang". Dipakai
   /// gerbang di <c>EmApp.ProcessRequest</c> maupun kedua action yang menampilkannya di layar, supaya
   /// aturan jendela waktunya - sejak kapan berlaku, sampai kapan, dan role yang sudah tidak aktif -
   /// hanya tertulis sekali. Ditulis dua kali, keduanya akan berbeda begitu salah satunya diperbaiki.
   /// </summary>
   internal static class UserClaimLoader
   {
      /// <summary>
      /// Hak yang diberikan langsung ke pengguna, yang masa berlakunya sedang jalan pada
      /// <paramref name="now"/>.
      /// </summary>
      /// <remarks>
      /// Tidak disaring terhadap katalog claim: pemberian yatim - hak yang sudah dihapus dari kode tapi
      /// barisnya tertinggal - harus tetap terlihat, karena hanya lewat situ ia bisa dikenali dan
      /// dicabut. Untuk gerbang hal itu tidak merugikan: hak yatim tidak cocok dengan action mana pun.
      /// </remarks>
      public static IQueryable<string> QueryDirectNames(ApiCoreContext ctx, string cUserId, DateTime now) =>
         ctx.ta_UserClaims
            .Where(r => r.cUserId == cUserId
                        && r.cUserClaimStart <= now
                        && r.cUserClaimExpiry >= now)
            .Select(r => r.cUserClaimName);

      /// <summary>
      /// Hak yang datang lewat role: hanya dari role yang aktif, dan hanya selama keanggotaannya sedang
      /// berjalan pada <paramref name="now"/>. Batas waktu keanggotaan yang kosong berarti tidak dibatasi
      /// dari sisi itu.
      /// </summary>
      public static IQueryable<string> QueryRoleNames(ApiCoreContext ctx, string cUserId, DateTime now) =>
         from assignment in ctx.ta_UserRoles
         join role in ctx.ta_Roles on assignment.cRoleId equals role.cRoleId
         join claim in ctx.ta_RoleClaims on role.cRoleId equals claim.cRoleId
         where assignment.cUserId == cUserId
               && role.cRoleState == RoleState.Active
               && (assignment.cUserRoleStart == null || assignment.cUserRoleStart <= now)
               && (assignment.cUserRoleExpiry == null || assignment.cUserRoleExpiry >= now)
         select claim.cClaimName;

      /// <summary>
      /// Gabungan keduanya dalam satu perjalanan ke database - bentuk inilah yang dibutuhkan gerbang,
      /// karena gerbang tidak peduli sebuah hak datang langsung atau lewat role.
      /// </summary>
      public static async Task<ClaimAction[]> LoadAsync(ApiCoreContext ctx, string cUserId, DateTime now,
         CancellationToken ct) {
         var names = await QueryDirectNames(ctx, cUserId, now)
            .Concat(QueryRoleNames(ctx, cUserId, now))
            .Distinct()
            .ToArrayAsync(ct);

         return [.. names.Select(ClaimAction.FromKey)];
      }
   }
}
