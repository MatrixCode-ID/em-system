using System.Collections.ObjectModel;
using System.Globalization;
using Em.Api.Core.Models;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Dialogs;

/// <summary>Reusable paged contact search using the contact service.</summary>
public partial class ContactPickerDialog : EmWindow
{
   /// <summary>Creates the dialog without loading records, for XAML and tests.</summary>
   public ContactPickerDialog() {
      InitializeComponent();
      Vm.MainWindow = this;
      Vm.RequestClose += result => DialogResult = result;
      Closed += (_, _) => Vm.Release();
   }
   /// <summary>Creates a contact picker with optional person filtering and caller-owned selection rules.</summary>
   public ContactPickerDialog(EmApp app, ContactSearchFilter? filter = null, Func<ContactSearchItem, string?>? selectionBlockReason = null) : this() {
      Vm.EmApp = app; Vm.Filter = filter ?? new(); Vm.SelectionBlockReason = selectionBlockReason;
      Vm.Service = app.ServiceProvider.GetRequiredService<IContactServices>();
      // The initial request belongs to the dialog lifecycle, before any user command runs.
      _ = Vm.SearchCommand();
   }
   /// <summary>Search state and the chosen contact.</summary>
   public ContactPickerDialogVm Vm => (ContactPickerDialogVm)DataContext;
}

/// <summary>Applies server searches and loads only one contact page at a time.</summary>
public sealed class ContactPickerDialogVm : MvvmModelBase
{
   private bool _released;
   private string _appliedSearch = "";
   /// <summary>Creates the search, paging and selection commands.</summary>
   public ContactPickerDialogVm() {
      RegisterCommand(nameof(SearchCommand), SearchCommand, SearchCommandAllowed);
      RegisterCommand(nameof(PreviousPageCommand), PreviousPageCommand, PreviousPageCommandAllowed);
      RegisterCommand(nameof(NextPageCommand), NextPageCommand, NextPageCommandAllowed);
      RegisterCommand(nameof(ChooseCommand), ChooseCommand, ChooseCommandAllowed);
   }
   /// <summary>Service using the current account and API connection.</summary>
   public IContactServices? Service { get; set; }
   /// <summary>Caller-owned filters applied by the contact service before pagination.</summary>
   public ContactSearchFilter Filter { get; set; } = new();
   /// <summary>Optional caller-owned reason why a contact cannot be chosen; null allows selection.</summary>
   public Func<ContactSearchItem, string?>? SelectionBlockReason { get; set; }
   /// <summary>Asks the dialog to close with the selected contact.</summary>
   public event Action<bool>? RequestClose;
   /// <summary>Contact name or linked username to search.</summary>
   public string Search { get => Get<string>() ?? ""; set => Set(value); }
   /// <summary>Current server page.</summary>
   public ObservableCollection<ContactPickerRow> Rows { get; } = [];
   /// <summary>Selected display row and its caller-supplied availability reason.</summary>
   public ContactPickerRow? SelectedRow { get => Get<ContactPickerRow>(); set => Set(value, _ => UpdateState()); }
   /// <summary>The contact returned to the calling module.</summary>
   public ContactSearchItem? SelectedContact => SelectedRow?.Contact;
   /// <summary>One-based number of the successfully loaded page.</summary>
   public int Page { get => Get<int>(1); private set => Set(value); }
   /// <summary>Whether more matching contacts exist.</summary>
   public bool HasMore { get => Get<bool>(); private set => Set(value); }
   /// <summary>Whether a search has completed successfully.</summary>
   public bool IsLoaded { get => Get<bool>(); private set => Set(value); }
   /// <summary>Page caption without loading a total record count.</summary>
   public string PageCaption => "Page " + Page.ToString(CultureInfo.GetCultureInfo("en-US")) + " · up to 50 contacts per page";
   /// <summary>Whether an empty-results message should be shown.</summary>
   public bool IsEmpty => IsLoaded && Rows.Count == 0;
   /// <summary>Error or instruction displayed under the search toolbar.</summary>
   public string Status { get => Get<string>() ?? ""; private set => Set(value); }
   /// <summary>Whether a new search may start.</summary>
   public bool SearchCommandAllowed() => !_released && IsNotBusy && Service is not null;
   /// <summary>Starts at page one with the text currently entered.</summary>
   public Task SearchCommand() => LoadPage(1, Search);
   /// <summary>Whether the preceding page can be loaded.</summary>
   public bool PreviousPageCommandAllowed() => SearchCommandAllowed() && IsLoaded && Page > 1;
   /// <summary>Loads the previous page of the applied search.</summary>
   public Task PreviousPageCommand() => PreviousPageCommandAllowed() ? LoadPage(Page - 1, _appliedSearch) : Task.CompletedTask;
   /// <summary>Whether another page is available.</summary>
   public bool NextPageCommandAllowed() => SearchCommandAllowed() && IsLoaded && HasMore;
   /// <summary>Loads the next page of the applied search.</summary>
   public Task NextPageCommand() => NextPageCommandAllowed() ? LoadPage(Page + 1, _appliedSearch) : Task.CompletedTask;
   /// <summary>Whether the selected contact is allowed by the calling module.</summary>
   public bool ChooseCommandAllowed() => SearchCommandAllowed() && SelectedRow is { BlockReason: null };
   /// <summary>Accepts the contact when the calling module allows it.</summary>
   public void ChooseCommand() { if (ChooseCommandAllowed()) RequestClose?.Invoke(true); }
   /// <summary>Ignores any pending response after the dialog has closed.</summary>
   public void Release() { _released = true; UpdateState(); }
   private async Task LoadPage(int page, string search) {
      if (!SearchCommandAllowed() || Service is not { } service) return;
      try {
         IsBusy = InWaiting = true; WaiterText = "Searching contacts..."; Status = ""; UpdateState();
         var result = await service.PostGetMeta_ContactSearchPage(new() { Search = search, Page = page, Filter = Filter });
         if (_released) return;
         SelectedRow = null; Rows.Clear(); foreach (var row in result.Rows) Rows.Add(new(row, SelectionBlockReason?.Invoke(row)));
         Page = page; HasMore = result.HasMore; _appliedSearch = search; IsLoaded = true;
      }
      catch (Exception ex) {
         if (!_released) Status = ex is Em.Shared.ActionException ? ex.Message.Replace("Server Error: ", "") : "Could not search contacts. Try again.";
      }
      finally { if (!_released) { IsBusy = InWaiting = false; UpdateState(); } }
   }
   private void UpdateState() {
      NotifyChanged(nameof(SelectedContact)); NotifyChanged(nameof(PageCaption)); NotifyChanged(nameof(IsEmpty));
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
   }
}

/// <summary>A contact row annotated only with the calling module's selection rule.</summary>
/// <param name="Contact">Contact search result from the contact service.</param>
/// <param name="BlockReason">Null allows selection; otherwise the reason is shown beside the contact.</param>
public sealed record ContactPickerRow(ContactSearchItem Contact, string? BlockReason);
