using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using Em.Shared;
using Em.Test.Models;
using Em.Test.Models.Ui;
using Em.Ui.Core.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Test.Wpf
{
   /// <summary>Navigasi ke editor item: item yang dibuka, atau kosong untuk item baru.</summary>
   public sealed class TestItemEditorPayload : NavigationPayloadBase
   {
      private readonly string? _code;

      public TestItemEditorPayload(string? itemId, string? code = null) : base(null) {
         ItemId = itemId;
         _code = code;
         DataState = itemId is null ? DataState.NewData : DataState.EditData;
      }

      /// <summary>Id item yang dibuka, atau <c>null</c> untuk item baru.</summary>
      public string? ItemId { get; }

      // The title is the key of the entry: one tab per item, and one for the new-item form.
      public override string? Title => ItemId is null ? "New test item" : $"Test item {_code ?? ItemId}";
   }

   /// <summary>Daftar item uji: pencarian di server, paging, aksi massal, dan pintu ke editor.</summary>
   public partial class TestItems : UserControl, INavigationBody
   {
      public TestItems() {
         InitializeComponent();
         // Coming back from the editor does not raise a reload, but it does show this screen again.
         Loaded += async (_, _) => await Vm.RefreshIfStaleAsync();
      }

      public TestItemsVm Vm => (TestItemsVm)DataContext;

      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      public Task OnRelease(INavigation sender) => Task.CompletedTask;
   }

   public sealed record TestStateOption(TestItemState? Value, string Caption);

   public class TestItemsVm : TestVmBase
   {
      private bool _loaded;

      public TestItemsVm() {
         RegisterCommand(nameof(SearchCommand), SearchCommand, SearchCommandAllowed);
         RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
         RegisterCommand(nameof(PrevPageCommand), PrevPageCommand, PrevPageCommandAllowed);
         RegisterCommand(nameof(NextPageCommand), NextPageCommand, NextPageCommandAllowed);
         RegisterCommand(nameof(NewCommand), NewCommand, NewCommandAllowed);
         RegisterCommand<TestItem?>(nameof(EditCommand), EditCommand);
         RegisterCommand(nameof(SeedCommand), SeedCommand, SeedCommandAllowed);
         RegisterCommand(nameof(BatchAddCommand), BatchAddCommand, BatchAddCommandAllowed);
         RegisterCommand(nameof(BatchBumpCommand), BatchBumpCommand, BatchBumpCommandAllowed);
         RegisterCommand<IList?>(nameof(DeleteSelectedCommand), DeleteSelectedCommand, DeleteSelectedCommandAllowed);
         RegisterCommand<TestItem?>(nameof(ProposeDeleteCommand), ProposeDeleteCommand, ProposeDeleteCommandAllowed);
      }

      #region Data

      public ObservableCollection<TestItem> Items { get; } = [];

      public IReadOnlyList<TestStateOption> StateOptions { get; } = [
         new(null, "Any state"),
         new(TestItemState.Active, "Active"),
         new(TestItemState.Disabled, "Disabled")
      ];

      public IReadOnlyList<int> PageSizes { get; } = [5, 10, 25, 50];

      public string Search {
         get => Get(string.Empty) ?? string.Empty;
         set => Set(value);
      }

      public TestStateOption SelectedState {
         get => Get(StateOptions[0])!;
         set => Set(value);
      }

      public int PageSize {
         get => Get(10);
         set => Set(value, size => { if (_loaded) _ = GoToPageAsync(1); });
      }

      public int Page {
         get => Get(1);
         private set => Set(value, _ => NotifyChanged(nameof(PageText)));
      }

      public int Total {
         get => Get(0);
         private set => Set(value, _ => { NotifyChanged(nameof(PageText)); NotifyChanged(nameof(IsEmpty)); });
      }

      public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)Math.Max(1, PageSize)));

      public string PageText => $"Page {Page} of {PageCount} - {Total:N0} item(s)";

      public bool IsEmpty => _loaded && Items.Count == 0;

      #endregion

      #region Loading

      public Task ReloadAsync() => GoToPageAsync(1);

      /// <summary>Muat ulang diam-diam saat layar tampil lagi, mis. sesudah editor menyimpan.</summary>
      public async Task RefreshIfStaleAsync() {
         if (!_loaded || IsBusy || EmApp is null) return;
         try {
            await ReadPageAsync(Page);
         }
         catch (Exception x) {
            Log("Refresh", Describe(x), true);
         }
      }

      private async Task GoToPageAsync(int page) {
         await RunBusyAsync("Loading items...", () => ReadPageAsync(page));
      }

      private async Task ReadPageAsync(int page) {
         var query = new TestItemQuery {
            Page = Math.Max(1, page),
            PageSize = PageSize,
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            State = SelectedState.Value
         };

         var (items, total) = await TestItem.SearchAsync(EmApp!, query);
         var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
         if (query.Page > pageCount && total > 0) {
            // The last page emptied out under us (rows were deleted): show the new last one instead.
            (items, total) = await TestItem.SearchAsync(EmApp!, new TestItemQuery {
               Page = pageCount, PageSize = query.PageSize, Search = query.Search, State = query.State
            });
            query.Page = pageCount;
         }

         Items.Clear();
         foreach (var item in items) Items.Add(item);
         Total = total;
         Page = query.Page;
         _loaded = true;
         NotifyChanged(nameof(PageCount));
         NotifyChanged(nameof(PageText));
         NotifyChanged(nameof(IsEmpty));
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

      public Task SearchCommand() => GoToPageAsync(1);

      public bool SearchCommandAllowed() => IsNotBusy;

      public Task RefreshCommand() => GoToPageAsync(Page);

      public bool RefreshCommandAllowed() => IsNotBusy;

      public Task PrevPageCommand() => GoToPageAsync(Page - 1);

      public bool PrevPageCommandAllowed() => IsNotBusy && Page > 1;

      public Task NextPageCommand() => GoToPageAsync(Page + 1);

      public bool NextPageCommandAllowed() => IsNotBusy && Page < PageCount;

      public async Task NewCommand() {
         if (NavigationEntry is null) return;
         await NavigationEntry.NavigateTo("test.items.editor", new TestItemEditorPayload(null));
      }

      public bool NewCommandAllowed() => IsNotBusy && Holds(ITestServices.EditItemsClaim);

      public async Task EditCommand(TestItem? item) {
         if (NavigationEntry is null || item is null) return;
         await NavigationEntry.NavigateTo("test.items.editor",
            new TestItemEditorPayload(item.cTestItemId, item.cTestItemCode));
      }

      public Task SeedCommand() =>
         RunBusyAsync("Seeding...", async () => {
            var added = await Service.PostGetMeta_TestSeedItems();
            Log("Seed samples", $"{added} item(s) added (existing sample codes are left alone).");
            await ReadPageAsync(1);
         });

      public bool SeedCommandAllowed() => IsNotBusy && Holds(ITestServices.EditItemsClaim);

      public Task BatchAddCommand() =>
         RunBusyAsync("Adding a batch...", async () => {
            var stamp = DateTime.Now.ToString("HHmmss");
            var now = DateTime.UtcNow;
            var rows = Enumerable.Range(1, 3).Select(i => new ta_TestItem {
               cTestItemCode = $"BAT-{stamp}-{i}",
               cTestItemName = $"Batch item {i}",
               cTestItemQty = i,
               cTestItemPrice = 100m * i,
               cTestItemState = TestItemState.Active,
               ustamp = now,
               datestamp = now
            }).ToArray();
            try {
               await Service.PostTa_TestItem_NewBatch(rows);
               Log("Batch add", "3 items added in one request.");
            }
            catch (Exception x) {
               Log("Batch add", Describe(x), true);
            }

            await ReadPageAsync(1);
         });

      public bool BatchAddCommandAllowed() => IsNotBusy && Holds(ITestServices.EditItemsClaim);

      public Task BatchBumpCommand() =>
         RunBusyAsync("Updating a batch...", async () => {
            var rows = Items.Select(r => {
               var entity = r.ToEntity();
               entity.cTestItemQty += 1;
               return (ta_TestItem)entity;
            }).ToArray();
            try {
               await Service.PostTa_TestItem_UpdateBatch(rows);
               Log("Batch update", $"Quantity raised by one on {rows.Length} item(s) in one request.");
            }
            catch (Exception x) {
               Log("Batch update", Describe(x), true);
            }

            await ReadPageAsync(Page);
         });

      public bool BatchBumpCommandAllowed() => IsNotBusy && Items.Count > 0 && Holds(ITestServices.EditItemsClaim);

      public Task DeleteSelectedCommand(IList? selected) {
         var rows = selected?.OfType<TestItem>().ToArray() ?? [];
         if (rows.Length == 0 || DialogOwner is not { } owner) return Task.CompletedTask;
         if (owner.ShowMboxDecideWarning($"Delete {rows.Length} selected item(s)?", "Delete items") != MessageBoxResult.Yes) {
            return Task.CompletedTask;
         }

         return RunBusyAsync("Deleting...", async () => {
            try {
               var entities = rows.Select(r => (ta_TestItem)r.ToEntity()).ToArray();
               if (entities.Length == 1) await Service.PostTa_TestItem_Delete(entities[0]);
               else await Service.PostTa_TestItem_DeleteBatch(entities);
               Log("Delete", $"{entities.Length} item(s) deleted ({(entities.Length == 1 ? "single" : "batch")} action).");
            }
            catch (Exception x) {
               Log("Delete", Describe(x), true);
            }

            await ReadPageAsync(Page);
         });
      }

      public bool DeleteSelectedCommandAllowed(IList? selected) =>
         IsNotBusy && selected is { Count: > 0 } && Holds(ITestServices.DeleteItemsClaim);

      public Task ProposeDeleteCommand(TestItem? item) {
         if (item is null) return Task.CompletedTask;
         return RunBusyAsync("Submitting the proposal...", async () => {
            try {
               var result = await Service.PostGetMeta_TestItemSubmitChange(new TestItemChange {
                  Operation = TestItemOperation.Delete,
                  ItemId = item.cTestItemId
               });
               Log("Propose delete (data approval)",
                  result.AppliedImmediately
                     ? $"You hold '{ITestServices.ApproveItemClaim}', so the deletion was applied at once (request {result.ApprovalRequestId})."
                     : $"Request {result.ApprovalRequestId} is waiting for someone who holds '{ITestServices.ApproveItemClaim}'.");
            }
            catch (Exception x) {
               Log("Propose delete (data approval)", Describe(x), true);
            }

            await ReadPageAsync(Page);
         });
      }

      public bool ProposeDeleteCommandAllowed(TestItem? item) => IsNotBusy && item is not null;

      #endregion
   }
}
