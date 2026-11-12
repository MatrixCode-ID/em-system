using System.Collections.ObjectModel;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Navigations;

/// <summary>A choice of the Kind field of a product type.</summary>
/// <param name="Value">The kind.</param>
/// <param name="Caption">Text shown in the list.</param>
public sealed record ProductKindOption(ProductKind Value, string Caption)
{
   /// <summary>Caption of a kind, also for the system kind that is never offered as a choice.</summary>
   public static string CaptionOf(ProductKind kind) => kind switch {
      ProductKind.Goods => "Goods",
      ProductKind.ManufactureBase => "Manufacture base",
      ProductKind.ProductComponent => "Product component",
      ProductKind.NonStock => "Non-stock",
      ProductKind.Service => "Service",
      ProductKind.Financial => "Financial",
      ProductKind.GenericTransaction => "Generic transaction",
      _ => kind.ToString()
   };

   /// <summary>The kinds an administrator can choose; the system kind is left out.</summary>
   public static IReadOnlyList<ProductKindOption> Choices { get; } = [
      .. new[] { ProductKind.Goods, ProductKind.ManufactureBase, ProductKind.ProductComponent, ProductKind.NonStock, ProductKind.Service, ProductKind.Financial }
         .Select(kind => new ProductKindOption(kind, CaptionOf(kind)))];
}

/// <summary>One product type in the list of the Product types page.</summary>
public sealed class ProductTypeItem(vi_TenantProductType row)
{
   /// <summary>The product type row as last read; for a draft, its starting values.</summary>
   public vi_TenantProductType Row { get; } = row;

   /// <summary>Whether the row has not been saved yet.</summary>
   public bool IsDraft => Row.cTenantProductTypeId.Length == 0;

   /// <summary>Name shown in the list.</summary>
   public string DisplayName => IsDraft ? "(new product type)" : Row.cTenantProductTypeName;

   /// <summary>Code and kind shown under the name.</summary>
   public string DetailCaption => IsDraft ? "" : $"{Row.cTenantProductTypeCode} · {ProductKindOption.CaptionOf(Row.cTenantProductTypeKind)}";

   /// <summary>Whether the product type is deactivated.</summary>
   public bool IsInactive => !IsDraft && Row.cTenantProductTypeState < ta_TenantProductType.StateActive;

   /// <summary>Whether this is the type the system creates; only its name and note can change.</summary>
   public bool IsSystem => Row.cTenantProductTypeKind == ProductKind.GenericTransaction;

   /// <summary>Number of products of this type.</summary>
   public int ProductCount => Row.ccTenantProductTypeProductCount;
}

/// <summary>State and commands of the Product types page: the product types of one tenant.</summary>
public sealed class BusinessModuleManagerProductTypeVm : MvvmModelBase
{
   private readonly ITenantProductTypeServices? _service;
   private bool _refreshingRows;
   private bool _opening;

   /// <summary>Creates a view model for navigation; the service is taken from the application.</summary>
   public BusinessModuleManagerProductTypeVm() { RegisterCommands(); }

   /// <summary>Creates a view model with an explicit service, for tests and other hosts.</summary>
   public BusinessModuleManagerProductTypeVm(ITenantProductTypeServices service) { _service = service; RegisterCommands(); }

   private ITenantProductTypeServices? Service => _service ?? EmApp?.ServiceProvider.GetRequiredService<ITenantProductTypeServices>();

