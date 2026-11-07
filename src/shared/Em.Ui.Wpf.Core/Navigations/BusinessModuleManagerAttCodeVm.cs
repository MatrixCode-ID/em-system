using System.Collections.ObjectModel;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Navigations;

/// <summary>A choice of the Category field.</summary>
/// <param name="Value">The category.</param>
/// <param name="Caption">Text shown in the list.</param>
public sealed record AttCodeCategoryOption(TenantAttCodeCategory Value, string Caption);

/// <summary>State and commands of the Attendance codes page: the code list of one tenant.</summary>
public sealed class BusinessModuleManagerAttCodeVm : MvvmModelBase
{
   private readonly ITenantAttCodeServices? _service;
   private bool _refreshingRows;
   private bool _opening;

   /// <summary>Creates a view model for navigation; the service is taken from the application.</summary>
   public BusinessModuleManagerAttCodeVm() { RegisterCommands(); }

   /// <summary>Creates a view model with an explicit service, for tests and other hosts.</summary>
   public BusinessModuleManagerAttCodeVm(ITenantAttCodeServices service) { _service = service; RegisterCommands(); }

   private ITenantAttCodeServices? Service => _service ?? EmApp?.ServiceProvider.GetRequiredService<ITenantAttCodeServices>();

   private void RegisterCommands() {
      RegisterCommand(nameof(NewCodeCommand), NewCodeCommand, NewCodeCommandAllowed);
      RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
      RegisterCommand(nameof(ToggleStateCommand), ToggleStateCommand, ToggleStateCommandAllowed);
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
      RegisterCommand(nameof(CopyFromTenantCommand), CopyFromTenantCommand, CopyFromTenantCommandAllowed);
   }

   /// <summary>The categories, in the order of the enum.</summary>
   public IReadOnlyList<AttCodeCategoryOption> CategoryOptions { get; } = [
      new(TenantAttCodeCategory.Present, "Present"), new(TenantAttCodeCategory.PaidAbsence, "Paid absence"),
      new(TenantAttCodeCategory.UnpaidAbsence, "Unpaid absence"), new(TenantAttCodeCategory.DayOff, "Day off")];

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
   public Func<TenantAttCodeCopy?>? PickCopy { get; set; }

   /// <summary>Saved codes of the tenant, and a draft when one is open.</summary>
   public ObservableCollection<vi_TenantAttCode> Rows { get; } = [];

   /// <summary>The code open in the editor.</summary>
   public vi_TenantAttCode? SelectedRow {
      get => Get<vi_TenantAttCode>();
      set {
         if (_refreshingRows) return;
         if (_opening) { Set(value); return; }
         if (ReferenceEquals(value, SelectedRow)) return;
         if (IsBusy || !CanLeave()) { NotifyChanged(); return; }
         DropDraft(value);
         Open(value);
      }
   }

