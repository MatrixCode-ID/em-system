using System.Collections.ObjectModel;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Navigations;

/// <summary>A choice of the Position field: a position, or no position at all.</summary>
/// <param name="Id">Position ID; <c>null</c> = no position.</param>
/// <param name="Caption">Text shown in the list.</param>
public sealed record PositionOption(string? Id, string Caption);

/// <summary>State and commands of the Positions page: the position list of one tenant.</summary>
public sealed class BusinessModuleManagerPositionVm : MvvmModelBase
{
   private readonly ITenantPositionServices? _service;
   private bool _refreshingRows;
   private bool _opening;

   /// <summary>Creates a view model for navigation; the service is taken from the application.</summary>
   public BusinessModuleManagerPositionVm() { RegisterCommands(); }

   /// <summary>Creates a view model with an explicit service, for tests and other hosts.</summary>
   public BusinessModuleManagerPositionVm(ITenantPositionServices service) { _service = service; RegisterCommands(); }

   private ITenantPositionServices? Service => _service ?? EmApp?.ServiceProvider.GetRequiredService<ITenantPositionServices>();

   private void RegisterCommands() {
      RegisterCommand(nameof(NewPositionCommand), NewPositionCommand, NewPositionCommandAllowed);
      RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
      RegisterCommand(nameof(ToggleStateCommand), ToggleStateCommand, ToggleStateCommandAllowed);
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
      RegisterCommand(nameof(CopyFromTenantCommand), CopyFromTenantCommand, CopyFromTenantCommandAllowed);
   }

   /// <summary>Tenants available in the toolbar.</summary>
   public ObservableCollection<vi_Tenant> TenantChoices { get; } = [];

   /// <summary>The tenant being managed.</summary>
   public vi_Tenant? SelectedTenant {
      get => Get<vi_Tenant>();
      set {
         if (ReferenceEquals(value, SelectedTenant)) return;
         if (IsBusy || !CanLeave()) { NotifyChanged(); return; }
         Set(value); Rows.Clear(); Open(null); IsLoaded = false;
         Loading = ReloadAsync(); UpdateState();
      }
   }

   /// <summary>Most recent tenant load, also awaitable by the owning screen.</summary>
   public Task Loading { get; private set; } = Task.CompletedTask;

   /// <summary>Host-supplied question asked before unsaved changes are discarded; <c>true</c> = discard.</summary>
   public Func<bool>? ConfirmDiscard { get; set; }

   /// <summary>Host dialog that chooses what to copy into the selected tenant; <c>null</c> = cancelled.</summary>
   public Func<TenantPositionCopy?>? PickCopy { get; set; }

   /// <summary>Saved positions of the tenant, and a draft when one is open.</summary>
   public ObservableCollection<vi_TenantPosition> Rows { get; } = [];

   /// <summary>The position open in the editor.</summary>
   public vi_TenantPosition? SelectedRow {
      get => Get<vi_TenantPosition>();
      set {
         if (_refreshingRows) return;
         if (_opening) { Set(value); return; }
         if (ReferenceEquals(value, SelectedRow)) return;
         if (IsBusy || !CanLeave()) { NotifyChanged(); return; }
         DropDraft(value);
         Open(value);
      }
   }