   private void RegisterCommands() {
      RegisterCommand(nameof(NewProductTypeCommand), NewProductTypeCommand, NewProductTypeCommandAllowed);
      RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
      RegisterCommand(nameof(ToggleStateCommand), ToggleStateCommand, ToggleStateCommandAllowed);
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
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

   /// <summary>Host-supplied question asked before a product type is deactivated; <c>true</c> = deactivate.</summary>
   public Func<vi_TenantProductType, bool>? ConfirmDeactivate { get; set; }

   /// <summary>The kinds offered by the Kind field.</summary>
   public IReadOnlyList<ProductKindOption> KindOptions => ProductKindOption.Choices;

   /// <summary>Saved product types of the tenant, and a draft when one is open.</summary>
   public ObservableCollection<ProductTypeItem> Rows { get; } = [];

   /// <summary>The product type open in the editor.</summary>
   public ProductTypeItem? SelectedRow {
      get => Get<ProductTypeItem>();
      set {
         if (_refreshingRows) return;
         if (_opening) { Set(value); return; }
         if (ReferenceEquals(value, SelectedRow)) return;
         if (IsBusy || !CanLeave()) { NotifyChanged(); return; }
         DropDraft(value);
         Open(value);
      }
   }

   /// <summary>Code of the edited product type.</summary>
   public string EditCode { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Name of the edited product type.</summary>
   public string EditName { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Kind of the edited product type; empty for the system type, whose kind is not a choice.</summary>
   public ProductKindOption? EditKind { get => Get<ProductKindOption>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Note of the edited product type.</summary>
   public string? EditNote { get => Get<string>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Whether a product type is open in the editor.</summary>
   public bool HasSelection => SelectedRow is not null;

   /// <summary>Whether nothing is open in the editor, to show the hint instead.</summary>
   public bool HasNoSelection => SelectedRow is null;

   /// <summary>Whether the editor shows a draft that has not been saved.</summary>
   public bool IsDraftSelected => SelectedRow?.IsDraft == true;

   /// <summary>Whether the editor shows the system type.</summary>
   public bool IsSystemSelected => SelectedRow?.IsSystem == true;

   /// <summary>Whether the code can be edited; the code of the system type is fixed.</summary>
   public bool CanEditCode => HasSelection && !IsSystemSelected;

   /// <summary>Whether the kind is fixed: the system type, or a type that already has products.</summary>
   public bool IsKindLocked => SelectedRow is { } item && (item.IsSystem || (!item.IsDraft && item.ProductCount > 0));

   /// <summary>Whether the kind can be chosen.</summary>
   public bool CanEditKind => HasSelection && !IsKindLocked;

   /// <summary>The kind as text, for the read-only box shown while the kind is locked.</summary>
   public string KindCaption => SelectedRow is { } item ? ProductKindOption.CaptionOf(item.IsDraft ? EditKind?.Value ?? item.Row.cTenantProductTypeKind : item.Row.cTenantProductTypeKind) : "";

   /// <summary>Why the kind cannot be chosen, shown under the field.</summary>
   public string KindHint => SelectedRow is not { } item ? "" :
      item.IsSystem ? "The system creates this product type. Only its name and note can change." :
      IsKindLocked ? "The kind cannot change because this product type already has products." :
      "The kind belongs to the product type; every product follows the kind of its type.";

   /// <summary>State of the edited product type as text; it changes with Deactivate and Activate on the toolbar.</summary>
   public string StateCaption => SelectedRow is not { } item ? "" : item.IsDraft ? "Active after save" : item.IsInactive ? "Inactive" : "Active";

   /// <summary>Whether the editor differs from the saved product type; a draft always counts as changed.</summary>
   public bool IsDirty => SelectedRow is { } item && (item.IsDraft || EditCode != item.Row.cTenantProductTypeCode || EditName != item.Row.cTenantProductTypeName ||
      (!item.IsSystem && EditKind?.Value != item.Row.cTenantProductTypeKind) || (EditNote ?? "") != (item.Row.cTenantProductTypeNote ?? ""));

   /// <summary>Caption of the state button: Deactivate for an active product type, Activate for an inactive one.</summary>
   public string ToggleStateCaption => SelectedRow?.IsInactive == true ? "Activate" : "Deactivate";

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

   /// <summary>Whether a new product type can be started.</summary>
   public bool NewProductTypeCommandAllowed() => CanBrowse;

   /// <summary>Starts a new product type as a draft row.</summary>
   public void NewProductTypeCommand() {
      if (!NewProductTypeCommandAllowed() || !CanLeave()) return;
      DropDraft(null);
      var draft = new ProductTypeItem(new vi_TenantProductType { cTenantId = SelectedTenant!.cTenantId, cTenantProductTypeKind = ProductKind.Goods, cTenantProductTypeState = ta_TenantProductType.StateActive });
      Rows.Add(draft);
      Open(draft);
      Status = "";
   }

   /// <summary>Whether the editor holds changes that can be saved.</summary>
   public bool SaveCommandAllowed() => CanEdit && IsDirty && !string.IsNullOrWhiteSpace(EditCode) && !string.IsNullOrWhiteSpace(EditName) && (IsSystemSelected || EditKind is not null);

   /// <summary>Creates the draft or saves the changes of the selected product type, then reloads the list.</summary>
   public Task SaveCommand() => RunAsync("Saving product type...", async () => {
      if (SelectedRow is not { } item || Service is not { } service) return;
      var row = item.Row; var draft = item.IsDraft;
      var change = new ta_TenantProductType {
         cTenantProductTypeId = row.cTenantProductTypeId, cTenantId = row.cTenantId, cTenantProductTypeCode = item.IsSystem ? row.cTenantProductTypeCode : EditCode,
         cTenantProductTypeName = EditName, cTenantProductTypeKind = item.IsSystem ? row.cTenantProductTypeKind : EditKind!.Value,
         cTenantProductTypeNote = string.IsNullOrEmpty(EditNote) ? null : EditNote, cTenantProductTypeState = row.cTenantProductTypeState,
         cTenantProductTypeOrder = row.cTenantProductTypeOrder, json_object = row.json_object, ustamp = row.ustamp
      };
      var saved = draft ? await service.PostGetTa_TenantProductType_New(change) : await service.PostGetTa_TenantProductType_Update(change);
      await Load(saved.cTenantProductTypeId);
      Status = draft ? $"Product type '{saved.cTenantProductTypeCode}' created." : $"Product type '{saved.cTenantProductTypeCode}' saved.";
   });

   /// <summary>Whether the selected saved product type, without unsaved changes, can be deactivated or activated.</summary>
   public bool ToggleStateCommandAllowed() => CanEdit && !IsDraftSelected && !IsDirty && !IsSystemSelected;

   /// <summary>Stops or resumes offering the selected product type; its products keep it and keep their own state.</summary>
   public async Task ToggleStateCommand() {
      if (!ToggleStateCommandAllowed() || SelectedRow is not { } item) return;
      var row = item.Row; var activate = item.IsInactive;
      if (!activate && ConfirmDeactivate?.Invoke(row) == false) return;
      await RunAsync("Saving product type...", async () => {
         if (Service is not { } service) return;
         var saved = await service.PostGetTa_TenantProductType_Update(new ta_TenantProductType {
            cTenantProductTypeId = row.cTenantProductTypeId, cTenantId = row.cTenantId, cTenantProductTypeCode = row.cTenantProductTypeCode,
            cTenantProductTypeName = row.cTenantProductTypeName, cTenantProductTypeKind = row.cTenantProductTypeKind, cTenantProductTypeNote = row.cTenantProductTypeNote,
            cTenantProductTypeOrder = row.cTenantProductTypeOrder, json_object = row.json_object,
            cTenantProductTypeState = activate ? ta_TenantProductType.StateActive : ta_TenantProductType.StateInactive, ustamp = row.ustamp
         });
         await Load(saved.cTenantProductTypeId);
         Status = activate ? $"Product type '{saved.cTenantProductTypeCode}' activated." : $"Product type '{saved.cTenantProductTypeCode}' deactivated.";
      });
   }

   /// <summary>Whether the list can be read again.</summary>
   public bool RefreshCommandAllowed() => IsNotBusy && SelectedTenant is not null;

   /// <summary>Reads the product types again, asking first when the editor holds unsaved changes.</summary>
   public Task RefreshCommand() => CanLeave() ? ReloadAsync() : Task.CompletedTask;

   /// <summary>Reads the selected tenant again, keeping the selected product type when it still exists.</summary>
   public Task ReloadAsync() => RunAsync("Loading product types...", async () => {
      var selected = SelectedRow is { IsDraft: false } item ? item.Row.cTenantProductTypeId : null;
      await Load(selected);
      Status = "";
   });

   private async Task Load(string? selectId) {
      if (Service is not { } service || SelectedTenant is null) return;
      var rows = await service.GetVi_TenantProductTypes(SelectedTenant.cTenantId);
      var items = rows.Select(x => new ProductTypeItem(x)).ToArray();
      _refreshingRows = true;
      try {
         Rows.Clear();
         foreach (var item in items) Rows.Add(item);
      }
      finally { _refreshingRows = false; }
      Open(items.FirstOrDefault(x => string.Equals(x.Row.cTenantProductTypeId, selectId, StringComparison.OrdinalIgnoreCase)));
      IsLoaded = true;
   }

   // Removes a draft row other than the one being opened.
   private void DropDraft(ProductTypeItem? keep) {
      if (Rows.FirstOrDefault(x => x.IsDraft) is not { } draft || ReferenceEquals(draft, keep)) return;
      _refreshingRows = true;
      try { Rows.Remove(draft); }
      finally { _refreshingRows = false; }
   }

   private void Open(ProductTypeItem? item) {
      _opening = true;
      try {
         SelectedRow = item;
         EditCode = item?.Row.cTenantProductTypeCode ?? ""; EditName = item?.Row.cTenantProductTypeName ?? ""; EditNote = item?.Row.cTenantProductTypeNote;
         EditKind = item is null ? null : KindOptions.FirstOrDefault(x => x.Value == item.Row.cTenantProductTypeKind);
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
      foreach (var name in (string[])[nameof(HasSelection), nameof(HasNoSelection), nameof(IsDraftSelected), nameof(IsSystemSelected), nameof(CanEditCode), nameof(IsKindLocked),
                  nameof(CanEditKind), nameof(KindCaption), nameof(KindHint), nameof(StateCaption), nameof(IsDirty), nameof(ToggleStateCaption), nameof(CanBrowse), nameof(CanEdit)])
         NotifyChanged(name);
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
   }
}
