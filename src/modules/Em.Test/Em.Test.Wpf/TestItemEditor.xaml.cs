using System.ComponentModel;
using System.Windows;
using Em.Test.Models;
using Em.Test.Models.Ui;
using Em.Ui.Core.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Test.Wpf
{
   /// <summary>
   /// The editor of one test item. It tests an Editor screen opened with a payload, change tracking through
   /// UiModel, refusing to leave the screen while there are still changes, and submitting changes through
   /// approval.
   /// </summary>
   public partial class TestItemEditor : UserControl, INavigationBody
   {
      private readonly Em.Ui.Wpf.Core.EmApp _app;

      public TestItemEditor(Em.Ui.Wpf.Core.EmApp app) {
         _app = app;
         InitializeComponent();
      }

      public TestItemEditorVm Vm => (TestItemEditorVm)DataContext;

      // OnNavigatingIn is also raised on back and forward, which re-enter a form that may be half filled in,
      // so only the payload is taken here; opening the record is the job of OnReloadRequested.
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) {
         if (args.Data is not TestItemEditorPayload payload) {
            args.Cancel = true;
            args.Message = "The item editor can only be opened with an item payload.";
            return Task.CompletedTask;
         }

         Vm.Payload = payload;
         return Task.CompletedTask;
      }

      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) {
         if (!Vm.HasUnsavedChanges) return Task.CompletedTask;

         var answer = (Vm.DialogOwner ?? _app.MainWindow).ShowMboxDecideWarning(
            "This item has changes that have not been saved. Leave and lose them?", "Unsaved changes");
         if (answer == MessageBoxResult.Yes) return Task.CompletedTask;

         args.Cancel = true;
         args.Message = "The item still has unsaved changes.";
         return Task.CompletedTask;
      }

      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) {
         if (args.Data is TestItemEditorPayload payload) Vm.Payload = payload;
         return Vm.LoadAsync();
      }

      public Task OnRelease(INavigation sender) {
         Vm.Release();
         return Task.CompletedTask;
      }
   }

   public class TestItemEditorVm : TestVmBase
   {
      public TestItemEditorVm() {
         RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
         RegisterCommand(nameof(DiscardCommand), DiscardCommand, DiscardCommandAllowed);
         RegisterCommand(nameof(ReloadServerCommand), ReloadServerCommand, ReloadServerCommandAllowed);
         RegisterCommand(nameof(ProposeChangeCommand), ProposeChangeCommand, ProposeChangeCommandAllowed);
         RegisterCommand(nameof(CloseCommand), CloseCommand);
      }

      public TestItemEditorPayload? Payload { get; set; }

      public IReadOnlyList<TestItemState> States { get; } = Enum.GetValues<TestItemState>();

      public TestItem? Item {
         get => Get<TestItem?>();
         private set => Set(value, _ => {
            NotifyChanged(nameof(HasItem));
            NotifyChanged(nameof(HasUnsavedChanges));
            NotifyChanged(nameof(Heading));
            NotifyChanged(nameof(Subheading));
         });
      }

      public bool HasItem => Item is not null;

      public bool HasUnsavedChanges => Item?.IsDirty == true;

      public string Heading => Item is null ? "Test item"
         : Item.IsBlank ? "New test item"
         : $"{Item.cTestItemCode} - {Item.cTestItemName}";

      public string Subheading => Item is null ? ""
         : Item.IsBlank ? "Not saved yet. The row is created when you save."
         : $"Id {Item.cTestItemId}. Last changed {Item.ustamp.ToLocalTime():yyyy-MM-dd HH:mm:ss}.";

      #region Loading

      public async Task LoadAsync() {
         if (EmApp is null) return;

         await RunBusyAsync("Opening the item...", async () => {
            Detach();
            if (Payload?.ItemId is null) {
               Item = TestItem.CreateNew(EmApp);
            }
            else {
               Item = await TestItem.GetByIdAsync(EmApp, Payload.ItemId)
                      ?? throw new InvalidOperationException("The item no longer exists on the server.");
            }

            Attach();
         });
      }

      public void Release() => Detach();

      private void Attach() {
         if (Item is not null) Item.PropertyChanged += ItemOnPropertyChanged;
      }

      private void Detach() {
         if (Item is not null) Item.PropertyChanged -= ItemOnPropertyChanged;
      }

      private void ItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (e.PropertyName is nameof(TestItem.IsDirty) or nameof(TestItem.IsBlank)) {
            NotifyChanged(nameof(HasUnsavedChanges));
            NotifyChanged(nameof(Heading));
            NotifyChanged(nameof(Subheading));
            RaiseCommandsChanged();
         }
      }

      #endregion

      #region Commands

      public Task SaveCommand() =>
         RunBusyAsync("Saving...", async () => {
            var wasNew = Item!.IsBlank;
            await Item.SaveAsync();
            Log("Save", wasNew ? "The item was created." : "The item was updated.");
            NotifyChanged(nameof(Heading));
            NotifyChanged(nameof(Subheading));
            if (wasNew) NavigationEntry?.SetTitle($"Test item {Item.cTestItemCode}");
         });

      public bool SaveCommandAllowed() => IsNotBusy && Item is { IsDirty: true } && Holds(ITestServices.EditItemsClaim);

      public Task DiscardCommand() {
         Item!.RollBack();
         Log("Discard", "Every field went back to the values it was opened with.");
         return Task.CompletedTask;
      }

      public bool DiscardCommandAllowed() => IsNotBusy && Item is { IsDirty: true };

      public Task ReloadServerCommand() =>
         RunBusyAsync("Reloading...", async () => {
            await Item!.ResetAsync();
            Log("Reload", "The item was read again from the server; local edits were replaced.");
         });

      public bool ReloadServerCommandAllowed() => IsNotBusy && Item is { IsBlank: false };

      public Task ProposeChangeCommand() =>
         RunBusyAsync("Submitting the proposal...", async () => {
            var item = Item!;
            var result = await Service.PostGetMeta_TestItemSubmitChange(new TestItemChange {
               Operation = item.IsBlank ? TestItemOperation.Create : TestItemOperation.Update,
               ItemId = item.IsBlank ? null : item.cTestItemId,
               Code = item.cTestItemCode,
               Name = item.cTestItemName,
               Qty = item.cTestItemQty,
               Price = item.cTestItemPrice,
               Note = string.IsNullOrWhiteSpace(item.cTestItemNote) ? null : item.cTestItemNote
            });
            Log("Propose change (data approval)",
               result.AppliedImmediately
                  ? $"You hold '{ITestServices.ApproveItemClaim}', so the change was applied at once (request {result.ApprovalRequestId}). Use Reload from server to see it."
                  : $"Request {result.ApprovalRequestId} is waiting in Approval Manager for someone who holds '{ITestServices.ApproveItemClaim}'.");
         });

      public bool ProposeChangeCommandAllowed() => IsNotBusy && Item is not null;

      public async Task CloseCommand() {
         if (NavigationEntry is not null) await NavigationEntry.Close();
      }

      #endregion

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
   }
}
