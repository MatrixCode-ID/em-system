using Em.Api.Core.Approval;
using Em.Api.Core.Models;

namespace Em.Api.Core.Hub
{
   /// <summary>
   /// Sumber daftar pekerjaan untuk approval: satu baris per jenis dokumen yang punya request menunggu
   /// keputusan user aktif, terpisah untuk approval dokumen dan approval data.
   /// </summary>
   /// <remarks>
   /// Setiap baris menawarkan satu aksi, membuka layar approval yang sudah tersaring ke jenis dokumen
   /// itu. Keputusannya sendiri tidak diambil di sini.
   /// </remarks>
   internal sealed class ApprovalHubTaskSource(IApprovalHubQuery query) : IHubTaskSource
   {
      /// <summary>Sumber untuk baris approval dokumen.</summary>
      public const string DocumentSource = "approval.document";

      /// <summary>Sumber untuk baris approval data.</summary>
      public const string DataSource = "approval.data";

      /// <summary>Nama aksi yang ditawarkan setiap baris.</summary>
      public const string OpenAction = "Open";

      // Has to equal ApprovalManagerNavigationPayload.NavigationName in Em.Ui.Core, which the server
      // cannot reference. The parameter that goes with it is the document type, as plain text.
      private const string ApprovalManagerNavigation = "em.approval.manager";

      /// <inheritdoc />
      public async Task<IReadOnlyList<HubTaskInfo>> GetTasksAsync(CancellationToken cancellationToken = default) {
         var groups = await query.GetWaitingForCallerAsync(cancellationToken);
         var now = DateTime.Now;

         // Request dates are written in the server's local time, so the age is measured against the same.
         return [.. groups.Select(group => new HubTaskInfo {
            Source = group.Kind == ApprovalKind.Data ? DataSource : DocumentSource,
            Id = group.DocType,
            Title = group.DocType,
            Count = group.Count,
            OldestAge = now > group.OldestRequestDate ? now - group.OldestRequestDate : TimeSpan.Zero,
            ActionName = OpenAction,
            NavigationTarget = ApprovalManagerNavigation,
            NavigationParameter = group.DocType
         })];
      }
   }
}
