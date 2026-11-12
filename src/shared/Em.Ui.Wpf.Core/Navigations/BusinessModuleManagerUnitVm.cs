using System.Collections.ObjectModel;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Navigations;

/// <summary>State and commands of the Units page: the units of measure shared by every tenant.</summary>
public sealed class BusinessModuleManagerUnitVm : MvvmModelBase
{
   private readonly IUnitServices? _service;
   private vi_Unit? _draft;
   private bool _refreshingRows;
   private bool _opening;

   /// <summary>Creates a view model for navigation; the service is taken from the application.</summary>
   public BusinessModuleManagerUnitVm() { RegisterCommands(); }

   /// <summary>Creates a view model with an explicit service, for tests and other hosts.</summary>
   public BusinessModuleManagerUnitVm(IUnitServices service) { _service = service; RegisterCommands(); }

   private IUnitServices? Service => _service ?? EmApp?.ServiceProvider.GetRequiredService<IUnitServices>();

   private void RegisterCommands() {
      RegisterCommand(nameof(NewUnitCommand), NewUnitCommand, NewUnitCommandAllowed);
      RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
      RegisterCommand(nameof(DeleteCommand), DeleteCommand, DeleteCommandAllowed);
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
   }

   /// <summary>Most recent load, also awaitable by the owning screen.</summary>
   public Task Loading { get; set; } = Task.CompletedTask;

   /// <summary>Host-supplied question asked before unsaved changes are discarded; <c>true</c> = discard.</summary>
   public Func<bool>? ConfirmDiscard { get; set; }

   /// <summary>Host-supplied question asked before a unit is deleted; <c>true</c> = delete.</summary>
   public Func<vi_Unit, bool>? ConfirmDelete { get; set; }

   /// <summary>Saved units, and a draft when one is open.</summary>
   public ObservableCollection<vi_Unit> Rows { get; } = [];

   /// <summary>The unit open in the editor.</summary>
   public vi_Unit? SelectedRow {
      get => Get<vi_Unit>();
      set {
         if (_refreshingRows) return;
         if (_opening) { Set(value); return; }
         if (ReferenceEquals(value, SelectedRow)) return;
         if (IsBusy || !CanLeave()) { NotifyChanged(); return; }
         DropDraft(value);
         Open(value);
      }
   }

