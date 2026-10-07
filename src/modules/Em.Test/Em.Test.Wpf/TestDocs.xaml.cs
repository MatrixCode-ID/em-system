using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using Em.Test.Models;
using Em.Ui.Core.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Test.Wpf
{
   /// <summary>
   /// The test documents screen: creating a draft, submitting it through document approval, viewing its base
   /// PDF in the built-in viewer, and opening the Approval Manager for that document. Also opened from the
   /// Source document button in the Approval Manager and from the hub, with the document key as its
   /// parameter.
   /// </summary>
   public partial class TestDocs : UserControl, INavigationBody
   {
      public TestDocs() {
         InitializeComponent();
         Loaded += async (_, _) => await Vm.RefreshIfStaleAsync();
      }

      public TestDocsVm Vm => (TestDocsVm)DataContext;

      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) {
         Vm.FocusOn(args.Data as string);
         return Task.CompletedTask;
      }

      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) {
         Vm.FocusOn(args.Data as string);
         return Vm.ReloadAsync();
      }

      public Task OnRelease(INavigation sender) => Task.CompletedTask;
   }

   public class TestDocsVm : TestVmBase
   {
      private const int PageSize = 50;
      private bool _loaded;
      private string? _focusId;

      public TestDocsVm() {
         RegisterCommand(nameof(CreateCommand), CreateCommand, CreateCommandAllowed);
         RegisterCommand(nameof(SubmitCommand), SubmitCommand, SubmitCommandAllowed);
         RegisterCommand(nameof(ViewSourcePdfCommand), ViewSourcePdfCommand, SelectedCommandAllowed);
         RegisterCommand(nameof(OpenApprovalCommand), OpenApprovalCommand, SelectedCommandAllowed);
         RegisterCommand(nameof(OpenAllApprovalsCommand), OpenAllApprovalsCommand);
         RegisterCommand<string?>(nameof(SetAmountCommand), SetAmountCommand, SetAmountCommandAllowed);
         RegisterCommand(nameof(DeleteCommand), DeleteCommand, DeleteCommandAllowed);
         RegisterCommand(nameof(RefreshCommand), RefreshCommand, SelectedOrIdleAllowed);
      }

      #region Data

      public ObservableCollection<vi_TestDoc> Items { get; } = [];

      public vi_TestDoc? Selected {
         get => Get<vi_TestDoc?>();
         set => Set(value, _ => { NotifyChanged(nameof(SelectionText)); RaiseCommandsChanged(); });
      }

      public bool IsEmpty => _loaded && Items.Count == 0;

      public string NewNo {
         get => Get($"TD-{DateTime.Now:yyyyMMdd-HHmmss}") ?? string.Empty;
         set => Set(value);
      }

      public string NewTitle {
         get => Get("Sample purchase order") ?? string.Empty;
         set => Set(value);
      }

      public decimal NewAmount {
         get => Get(2500000m);
         set => Set(value);
      }

      public string SelectionText => Selected is null
         ? "Select a document to submit it, view its PDF, or open its approval."
         : $"{Selected.cTestDocNo} - {Selected.cTestDocTitle}  |  amount {Selected.cTestDocAmount:N2}  |  status {Selected.cTestDocStatus}" +
           (Selected.cTestDocQaPassed is { } passed
              ? $"  |  QA {(passed ? "passed" : "did not pass")}{(string.IsNullOrWhiteSpace(Selected.cTestDocQaRemarks) ? "" : $" ('{Selected.cTestDocQaRemarks}')")}"
              : "");

      #endregion

      #region Loading

      // The key arrives from Approval Manager's Source document button as the canonical key: a JSON array
      // whose first part is the document id.
      public void FocusOn(string? canonicalKey) {
         if (string.IsNullOrWhiteSpace(canonicalKey)) return;
         try {
            _focusId = JsonSerializer.Deserialize<string[]>(canonicalKey)?.FirstOrDefault();
         }
         catch (JsonException) {
            _focusId = canonicalKey;
         }

         if (_loaded) SelectFocused();
      }

      private void SelectFocused() {
         if (_focusId is null) return;
         if (Items.FirstOrDefault(r => r.cTestDocId == _focusId) is { } row) {
            Selected = row;
            _focusId = null;
         }
      }

      public Task ReloadAsync() => RunBusyAsync("Loading documents...", ReadAsync);

      public async Task RefreshIfStaleAsync() {
         if (!_loaded || IsBusy || EmApp is null) return;
         try {
            await ReadAsync();
         }
         catch (Exception x) {
            Log("Refresh", Describe(x), true);
         }
      }

      private async Task ReadAsync() {
         var keep = Selected?.cTestDocId;
         var rows = await Service.GetVi_TestDocs_InPage(1, PageSize);
         Items.Clear();
         foreach (var row in rows) Items.Add(row);
         _loaded = true;
         NotifyChanged(nameof(IsEmpty));
         Selected = Items.FirstOrDefault(r => r.cTestDocId == keep);
         SelectFocused();
         RaiseCommandsChanged();
      }

      private async Task RunBusyAsync(string waiterText, Func<Task> work) {
         if (EmApp is null || IsBusy) return;

         try {
            WaiterText = waiterText;
            IsBusy = InWaiting = true;
            RaiseCommandsChanged();
            await work();
         }
         catch (Exception x) {
            Log(waiterText, Describe(x), true);
         }
         finally {
            InWaiting = IsBusy = false;
            RaiseCommandsChanged();
         }
      }

      #endregion

      #region Commands

      public Task CreateCommand() =>
         RunBusyAsync("Creating...", async () => {
            var now = DateTime.UtcNow;
            try {
               await Service.PostTa_TestDoc_New(new ta_TestDoc {
                  cTestDocNo = NewNo,
                  cTestDocTitle = NewTitle,
                  cTestDocAmount = NewAmount,
                  ustamp = now,
                  datestamp = now
               });
               Log("Create draft", $"{NewNo} was created as a draft.");
               NewNo = $"TD-{DateTime.Now:yyyyMMdd-HHmmss}";
            }
            catch (Exception x) {
               Log("Create draft", Describe(x), true);
            }

            await ReadAsync();
         });

      public bool CreateCommandAllowed() => IsNotBusy && Holds(ITestServices.EditItemsClaim);

      public Task SubmitCommand() =>
         RunBusyAsync("Submitting...", async () => {
            var doc = Selected!;
            try {
               var requestId = await Service.PostGetMeta_TestDocSubmit(doc.cTestDocId, "Submitted from the Em Test module");
               Log("Submit for approval", $"Request {requestId} was created for {doc.cTestDocNo}. Open it in Approval Manager to sign the steps.");
            }
            catch (Exception x) {
               Log("Submit for approval", Describe(x), true);
            }

            await ReadAsync();
         });

      public bool SubmitCommandAllowed() => IsNotBusy && Selected is { cTestDocStatus: TestDocStatus.Draft };

      public async Task ViewSourcePdfCommand() {
         if (NavigationEntry is null || Selected is not { } doc) return;

         // The viewer takes a loader and calls it itself, on open and on every Refresh, and closes the
         // stream it gets. The right to read the document is checked by the action, not by the viewer.
         var opened = await NavigationEntry.ViewPdf($"Source {doc.cTestDocNo}",
            _ => Service.GetMeta_TestDocPdf(doc.cTestDocId), $"{doc.cTestDocNo}.pdf");
         if (!opened) Log("Source PDF", "The viewer could not be opened.", true);
      }

      public async Task OpenApprovalCommand() {
         if (NavigationEntry is null || Selected is not { } doc) return;

         var opened = await NavigationEntry.NavigateTo(ApprovalManagerNavigationPayload.NavigationName,
            new ApprovalManagerNavigationPayload {
               DocType = ITestServices.DocType,
               DocKey = JsonSerializer.Serialize(new[] { doc.cTestDocId }),
               WaitingForMeOnly = false
            });
         if (!opened) Log("Approval Manager", "It could not be opened (no access, or the current screen refused to leave).", true);
      }

      public async Task OpenAllApprovalsCommand() {
         if (NavigationEntry is null) return;

         var opened = await NavigationEntry.NavigateTo(ApprovalManagerNavigationPayload.NavigationName,
            new ApprovalManagerNavigationPayload { DocType = ITestServices.DocType, WaitingForMeOnly = false });
         if (!opened) Log("Approval Manager", "It could not be opened (no access, or the current screen refused to leave).", true);
      }

      public Task SetAmountCommand(string? amount) =>
         RunBusyAsync("Updating the amount...", async () => {
            var doc = Selected!;
            var value = decimal.Parse(amount ?? "0", CultureInfo.InvariantCulture);
            try {
               var entity = (await Service.GetTa_TestDoc_ById(doc.cTestDocId))
                            ?? throw new InvalidOperationException("The document no longer exists.");
               entity.cTestDocAmount = value;
               await Service.PostTa_TestDoc_Update(entity);
               Log("Update amount", $"{doc.cTestDocNo} now has amount {value:N2}.");
            }
            catch (Exception x) {
               // A document that is not a draft is expected to be refused with 409: that is the test.
               Log("Update amount", Describe(x), true);
            }

            await ReadAsync();
         });

      public bool SetAmountCommandAllowed(string? amount) =>
         IsNotBusy && Selected is not null && Holds(ITestServices.EditItemsClaim);

      public Task DeleteCommand() {
         if (Selected is not { } doc || DialogOwner is not { } owner) return Task.CompletedTask;
         if (owner.ShowMboxDecideWarning($"Delete document {doc.cTestDocNo}?", "Delete document") != MessageBoxResult.Yes) {
            return Task.CompletedTask;
         }

         return RunBusyAsync("Deleting...", async () => {
            try {
               await Service.PostTa_TestDoc_Delete(doc);
               Log("Delete document", $"{doc.cTestDocNo} was deleted.");
            }
            catch (Exception x) {
               Log("Delete document", Describe(x), true);
            }

            await ReadAsync();
         });
      }

      public bool DeleteCommandAllowed() => IsNotBusy && Selected is not null && Holds(ITestServices.DeleteItemsClaim);

      public Task RefreshCommand() => ReloadAsync();

      public bool SelectedCommandAllowed() => IsNotBusy && Selected is not null;

      public bool SelectedOrIdleAllowed() => IsNotBusy;

      #endregion
   }
}
