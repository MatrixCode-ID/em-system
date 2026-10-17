using System.IO;
using System.Windows.Input;
using Em.Api.Core.Models;
namespace Em.Ui.Core.Shared;
/// <summary>Outcome of the owning module's decision check.</summary>
public sealed record ApprovalDecisionCheck(bool Ok, string? Message = null);
/// <summary>Module-owned extra command in the engine toolbar.</summary>
public sealed record ApprovalToolbarItem(string Title, ICommand Command);
/// <summary>The current server-authorized request and a way to refresh its engine shell.</summary>
public sealed record ApprovalSystemEditorContext(ApprovalRequestDetail State, IApprovalServices Services, Func<Task> RefreshAsync);
/// <summary>Platform-neutral contract implemented by the module's System approval view.</summary>
public interface IEmApprovalSystemEditor {
   /// <summary>Loads module-owned data into the editor.</summary>
   Task LoadAsync(ApprovalSystemEditorContext context, CancellationToken ct = default);
   /// <summary>Checks business input and, when needed, saves it before the engine signature.</summary>
   Task<ApprovalDecisionCheck> ValidateSignAsync(ta_ApprovalRequestStep step, CancellationToken ct = default);
   /// <summary>Checks rejection rules before the engine is called.</summary>
   Task<ApprovalDecisionCheck> ValidateRejectAsync(ta_ApprovalRequestStep step, CancellationToken ct = default);
   /// <summary>Optional preview stream owned by the engine shell.</summary>
   Task<Stream?> GetPreviewAsync(CancellationToken ct = default) => Task.FromResult<Stream?>(null);
   /// <summary>Whether this editor offers preview.</summary>
   bool HasPreview => false;
   /// <summary>Whether a source navigation is offered.</summary>
   bool HasSource => false;
   /// <summary>Opens the module-owned source screen.</summary>
   Task OpenSourceAsync() => Task.CompletedTask;
   /// <summary>Extra module toolbar commands.</summary>
   IReadOnlyList<ApprovalToolbarItem> ToolbarItems => [];
   /// <summary>Updates the module's link after reinstate; recover through FindByKey if saving fails.</summary>
   Task OnReinstatedAsync(string oldId, string newId, string key) => Task.CompletedTask;
}
/// <summary>Typed convenience methods over the server-built System flow.</summary>
public static class ApprovalSystemClient {
   /// <summary>Requests the definition's flow for an opaque module key.</summary>
   public static Task<string> RequestApprovalAsync<TDefinition>(this IApprovalServices services, object key, string summary)
      where TDefinition : IEmApprovalSystem => services.PostGetMeta_ApprovalSystemRequest(ApprovalSubjectKey.NameOf(typeof(TDefinition)), ApprovalSubjectKey.Serialize(key), summary);
   /// <summary>Finds the active or finished request for recovery.</summary>
   public static Task<ta_ApprovalRequest?> FindByKeyAsync<TDefinition>(this IApprovalServices services, object key)
      where TDefinition : IEmApprovalSystem => services.PostGetMeta_ApprovalSystemFindByKey(ApprovalSubjectKey.NameOf(typeof(TDefinition)), ApprovalSubjectKey.Serialize(key));
   /// <summary>Voids a request while preserving its signatures.</summary>
   public static Task VoidAsync(this IApprovalServices services, string id, string reason) => services.PostMeta_ApprovalSystemVoid(id, reason);
   /// <summary>Rebuilds a System request from current settings.</summary>
   public static Task<string> ReinstateAsync(this IApprovalServices services, string id, string summary, string reason) => services.PostGetMeta_ApprovalSystemReinstate(id, summary, reason);
}
