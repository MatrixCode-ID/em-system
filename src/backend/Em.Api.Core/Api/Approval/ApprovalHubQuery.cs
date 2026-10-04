using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Jawaban <see cref="IApprovalHubQuery"/>: aturan "menunggu pemanggil" dipakai bersama dengan daftar
   /// request, jadi layar approval dan daftar pekerjaan tidak bisa berselisih tentang apa yang menunggu.
   /// </summary>
   internal sealed class ApprovalHubQuery(ApiCoreContext ctx, ApprovalRegistry registry, ActionRequest caller)
      : IApprovalHubQuery
   {
      /// <inheritdoc />
      public Task<IReadOnlyList<ApprovalHubGroup>> GetWaitingForCallerAsync(
         CancellationToken cancellationToken = default) =>
         new ApprovalRequestReader(ctx, registry, caller, cancellationToken).HubGroupsAsync();
   }
}