   /// <summary>Code of the edited unit; it can be entered for a new unit only.</summary>
   public string EditCode { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Description of the edited unit.</summary>
   public string EditDescription { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Symbol of the edited unit.</summary>
   public string EditSymbol { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Whether a unit is open in the editor.</summary>
   public bool HasSelection => SelectedRow is not null;

   /// <summary>Whether nothing is open in the editor, to show the hint instead.</summary>
   public bool HasNoSelection => SelectedRow is null;

   /// <summary>Whether the editor shows a draft that has not been saved.</summary>
   public bool IsDraftSelected => SelectedRow is { } row && ReferenceEquals(row, _draft);

   /// <summary>Whether the code can be entered: only before the first save, because products refer to it.</summary>
   public bool CanEditCode => IsDraftSelected;

   /// <summary>Whether the code is fixed, to show it as read-only.</summary>
   public bool IsCodeFixed => HasSelection && !IsDraftSelected;

   /// <summary>Whether the editor differs from the saved unit; a draft always counts as changed.</summary>
   public bool IsDirty => SelectedRow is { } row && (IsDraftSelected || EditDescription != row.cUnitDesc || EditSymbol != row.cUnitSymbol);

   /// <summary>How many products use the selected unit, as a sentence.</summary>
   public string UsageCaption => SelectedRow is not { } row || IsDraftSelected ? "" :
      row.ccUnitProductCount == 0 ? "No product uses this unit; it can be deleted." :
      row.ccUnitProductCount == 1 ? "1 product uses this unit; it cannot be deleted." : $"{row.ccUnitProductCount} products use this unit; it cannot be deleted.";

   /// <summary>Whether the toolbar and list accept input.</summary>
   public bool CanBrowse => IsLoaded && IsNotBusy;

   /// <summary>Whether the editor fields accept input.</summary>
   public bool CanEdit => CanBrowse && HasSelection;

   /// <summary>True after the latest load succeeded.</summary>
   public bool IsLoaded { get => Get<bool>(); private set => Set(value); }

   /// <summary>Result or server error shown below the toolbar.</summary>
   public string Status { get => Get<string>() ?? ""; private set => Set(value); }

   /// <summary>Checks whether unsaved changes may be discarded.</summary>
   public bool CanLeave() => !IsDirty || ConfirmDiscard?.Invoke() != false;

   /// <summary>Restores the last saved values after the owning screen confirmed discard.</summary>
   public void DiscardChanges() {
      if (!IsDirty) return;
      if (IsDraftSelected) { DropDraft(null); Open(null); }
      else Open(SelectedRow);
   }

   /// <summary>Whether a new unit can be started.</summary>
   public bool NewUnitCommandAllowed() => CanBrowse;

   /// <summary>Starts a new unit as a draft row.</summary>
   public void NewUnitCommand() {
      if (!NewUnitCommandAllowed() || !CanLeave()) return;
      DropDraft(null);
      var draft = _draft = new vi_Unit();
      Rows.Add(draft);
      Open(draft);
      Status = "";
   }

   /// <summary>Whether the editor holds changes that can be saved.</summary>
   public bool SaveCommandAllowed() => CanEdit && IsDirty && !string.IsNullOrWhiteSpace(EditCode) && !string.IsNullOrWhiteSpace(EditDescription) && !string.IsNullOrWhiteSpace(EditSymbol);

   /// <summary>Creates the draft or saves the changes of the selected unit, then reloads the list.</summary>
   public Task SaveCommand() => RunAsync("Saving unit...", async () => {
      if (SelectedRow is not { } row || Service is not { } service) return;
      var draft = IsDraftSelected;
      var change = new ta_Unit { cUnitId = draft ? EditCode.Trim() : row.cUnitId, cUnitDesc = EditDescription, cUnitSymbol = EditSymbol, ustamp = row.ustamp };
      var saved = draft ? await service.PostGetTa_Unit_New(change) : await service.PostGetTa_Unit_Update(change);
      await Load(saved.cUnitId);
      Status = draft ? $"Unit '{saved.cUnitId}' created." : $"Unit '{saved.cUnitId}' saved.";
   });

   /// <summary>Whether the selected saved unit can be deleted: no unsaved changes and no product using it.</summary>
   public bool DeleteCommandAllowed() => CanEdit && !IsDraftSelected && !IsDirty && SelectedRow is { ccUnitProductCount: 0 };

   /// <summary>Deletes the selected unit after confirmation. The server refuses a unit a product uses.</summary>
   public async Task DeleteCommand() {
      if (!DeleteCommandAllowed() || SelectedRow is not { } row || ConfirmDelete?.Invoke(row) == false) return;
      await RunAsync("Deleting unit...", async () => {
         if (Service is not { } service) return;
         await service.PostMeta_Unit_Delete(row.cUnitId, row.ustamp);
         await Load(null);
         Status = $"Unit '{row.cUnitId}' deleted.";
      });
   }

   /// <summary>Whether the list can be read again.</summary>
   public bool RefreshCommandAllowed() => IsNotBusy;

   /// <summary>Reads the units again, asking first when the editor holds unsaved changes.</summary>
   public Task RefreshCommand() => CanLeave() ? ReloadAsync() : Task.CompletedTask;

   /// <summary>Reads the units again, keeping the selected unit when it still exists.</summary>
   public Task ReloadAsync() => RunAsync("Loading units...", async () => {
      var selected = SelectedRow is { } row && !IsDraftSelected ? row.cUnitId : null;
      await Load(selected);
      Status = "";
   });

   private async Task Load(string? selectId) {
      if (Service is not { } service) return;
      var rows = await service.GetVi_Units();
      _refreshingRows = true;
      try {
         _draft = null;
         Rows.Clear();
         foreach (var row in rows) Rows.Add(row);
      }
      finally { _refreshingRows = false; }
      Open(selectId is null ? null : rows.FirstOrDefault(x => string.Equals(x.cUnitId, selectId, StringComparison.OrdinalIgnoreCase)));
      IsLoaded = true;
   }

   // Removes a draft row other than the one being opened.
   private void DropDraft(vi_Unit? keep) {
      if (_draft is not { } draft || ReferenceEquals(draft, keep)) return;
      _draft = null;
      _refreshingRows = true;
      try { Rows.Remove(draft); }
      finally { _refreshingRows = false; }
   }

   private void Open(vi_Unit? row) {
      _opening = true;
      try {
         SelectedRow = row;
         EditCode = row?.cUnitId ?? ""; EditDescription = row?.cUnitDesc ?? ""; EditSymbol = row?.cUnitSymbol ?? "";
      }
      finally { _opening = false; }
      NotifyChanged(nameof(SelectedRow));
      UpdateState();
   }

   private async Task RunAsync(string caption, Func<Task> action) {
      if (IsBusy || Service is null) return;
      try {
         IsBusy = InWaiting = true; WaiterText = caption; UpdateState();
         await action();
      }
      catch (ActionException ex) { Status = ex.Message.Replace("Server Error: ", ""); }
      catch (Exception ex) {
         Status = "The operation did not complete. Refresh and try again.";
         if (EmApp is not null) AlertError(ex);
      }
      finally { InWaiting = IsBusy = false; UpdateState(); }
   }

   private void UpdateState() {
      foreach (var name in (string[])[nameof(HasSelection), nameof(HasNoSelection), nameof(IsDraftSelected), nameof(CanEditCode), nameof(IsCodeFixed), nameof(IsDirty), nameof(UsageCaption), nameof(CanBrowse), nameof(CanEdit)])
         NotifyChanged(name);
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
   }
}
