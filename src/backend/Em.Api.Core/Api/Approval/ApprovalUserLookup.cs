using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Cara engine mencari user nyata dari identitas lain, dipakai modul saat menentukan penanda tangan
   /// sebuah langkah dari isi dokumennya.
   /// </summary>
   /// <remarks>
   /// Dokumen - terutama dokumen yang dibuat aplikasi lama - menyebut orang dengan nomor karyawan, bukan
   /// dengan id user. Pemetaannya ditulis sekali di sini, bukan di setiap modul, supaya seluruh alur
   /// menemukan orang yang sama dengan cara yang sama.
   /// </remarks>
   public class ApprovalUserLookup(ApiCoreContext ctx, CancellationToken cancellationToken) : IApprovalUserLookup
   {
      /// <inheritdoc />
      public async Task<string> ByEmployeeIdAsync(string employeeId) {
         if (string.IsNullOrWhiteSpace(employeeId)) {
            throw new ActionException(
               "The document does not say which employee has to sign this step, so the request cannot be submitted.",
               409);
         }

         var userIds = await (
            from emp in ctx.ta_Emps
            join user in ctx.ta_Users on emp.cContactId equals user.cContactId
            where emp.cEmpId == employeeId && user.cUserState == UserState.Active
            select user.cUserId).ToArrayAsync(cancellationToken);

         // Both failures are raised at submit rather than at signing time, and on purpose: the person who
         // can fix them is the one submitting, and leaving them for later would instead stop someone else's
         // decision halfway through the flow.
         if (userIds.Length == 0) {
            throw new ActionException(
               $"Employee '{employeeId}' has no active user, so there is nobody to sign this step. " +
               "Give that employee a user first, or have someone else prepare the document.", 409);
         }

         if (userIds.Length > 1) {
            throw new ActionException(
               $"Employee '{employeeId}' has more than one active user ({string.Join(", ", userIds)}), " +
               "so it is not clear who should sign this step. Leave only one of them active.", 409);
         }

         return userIds[0];
      }
   }
}