   /// <summary>Code of the edited position.</summary>
   public string EditCode { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Name of the edited position.</summary>
   public string EditName { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Note of the edited position.</summary>
   public string? EditNote { get => Get<string>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Whether a position is open in the editor.</summary>
   public bool HasSelection => SelectedRow is not null;

   /// <summary>Whether nothing is open in the editor, to show the hint instead.</summary>
   public bool HasNoSelection => SelectedRow is null;

   /// <summary>Whether the editor shows a draft that has not been saved.</summary>
   public bool IsDraftSelected => SelectedRow is { cTenantPositionId.Length: 0 };

   /// <summary>Whether the editor differs from the saved position; a draft always counts as changed.</summary>
   public bool IsDirty => SelectedRow is { } row && (IsDraftSelected || EditCode != row.cTenantPositionCode ||
      EditName != row.cTenantPositionName || (EditNote ?? "") != (row.cTenantPositionNote ?? ""));

   /// <summary>Caption of the state button: Deactivate for an active position, Activate for an inactive one.</summary>
   public string ToggleStateCaption => SelectedRow?.cTenantPositionState < ta_TenantPosition.StateActive ? "Activate" : "Deactivate";

   /// <summary>Whether the toolbar and list accept input.</summary>
   public bool CanBrowse => IsLoaded && SelectedTenant is not null && IsNotBusy;

   /// <summary>Whether the editor fields accept input.</summary>
   public bool CanEdit => CanBrowse && HasSelection;

   /// <summary>True after the latest tenant load succeeded.</summary>
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

   /// <summary>Whether a new position can be started.</summary>
   public bool NewPositionCommandAllowed() => CanBrowse;

   /// <summary>Starts a new position as a draft row.</summary>
   public void NewPositionCommand() {
      if (!NewPositionCommandAllowed() || !CanLeave()) return;
      DropDraft(null);
      var draft = new vi_TenantPosition { cTenantId = SelectedTenant!.cTenantId, cTenantPositionState = ta_TenantPosition.StateActive };
      Rows.Add(draft);
      Open(draft);
      Status = "";
   }

   /// <summary>Whether the editor holds changes that can be saved.</summary>
   public bool SaveCommandAllowed() => CanEdit && IsDirty && !string.IsNullOrWhiteSpace(EditCode) && !string.IsNullOrWhiteSpace(EditName);

   /// <summary>Creates the draft or saves the changes of the selected position, then reloads the list.</summary>
   public Task SaveCommand() => RunAsync("Saving position...", async () => {
      if (SelectedRow is not { } row || Service is not { } service) return;
      var draft = IsDraftSelected;
      var change = new ta_TenantPosition {
         cTenantPositionId = row.cTenantPositionId, cTenantId = row.cTenantId, cTenantPositionCode = EditCode, cTenantPositionName = EditName,
         cTenantPositionNote = string.IsNullOrEmpty(EditNote) ? null : EditNote, cTenantPositionState = row.cTenantPositionState,
         json_object = row.json_object, ustamp = row.ustamp
      };
      var saved = draft ? await service.PostGetTa_TenantPosition_New(change) : await service.PostGetTa_TenantPosition_Update(change);
      await Load(saved.cTenantPositionId);
      Status = draft ? $"Position '{saved.cTenantPositionCode}' created." : $"Position '{saved.cTenantPositionCode}' saved.";
   });

   /// <summary>Whether the selected saved position, without unsaved changes, can be deactivated or activated.</summary>
   public bool ToggleStateCommandAllowed() => CanEdit && !IsDraftSelected && !IsDirty;

   /// <summary>Stops or resumes offering the selected position; employees holding it keep it.</summary>
   public Task ToggleStateCommand() => RunAsync("Saving position...", async () => {
      if (SelectedRow is not { } row || IsDraftSelected || Service is not { } service) return;
      var activate = row.cTenantPositionState < ta_TenantPosition.StateActive;
      var saved = await service.PostGetTa_TenantPosition_Update(new ta_TenantPosition {
         cTenantPositionId = row.cTenantPositionId, cTenantId = row.cTenantId, cTenantPositionCode = row.cTenantPositionCode,
         cTenantPositionName = row.cTenantPositionName, cTenantPositionNote = row.cTenantPositionNote, json_object = row.json_object,
         cTenantPositionState = activate ? ta_TenantPosition.StateActive : ta_TenantPosition.StateInactive, ustamp = row.ustamp
      });
      await Load(saved.cTenantPositionId);
      Status = activate ? $"Position '{saved.cTenantPositionCode}' activated." : $"Position '{saved.cTenantPositionCode}' deactivated.";
   });

   /// <summary>Whether the list can be read again.</summary>
   public bool RefreshCommandAllowed() => IsNotBusy && SelectedTenant is not null;

   /// <summary>Reads the positions again, asking first when the editor holds unsaved changes.</summary>
   public Task RefreshCommand() => CanLeave() ? ReloadAsync() : Task.CompletedTask;

   /// <summary>Whether positions can be copied into the tenant.</summary>
   public bool CopyFromTenantCommandAllowed() => CanBrowse && TenantChoices.Count > 1;

   /// <summary>Lets the host choose positions of another tenant, then copies them here.</summary>
   public async Task CopyFromTenantCommand() {
      if (!CopyFromTenantCommandAllowed() || !CanLeave() || PickCopy?.Invoke() is not { } copy) return;
      await RunAsync("Copying positions...", async () => {
         if (Service is not { } service) return;
         var created = await service.PostGetMeta_TenantPosition_Copy(copy);
         await Load(created.FirstOrDefault()?.cTenantPositionId);
         Status = created.Length == 1 ? "Copied 1 position." : $"Copied {created.Length} positions.";
      });
   }

   /// <summary>Reads the selected tenant again, keeping the selected position when it still exists.</summary>
   public Task ReloadAsync() => RunAsync("Loading positions...", async () => {
      var selected = SelectedRow is { cTenantPositionId.Length: > 0 } row ? row.cTenantPositionId : null;
      await Load(selected);
      Status = "";
   });

   private async Task Load(string? selectId) {
      if (Service is not { } service || SelectedTenant is null) return;
      var rows = await service.GetVi_TenantPositions(SelectedTenant.cTenantId);
      _refreshingRows = true;
      try {
         Rows.Clear();
         foreach (var row in rows) Rows.Add(row);
      }
      finally { _refreshingRows = false; }
      Open(rows.FirstOrDefault(x => string.Equals(x.cTenantPositionId, selectId, StringComparison.OrdinalIgnoreCase)));
      IsLoaded = true;
   }

   // Removes a draft row other than the one being opened.
   private void DropDraft(vi_TenantPosition? keep) {
      if (Rows.FirstOrDefault(x => x.cTenantPositionId.Length == 0) is not { } draft || ReferenceEquals(draft, keep)) return;
      _refreshingRows = true;
      try { Rows.Remove(draft); }
      finally { _refreshingRows = false; }
   }

   private void Open(vi_TenantPosition? row) {
      _opening = true;
      try {
         SelectedRow = row;
         EditCode = row?.cTenantPositionCode ?? ""; EditName = row?.cTenantPositionName ?? ""; EditNote = row?.cTenantPositionNote;
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
      foreach (var name in (string[])[nameof(HasSelection), nameof(HasNoSelection), nameof(IsDraftSelected), nameof(IsDirty), nameof(ToggleStateCaption), nameof(CanBrowse), nameof(CanEdit)])
         NotifyChanged(name);
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
   }
}
