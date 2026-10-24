using System.Collections;
using Em.Api.Core.Models;
using Em.Ui.Wpf.Dialogs;
namespace Em.Ui.Wpf.Navigations;
public sealed partial class ApprovalManagerVm {
   /// <summary>Invalidates commands after the view changes its live multi-selection.</summary>
   public void RefreshSelection() { foreach (var command in Commands) command.RaiseCanExecuteChanged(); }
   private ApprovalCapability[] _batchCapabilities = [];
   /// <summary>Whether the selected definition allows server-side batch signing.</summary>
   public bool BatchSigningAvailable => _batchCapabilities.Any(x => x.Kind == ApprovalKind.System && x.AllowBatchSign && (SelectedDefinition?.Kind is null || (SelectedDefinition.Kind == x.Kind && SelectedDefinition.Name == x.Name)));
   private void SetBatchCapabilities(ApprovalCapability[] capabilities) { _batchCapabilities = capabilities; NotifyChanged(nameof(BatchSigningAvailable)); }
   private bool SignSelectionAllowed(IList rows) => BatchSigningAvailable && BatchSelectionAllowed(rows);
   private bool SignAllAllowed() => BatchSigningAvailable && BatchAllAllowed();
   private void RegisterBatchCommands() {
      RegisterCommand<IList>(nameof(SignSelectedCommand), SignSelectedCommand, SignSelectionAllowed);
      RegisterCommand<IList>(nameof(VoidSelectedCommand), VoidSelectedCommand, BatchSelectionAllowed);
      RegisterCommand(nameof(SignAllCommand), SignAllCommand, SignAllAllowed);
      RegisterCommand(nameof(VoidAllCommand), VoidAllCommand, BatchAllAllowed);
   }
   private bool BatchSelectionAllowed(IList rows) => CanBatchAct && !IsBusy && rows?.Cast<object>().OfType<ta_ApprovalRequest>().Any(x => x.cApprovalRequestKind == ApprovalKind.System) == true;
   private bool CanBatchAct => EmApp?.ActiveUser?.cUserId is not (null or Em.Defaults.AdminUserId or Em.Defaults.DebuggerUserId);
   private bool BatchAllAllowed() => CanBatchAct && !IsBusy && Rows.Any(x => x.cApprovalRequestKind == ApprovalKind.System);
   /// <summary>Signs the selected System requests at their captured active level.</summary>
   public Task SignSelectedCommand(IList rows) => BatchAsync(rows.Cast<ta_ApprovalRequest>().ToArray(), false);
   /// <summary>Voids the selected System requests according to server permissions.</summary>
   public Task VoidSelectedCommand(IList rows) => BatchAsync(rows.Cast<ta_ApprovalRequest>().ToArray(), true);
   /// <summary>Signs System requests matching the current manager filters.</summary>
   public Task SignAllCommand() => BatchAsync(null, false);
   /// <summary>Voids System requests matching the current manager filters.</summary>
   public Task VoidAllCommand() => BatchAsync(null, true);
   private async Task BatchAsync(ta_ApprovalRequest[]? rows, bool isVoid) {
      if (IsBusy) return;
      if (rows is null) {
         var collected = new List<ta_ApprovalRequest>();
         await RunAsync(async () => {
            for (var page = 1; ; page++) {
               var result = await Service.PostGetMeta_ApprovalRequests(new(SelectedDefinition?.Kind, SelectedDefinition?.Name, _payload.SubjectKey, Stage, WaitingForMe, Search, page, 200));
               collected.AddRange(result.Items);
               if (collected.Count >= result.Total || result.Items.Length == 0) break;
            }
         });
         if (!string.IsNullOrEmpty(Error)) return;
         rows = collected.ToArray();
      }
      var ids = rows.Where(x => x.cApprovalRequestKind == ApprovalKind.System).Select(x => x.cApprovalRequestId).Distinct().ToArray();
      if (ids.Length == 0) return;
      var prompt = new ApprovalDecisionDialog(isVoid ? "Void all" : "Sign all", $"{ids.Length} System requests. Each request is processed independently; newly opened levels are excluded.", isVoid) { Owner = DialogOwner };
      if (prompt.ShowDialog() != true) return;
      string message = string.Empty;
      await RunAsync(async () => {
         var items = new List<ApprovalBatchItem>();
         foreach (var chunk in ids.Chunk(200)) {
            var result = isVoid ? await Service.PostGetMeta_ApprovalSystemVoidAll(new(chunk, prompt.Vm.Note)) :
               await Service.PostGetMeta_ApprovalSystemSignAll(new(null, chunk, ApprovalSignAllScope.AllActiveSteps, prompt.Vm.Note));
            items.AddRange(result.Items);
         }
         message = $"Succeeded: {items.Count(x => x.Success)}; skipped/failed: {items.Count(x => !x.Success)}\n" + string.Join("\n", items.Where(x => !x.Success).Select(x => $"{x.RequestId}: {x.Message}"));
      });
      if (message.Length == 0 && Error.Length > 0) message = Error;
      await RefreshAsync();
      if (message.Length > 0) DialogOwner?.ShowMboxInfo(message, "Batch approval result");
   }
}