   /// <summary>Code of the edited record.</summary>
   public string EditCode { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Name of the edited record.</summary>
   public string EditName { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Category of the edited record.</summary>
   public AttCodeCategoryOption? EditCategory { get => Get<AttCodeCategoryOption>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Display order of the edited record.</summary>
   public int EditOrder { get => Get<int>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Note of the edited record.</summary>
   public string? EditNote { get => Get<string>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Whether a code is open in the editor.</summary>
   public bool HasSelection => SelectedRow is not null;

   /// <summary>Whether nothing is open in the editor, to show the hint instead.</summary>
   public bool HasNoSelection => SelectedRow is null;

   /// <summary>Whether the editor shows a draft that has not been saved.</summary>
   public bool IsDraftSelected => SelectedRow is { cTenantAttCodeId.Length: 0 };

   /// <summary>Whether the editor differs from the saved code; a draft always counts as changed.</summary>
   public bool IsDirty => SelectedRow is { } row && (IsDraftSelected || EditCode != row.cTenantAttCodeCode || EditName != row.cTenantAttCodeName ||
      EditCategory?.Value != row.cTenantAttCodeCategory || EditOrder != row.cTenantAttCodeOrder || (EditNote ?? "") != (row.cTenantAttCodeNote ?? ""));

   /// <summary>Caption of the state button: Deactivate for an active code, Activate for an inactive one.</summary>
   public string ToggleStateCaption => SelectedRow?.cTenantAttCodeState < ta_TenantAttCode.StateActive ? "Activate" : "Deactivate";

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

   /// <summary>Whether a new code can be started.</summary>
   public bool NewCodeCommandAllowed() => CanBrowse;

   /// <summary>Starts a new code as a draft row.</summary>
   public void NewCodeCommand() {
      if (!NewCodeCommandAllowed() || !CanLeave()) return;
      DropDraft(null);
      var draft = new vi_TenantAttCode { cTenantId = SelectedTenant!.cTenantId, cTenantAttCodeState = ta_TenantAttCode.StateActive };
      Rows.Add(draft);
      Open(draft);
      Status = "";
   }

   /// <summary>Whether the editor holds changes that can be saved.</summary>
   public bool SaveCommandAllowed() => CanEdit && IsDirty && EditCategory is not null && !string.IsNullOrWhiteSpace(EditCode) && !string.IsNullOrWhiteSpace(EditName);

   /// <summary>Creates the draft or saves the changes of the selected code, then reloads the list.</summary>
   public Task SaveCommand() => RunAsync("Saving attendance code...", async () => {
      if (SelectedRow is not { } row || Service is not { } service) return;
      var draft = IsDraftSelected;
      var change = new ta_TenantAttCode {
         cTenantAttCodeId = row.cTenantAttCodeId, cTenantId = row.cTenantId, cTenantAttCodeCode = EditCode, cTenantAttCodeName = EditName,
         cTenantAttCodeCategory = EditCategory!.Value, cTenantAttCodeOrder = EditOrder, cTenantAttCodeNote = string.IsNullOrEmpty(EditNote) ? null : EditNote,
         cTenantAttCodeState = row.cTenantAttCodeState, json_object = row.json_object, ustamp = row.ustamp
      };
      var saved = draft ? await service.PostGetTa_TenantAttCode_New(change) : await service.PostGetTa_TenantAttCode_Update(change);
      await Load(saved.cTenantAttCodeId);
      Status = draft ? $"Code '{saved.cTenantAttCodeCode}' created." : $"Code '{saved.cTenantAttCodeCode}' saved.";
   });

   /// <summary>Whether the selected saved code, without unsaved changes, can be deactivated or activated.</summary>
   public bool ToggleStateCommandAllowed() => CanEdit && !IsDraftSelected && !IsDirty;

   /// <summary>Stops or resumes offering the selected code.</summary>
   public Task ToggleStateCommand() => RunAsync("Saving attendance code...", async () => {
      if (SelectedRow is not { } row || IsDraftSelected || Service is not { } service) return;
      var activate = row.cTenantAttCodeState < ta_TenantAttCode.StateActive;
      var saved = await service.PostGetTa_TenantAttCode_Update(new ta_TenantAttCode {
         cTenantAttCodeId = row.cTenantAttCodeId, cTenantId = row.cTenantId, cTenantAttCodeCode = row.cTenantAttCodeCode, cTenantAttCodeName = row.cTenantAttCodeName,
         cTenantAttCodeCategory = row.cTenantAttCodeCategory, cTenantAttCodeOrder = row.cTenantAttCodeOrder, cTenantAttCodeNote = row.cTenantAttCodeNote, json_object = row.json_object,
         cTenantAttCodeState = activate ? ta_TenantAttCode.StateActive : ta_TenantAttCode.StateInactive, ustamp = row.ustamp
      });
      await Load(saved.cTenantAttCodeId);
      Status = activate ? $"Code '{saved.cTenantAttCodeCode}' activated." : $"Code '{saved.cTenantAttCodeCode}' deactivated.";
   });

   /// <summary>Whether the list can be read again.</summary>
   public bool RefreshCommandAllowed() => IsNotBusy && SelectedTenant is not null;

   /// <summary>Reads the codes again, asking first when the editor holds unsaved changes.</summary>
   public Task RefreshCommand() => CanLeave() ? ReloadAsync() : Task.CompletedTask;

   /// <summary>Whether codes can be copied into the tenant.</summary>
   public bool CopyFromTenantCommandAllowed() => CanBrowse && TenantChoices.Count > 1;

   /// <summary>Lets the host choose codes of another tenant, then copies them here.</summary>
   public async Task CopyFromTenantCommand() {
      if (!CopyFromTenantCommandAllowed() || !CanLeave() || PickCopy?.Invoke() is not { } copy) return;
      await RunAsync("Copying attendance codes...", async () => {
         if (Service is not { } service) return;
         var created = await service.PostGetMeta_TenantAttCode_Copy(copy);
         await Load(created.FirstOrDefault()?.cTenantAttCodeId);
         Status = created.Length == 1 ? "Copied 1 code." : $"Copied {created.Length} codes.";
      });
   }

   /// <summary>Reads the selected tenant again, keeping the selected code when it still exists.</summary>
   public Task ReloadAsync() => RunAsync("Loading attendance codes...", async () => {
      var selected = SelectedRow is { cTenantAttCodeId.Length: > 0 } row ? row.cTenantAttCodeId : null;
      await Load(selected);
      Status = "";
   });

   private async Task Load(string? selectId) {
      if (Service is not { } service || SelectedTenant is null) return;
      var rows = await service.GetVi_TenantAttCodes(SelectedTenant.cTenantId);
      _refreshingRows = true;
      try {
         Rows.Clear();
         foreach (var row in rows) Rows.Add(row);
      }
      finally { _refreshingRows = false; }
      Open(rows.FirstOrDefault(x => string.Equals(x.cTenantAttCodeId, selectId, StringComparison.OrdinalIgnoreCase)));
      IsLoaded = true;
   }

   // Removes a draft row other than the one being opened.
   private void DropDraft(vi_TenantAttCode? keep) {
      if (Rows.FirstOrDefault(x => x.cTenantAttCodeId.Length == 0) is not { } draft || ReferenceEquals(draft, keep)) return;
      _refreshingRows = true;
      try { Rows.Remove(draft); }
      finally { _refreshingRows = false; }
   }

   private void Open(vi_TenantAttCode? row) {
      _opening = true;
      try {
         SelectedRow = row;
         EditCode = row?.cTenantAttCodeCode ?? ""; EditName = row?.cTenantAttCodeName ?? ""; EditOrder = row?.cTenantAttCodeOrder ?? -1; EditNote = row?.cTenantAttCodeNote;
         EditCategory = row is null ? null : CategoryOptions.FirstOrDefault(x => x.Value == row.cTenantAttCodeCategory);
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
