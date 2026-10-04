using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Menggerakkan request maju setelah sebuah langkah selesai disetujui: kalau levelnya sudah lengkap,
   /// level itu ditutup lalu request pindah ke level berikutnya, atau selesai bila tidak ada lagi.
   /// </summary>
   /// <remarks>
   /// Dipakai pengajuan (tanda tangan otomatis pengaju bisa langsung melengkapi levelnya) dan keputusan,
   /// supaya aturan "kapan sebuah level lengkap" ditulis sekali. Semua hook-nya berjalan di dalam
   /// transaksi pemanggil; yang menjalankan hook sesudah commit adalah pemanggil juga.
   /// </remarks>
   internal static class ApprovalProgress
   {
      /// <summary>
      /// Memajukan request selama levelnya yang sedang berjalan sudah lengkap, lalu menyimpan keadaannya.
      /// </summary>
      /// <param name="ctx">Context database inti, yang sedang berada di dalam transaksi.</param>
      /// <param name="flow">Alur jenis dokumennya.</param>
      /// <param name="scope">Bahan untuk menyerahkan giliran ke handler modul.</param>
      /// <param name="request">Request yang dimajukan. Harus terlacak oleh <paramref name="ctx"/>.</param>
      /// <param name="steps">Seluruh langkah request itu. Harus terlacak oleh <paramref name="ctx"/>.</param>
      /// <returns><c>true</c> kalau request selesai seluruhnya karena pemajuan ini.</returns>
      public static async Task<bool> AdvanceAsync(ApiCoreContext ctx, ApprovalFlowDeclaration flow,
         ApprovalRunScope scope, ta_ApprovalRequest request, IReadOnlyList<ta_ApprovalRequestStep> steps) {
         var finished = false;

         while (true) {
            var level = request.cApprovalRequestLevel;

            // A level is complete when nothing in it is still waiting. A skipped step never waits, and a
            // level made only of skipped steps is never entered, because the next level is always picked
            // from the steps that are still waiting.
            if (steps.Any(r => r.cApprovalRequestStepLevel == level &&
                               r.cApprovalRequestStepStage == ApprovalStepStatus.Waiting)) {
               break;
            }

            await flow.RunLevelCompletedAsync(scope, level);

            var nextLevels = steps
               .Where(r => r.cApprovalRequestStepLevel > level &&
                           r.cApprovalRequestStepStage == ApprovalStepStatus.Waiting)
               .Select(r => r.cApprovalRequestStepLevel)
               .ToList();

            if (nextLevels.Count == 0) {
               request.cApprovalRequestStage = ApprovalStage.Approved;
               request.cApprovalRequestCompletedDate = DateTime.Now;
               await flow.RunFinishingAsync(scope);
               finished = true;
               break;
            }

            request.cApprovalRequestLevel = nextLevels.Min();
         }

         request.ustamp = DateTime.UtcNow;
         await ctx.SaveChangesAsync();
         return finished;
      }
   }
}
