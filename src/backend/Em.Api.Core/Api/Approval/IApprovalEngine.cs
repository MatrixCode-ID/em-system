using Em.Api.Core.Models;
namespace Em.Api.Core.Approval;
/// <summary>Module entry point for deferred Data changes and server-built System requests.</summary>
public interface IApprovalEngine {
   /// <summary>Submits one Data tree, or applies it atomically for a caller who may decide it.</summary>
   Task<ApprovalSubmitResult> SubmitDataAsync(DataChange root, string? note = null, CancellationToken ct = default);
   /// <summary>Requests a System flow from its registered definition and current signer settings.</summary>
   Task<string> RequestAsync(string name, string subjectKey, string summary, CancellationToken ct = default);
   /// <summary>Voids a System request without altering its signature history.</summary>
   Task VoidAsync(string requestId, string reason, CancellationToken ct = default);
   /// <summary>Rebuilds the System flow and links a new request to the voided predecessor.</summary>
   Task<string> ReinstateAsync(string requestId, string summary, string reason, CancellationToken ct = default);
   /// <summary>Finds the System request occupying a subject for recovery after a module save fails.</summary>
   Task<ta_ApprovalRequest?> FindByKeyAsync(string name, string subjectKey, CancellationToken ct = default);
}
