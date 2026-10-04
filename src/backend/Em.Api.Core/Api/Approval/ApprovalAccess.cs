using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Pemeriksa hak approval: siapa yang boleh melihat sebuah request, siapa yang boleh memutuskan
   /// langkahnya, dan siapa yang sama sekali tidak boleh menandatangani apa pun.
   /// </summary>
   /// <remarks>
   /// Dipisah dari service approval supaya aturannya ditulis sekali dan dipakai setiap action yang
   /// membutuhkannya - daftar, rincian, PDF, keputusan, penarikan, komentar.
   /// <para>
   /// Approval adalah pengecualian dari aturan "administrator bisa semua" dalam satu hal: akun
   /// administrator bawaan dan akun debugger <b>tidak bisa menandatangani</b>, karena tanda tangan harus
   /// menunjuk orang yang benar-benar ada di daftar user. Pengembang mengujinya dengan berpindah menjadi
   /// user nyata. Sebaliknya, user nyata yang saklar administratornya menyala tetap boleh - saklar itu
   /// berlaku seperti memegang semua hak langkah.
   /// </para>
   /// </remarks>
   public class ApprovalAccess(ApiCoreContext ctx)
   {
      /// <summary>
      /// Memastikan pemanggil boleh membubuhkan tanda tangan, lalu mengembalikan id-nya.
      /// </summary>
      /// <param name="request">Keterangan request yang sedang dikerjakan.</param>
      /// <param name="cancellationToken">Token pembatalan.</param>
      /// <returns>Id user pemanggil.</returns>
      /// <exception cref="ActionException">
      /// 401 kalau tidak ada pemanggil yang terbukti; 403 kalau pemanggilnya akun sistem - akun
      /// administrator bawaan atau akun debugger - atau kalau user-nya tidak ada maupun tidak aktif lagi.
      /// </exception>
      public async Task<string> RequireRealUserAsync(ActionRequest request,
         CancellationToken cancellationToken = default) {
         var userId = request.RequireUserId();

         // The two system accounts carry ids that are deliberately not valid row ids, so there is no
         // row for them to point at - which is also what makes the database refuse their signature.
         // Saying so here turns that refusal into an answer the caller can read.
         if (userId is Defaults.AdminUserId or Defaults.DebuggerUserId) {
            throw new ActionException(
               "A built-in system account cannot take part in an approval, because a signature has to name a real user. " +
               "Switch to a real user first.", 403);
         }

         var isActive = await ctx.ta_Users
            .Where(r => r.cUserId == userId && r.cUserState == UserState.Active)
            .AnyAsync(cancellationToken);

         if (!isActive) {
            throw new ActionException("The calling user is not an active user, so it cannot take part in an approval.",
               403);
         }

         return userId;
      }

      /// <summary>
      /// Apakah pemanggil memegang sebuah hak. Urutannya sama dengan pemeriksaan hak di gerbang - jalur
      /// pengembang, lalu saklar administrator, baru hak yang diberikan - supaya satu aturan tidak
      /// diperiksa dengan dua cara berbeda.
      /// </summary>
      /// <param name="request">Keterangan request yang sedang dikerjakan.</param>
      /// <param name="claim">Hak yang diperiksa.</param>
      public static bool HoldsClaim(ActionRequest request, ClaimAction claim) =>
         request.IsDebugRequest || request.IsAdmin ||
         request.Claims.Any(r => string.Equals(r.Key, claim.Key, StringComparison.OrdinalIgnoreCase));

      /// <summary>
      /// Apakah pemanggil boleh melihat request sebuah jenis dokumen: pemegang hak langkah mana pun di
      /// alurnya - termasuk pengaju, yang memegang hak langkah pertama - pemegang hak pembaca jenis
      /// dokumen itu, dan user yang saklar administratornya menyala.
      /// </summary>
      /// <param name="request">Keterangan request yang sedang dikerjakan.</param>
      /// <param name="flow">Alur jenis dokumen yang dilihat.</param>
      public static bool CanView(ActionRequest request, ApprovalFlowDeclaration flow) =>
         flow.Claims.Any(r => HoldsClaim(request, r));

      /// <summary>
      /// Memastikan pemanggil boleh melihat request sebuah jenis dokumen. Hak berkomentar sama dengan hak
      /// melihat, jadi pemeriksaan ini juga yang dipakai sebelum komentar ditulis.
      /// </summary>
      /// <param name="request">Keterangan request yang sedang dikerjakan.</param>
      /// <param name="flow">Alur jenis dokumen yang dilihat.</param>
      /// <exception cref="ActionException">
      /// 401 kalau tidak ada pemanggil yang terbukti; 403 kalau ia tidak memegang satu pun hak di alur itu.
      /// </exception>
      public static void RequireView(ActionRequest request, ApprovalFlowDeclaration flow) {
         request.RequireUserId();

         if (!CanView(request, flow)) {
            throw new ActionException(
               $"You are not allowed to see approval requests of document type '{flow.DocType}'.", 403);
         }
      }

      /// <summary>
      /// Seluruh jenis dokumen yang boleh dilihat pemanggil. Dipakai daftar request, supaya penyaringnya
      /// dibatasi di server - bukan dengan membaca semuanya lalu membuang yang tidak boleh dilihat.
      /// </summary>
      /// <param name="request">Keterangan request yang sedang dikerjakan.</param>
      /// <param name="registry">Katalog seluruh alur yang terdaftar.</param>
      public static IReadOnlyList<string> ViewableDocTypes(ActionRequest request, ApprovalRegistry registry) =>
         [.. registry.Flows.Where(r => CanView(request, r)).Select(r => r.DocType)];
   }
}
