namespace Em.Api.Core.Approval;
internal sealed class ApprovalHubQuery(IApprovalEngine engine) : IApprovalHubQuery {
   public Task<IReadOnlyList<ApprovalHubGroup>> GetWaitingForCallerAsync(CancellationToken cancellationToken = default) =>
      ((ApprovalEngine)engine).HubAsync(cancellationToken);
}
