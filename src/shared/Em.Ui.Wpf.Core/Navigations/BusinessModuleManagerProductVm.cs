using System.Collections.ObjectModel;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Navigations;

/// <summary>A product type choice: of the tree filter (where no id means every type) or of the editor.</summary>
/// <param name="Id">Product type ID; <c>null</c> = every type.</param>
/// <param name="Caption">Text shown in the list.</param>
public sealed record ProductTypeOption(string? Id, string Caption);

/// <summary>One product in the tree of the Products page, a draft that is not saved yet, or a row that stands for children not read yet.</summary>
public sealed class ProductNode : MvvmModelBase
{
   private readonly BusinessModuleManagerProductVm _owner;

   internal ProductNode(BusinessModuleManagerProductVm owner, vi_TenantProduct product, ProductNode? parent, bool isDraft = false, bool isPlaceholder = false) {
      _owner = owner; Product = product; Parent = parent; IsDraft = isDraft; IsPlaceholder = isPlaceholder;
   }

   /// <summary>The product row as last read; for a draft, its starting values.</summary>
   public vi_TenantProduct Product { get => Get<vi_TenantProduct>()!; private set => Set(value); }

   /// <summary>Parent node; <c>null</c> for a root product.</summary>
   public ProductNode? Parent { get; }

   /// <summary>Child nodes in display order; one placeholder until the children are read.</summary>
   public ObservableCollection<ProductNode> Children { get; } = [];

   /// <summary>Whether this node is a new product that has not been saved.</summary>
   public bool IsDraft { get; }

   /// <summary>Whether this row only stands for children that are not read yet.</summary>
   public bool IsPlaceholder { get; }

   /// <summary>Whether the children were read from the server.</summary>
   public bool ChildrenLoaded { get; internal set; }

   /// <summary>Name shown in the tree.</summary>
   public string DisplayName => IsPlaceholder ? "Loading..." : IsDraft ? "(new product)" : Product.cTenantProductName;

   /// <summary>Code shown next to the name.</summary>
   public string DisplayCode => IsDraft || IsPlaceholder ? "" : Product.cTenantProductCode;

   /// <summary>Number of direct children, shown when there are any.</summary>
   public string ChildCountCaption => IsDraft || IsPlaceholder || Product.ccTenantProductChildCount == 0 ? "" : Product.ccTenantProductChildCount.ToString();

   /// <summary>Whether the product is deactivated.</summary>
   public bool IsInactive => !IsDraft && !IsPlaceholder && Product.cTenantProductState < ta_TenantProduct.StateActive;

   /// <summary>Whether this is the product the system creates; only its name and note can change.</summary>
   public bool IsSystem => Product.cvTenantProductTypeKind == ProductKind.GenericTransaction;

   /// <summary>Whether the children are shown; opening a node reads its children the first time.</summary>
   public bool IsExpanded {
      get => Get<bool>();
      set { if (Set(value) && value) _owner.OnNodeExpanded(this); }
   }

   /// <summary>Whether this node is selected in the tree; selecting it opens it in the editor.</summary>
   public bool IsSelected {
      get => Get<bool>();
      set { if (Set(value) && value) _owner.OnNodeSelected(this); }
   }

   /// <summary>This node and every node below it that was read.</summary>
   public IEnumerable<ProductNode> Descendants() {
      yield return this;
      foreach (var child in Children)
         foreach (var node in child.Descendants()) yield return node;
   }

   // Changes the selection mark without opening the node in the editor.
   internal void SelectQuietly(bool value) => Set(value, propertyName: nameof(IsSelected));

   // Opens the node without asking the owner to read its children.
   internal void ExpandQuietly() => Set(true, propertyName: nameof(IsExpanded));

   // Replaces the row after a save or a reload and refreshes the texts built from it.
   internal void Update(vi_TenantProduct product) {
      Product = product;
      foreach (var name in (string[])[nameof(DisplayName), nameof(DisplayCode), nameof(ChildCountCaption), nameof(IsInactive), nameof(IsSystem)]) NotifyChanged(name);
   }
}

/// <summary>State and commands of the Products page: the product tree of one tenant, read one level at a time.</summary>
public sealed class BusinessModuleManagerProductVm : MvvmModelBase
{
   private static readonly ProductTypeOption AllTypes = new(null, "All product types");
   private readonly ITenantProductServices? _service;
   private readonly ITenantProductTypeServices? _typeService;
   private readonly IUnitServices? _unitService;
   private vi_TenantProductType[] _types = [];
   private ProductNode? _draft;
   private bool _restoringSelection;
   private bool _settingFilter;

   /// <summary>Creates a view model for navigation; the services are taken from the application.</summary>
   public BusinessModuleManagerProductVm() { RegisterCommands(); }

   /// <summary>Creates a view model with explicit services, for tests and other hosts.</summary>
   public BusinessModuleManagerProductVm(ITenantProductServices service, ITenantProductTypeServices typeService, IUnitServices unitService) {
      _service = service; _typeService = typeService; _unitService = unitService; RegisterCommands();
   }

   private ITenantProductServices? Service => _service ?? EmApp?.ServiceProvider.GetRequiredService<ITenantProductServices>();
   private ITenantProductTypeServices? TypeService => _typeService ?? EmApp?.ServiceProvider.GetRequiredService<ITenantProductTypeServices>();
   private IUnitServices? UnitService => _unitService ?? EmApp?.ServiceProvider.GetRequiredService<IUnitServices>();

   private void RegisterCommands() {
      TypeFilterOptions.Add(AllTypes);
      Set(AllTypes, propertyName: nameof(TypeFilter));
      RegisterCommand(nameof(NewRootCommand), NewRootCommand, NewRootCommandAllowed);
      RegisterCommand(nameof(AddChildCommand), AddChildCommand, AddChildCommandAllowed);
      RegisterCommand(nameof(AddChildrenCommand), AddChildrenCommand, AddChildrenCommandAllowed);
      RegisterCommand(nameof(CopyChildrenCommand), CopyChildrenCommand, CopyChildrenCommandAllowed);
      RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
      RegisterCommand(nameof(ToggleStateCommand), ToggleStateCommand, ToggleStateCommandAllowed);
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
      RegisterCommand(nameof(SearchCommand), SearchCommand, SearchCommandAllowed);
      RegisterCommand(nameof(ClearSearchCommand), ClearSearchCommand, ClearSearchCommandAllowed);
      RegisterCommand<TenantProductSearchHit>(nameof(OpenHitCommand), OpenHitCommand, OpenHitCommandAllowed);
   }

   #region Tenant and lookups

   /// <summary>Tenants available in the toolbar.</summary>
   public ObservableCollection<vi_Tenant> TenantChoices { get; } = [];

   /// <summary>The tenant being managed.</summary>
   public vi_Tenant? SelectedTenant {
      get => Get<vi_Tenant>();
      set {
         if (ReferenceEquals(value, SelectedTenant)) return;
         if (IsBusy || !CanLeave()) { NotifyChanged(); return; }
         Set(value); ResetTree(); IsLoaded = false;
         SetFilterQuietly(AllTypes);
         Loading = ReloadAsync(); UpdateState();
      }
   }

   /// <summary>Most recent load, also awaitable by the owning screen.</summary>
   public Task Loading { get; set; } = Task.CompletedTask;

   /// <summary>Host-supplied question asked before unsaved changes are discarded; <c>true</c> = discard.</summary>
   public Func<bool>? ConfirmDiscard { get; set; }

   /// <summary>Host-supplied question asked before a product is deactivated; <c>true</c> = deactivate.</summary>
   public Func<vi_TenantProduct, bool>? ConfirmDeactivate { get; set; }

   /// <summary>Host dialog that collects new children of a product; <c>null</c> = cancelled.</summary>
   public Func<vi_TenantProduct, TenantProductBatch?>? PickChildren { get; set; }

   /// <summary>Host dialog that chooses the children to copy under a product; <c>null</c> = cancelled.</summary>
   public Func<vi_TenantProduct, TenantProductCopy?>? PickCopy { get; set; }

   /// <summary>Choices of the tree filter: every type, then each type of the tenant.</summary>
   public ObservableCollection<ProductTypeOption> TypeFilterOptions { get; } = [];

   /// <summary>The product type the tree is limited to; changing it reads the tree again.</summary>
   public ProductTypeOption? TypeFilter {
      get => Get<ProductTypeOption>();
      set {
         if (_settingFilter) { Set(value); return; }
         var choice = value ?? AllTypes;
         if (ReferenceEquals(choice, TypeFilter)) return;
         if (IsBusy || !CanLeave()) { NotifyChanged(); return; }
         Set(choice);
         if (SelectedTenant is not null) Loading = RunAsync("Loading products...", async () => { ResetTree(); await LoadRoots(); });
      }
   }

   /// <summary>Product types offered by the editor: the active ones, plus the type the product already has.</summary>
   public ObservableCollection<ProductTypeOption> TypeOptions { get; } = [];

   /// <summary>Units offered by the editor.</summary>
   public ObservableCollection<vi_Unit> UnitOptions { get; } = [];

   #endregion

   #region Tree

   /// <summary>Root nodes of the product tree, including a root draft.</summary>
   public ObservableCollection<ProductNode> RootNodes { get; } = [];

   /// <summary>The node open in the editor.</summary>
   public ProductNode? SelectedNode { get => Get<ProductNode>(); private set { Set(value); UpdateState(); } }

   /// <summary>Every node that was read, depth first.</summary>
   public IEnumerable<ProductNode> AllNodes => RootNodes.SelectMany(n => n.Descendants());

   internal void OnNodeSelected(ProductNode node) {
      if (_restoringSelection || ReferenceEquals(node, SelectedNode)) return;
      var previous = SelectedNode;
      if (node.IsPlaceholder || (IsDirty && ConfirmDiscard?.Invoke() == false)) {
         // Keep the edited node selected: the tree already moved, so move it back.
         _restoringSelection = true;
         try { node.SelectQuietly(false); previous?.SelectQuietly(true); }
         finally { _restoringSelection = false; }
         return;
      }
      if (_draft is not null && !ReferenceEquals(node, _draft)) RemoveDraft();
      foreach (var other in AllNodes.Where(n => !ReferenceEquals(n, node))) other.SelectQuietly(false);
      Open(node);
   }

   internal void OnNodeExpanded(ProductNode node) {
      if (node.ChildrenLoaded || node.IsDraft || node.IsPlaceholder) return;
      Loading = LoadChildren(node);
   }

   #endregion

   #region Search

   /// <summary>Text looked for in product codes and names.</summary>
   public string SearchText { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Products found by the last search.</summary>
   public ObservableCollection<TenantProductSearchHit> SearchResults { get; } = [];

   /// <summary>Whether the search results are shown in place of the tree.</summary>
   public bool IsSearching { get => Get<bool>(); private set => Set(value, _ => { NotifyChanged(nameof(IsBrowsingTree)); UpdateState(); }); }

   /// <summary>Whether the tree is shown.</summary>
   public bool IsBrowsingTree => !IsSearching;

   /// <summary>How many products the last search found.</summary>
   public string SearchCaption { get => Get<string>() ?? ""; private set => Set(value); }

   /// <summary>Whether a search can start.</summary>
   public bool SearchCommandAllowed() => CanBrowse && !string.IsNullOrWhiteSpace(SearchText);

   /// <summary>Looks for products by code or name, within the type of the filter when one is chosen.</summary>
   public async Task SearchCommand() {
      if (!SearchCommandAllowed() || !CanLeave()) return;
      await RunAsync("Searching products...", async () => {
         if (Service is not { } service || SelectedTenant is not { } tenant) return;
         var hits = await service.GetMeta_TenantProduct_Search(tenant.cTenantId, SearchText.Trim(), TypeFilter?.Id);
         DiscardChanges();
         SearchResults.Clear();
         foreach (var hit in hits) SearchResults.Add(hit);
         SearchCaption = hits.Length == 0 ? "No product has this text in its code or name." :
            hits.Length >= 200 ? "The first 200 products found. Enter more of the code or name to narrow the search." :
            hits.Length == 1 ? "1 product found. Choose it to open it in the tree." : $"{hits.Length} products found. Choose one to open it in the tree.";
         IsSearching = true;
         Status = "";
      });
   }

   /// <summary>Whether the search results can be closed.</summary>
   public bool ClearSearchCommandAllowed() => IsSearching && IsNotBusy;

   /// <summary>Closes the search results and shows the tree again.</summary>
   public void ClearSearchCommand() { SearchResults.Clear(); SearchText = ""; IsSearching = false; }

   /// <summary>Whether a search result can be opened.</summary>
   public bool OpenHitCommandAllowed(TenantProductSearchHit? hit) => hit is not null && IsNotBusy;

   /// <summary>Opens the tree down to a product that was found and selects it.</summary>
   public async Task OpenHitCommand(TenantProductSearchHit? hit) {
      if (hit is null || !OpenHitCommandAllowed(hit)) return;
      await RunAsync("Opening product...", async () => {
         // A parent of another type is hidden by the filter, so the way down is walked without it.
         if (TypeFilter?.Id is not null) { SetFilterQuietly(AllTypes); ResetTree(); await LoadRoots(); }
         SearchResults.Clear(); SearchText = ""; IsSearching = false;
         await OpenPath([.. hit.ParentIds, hit.Row.cTenantProductId]);
      });
   }

   #endregion

   #region Editor

   /// <summary>Code of the edited product.</summary>
   public string EditCode { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Name of the edited product.</summary>
   public string EditName { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Note of the edited product.</summary>
   public string? EditNote { get => Get<string>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Product type of the edited product.</summary>
   public ProductTypeOption? EditType { get => Get<ProductTypeOption>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Unit of the edited product.</summary>
   public vi_Unit? EditUnit { get => Get<vi_Unit>(); set => Set(value, _ => UpdateState()); }

   /// <summary>The way from the root to the edited product, as one line of names.</summary>
   public string PathCaption {
      get {
         if (SelectedNode is not { } node) return "";
         var names = new List<string>();
         for (var up = node; up is not null; up = up.Parent) names.Add(up.IsDraft ? "(new product)" : up.Product.cTenantProductName);
         names.Reverse();
         return string.Join("  ›  ", names);
      }
   }

   /// <summary>Parent of the edited product as text; the parent cannot change after the product is created.</summary>
   public string ParentCaption => SelectedNode is not { } node ? "" : node.Parent is { } parent ? $"{parent.Product.cTenantProductName} ({parent.Product.cTenantProductCode})" : "(none - root product)";

   /// <summary>State of the edited product as text; it changes with Deactivate and Activate on the toolbar.</summary>
   public string StateCaption => SelectedNode is not { } node ? "" : node.IsDraft ? "Active after save" : node.IsInactive ? "Inactive" : "Active";

   /// <summary>Whether a node is open in the editor.</summary>
   public bool HasSelection => SelectedNode is not null;

   /// <summary>Whether nothing is open in the editor, to show the hint instead.</summary>
   public bool HasNoSelection => SelectedNode is null;

   /// <summary>Whether the editor shows a draft that has not been saved.</summary>
   public bool IsDraftSelected => SelectedNode?.IsDraft == true;

   /// <summary>Whether the editor shows the system product.</summary>
   public bool IsSystemSelected => SelectedNode is { IsDraft: false, IsSystem: true };

   /// <summary>Whether the code and the product type can be edited; those of the system product are fixed.</summary>
   public bool CanEditIdentity => HasSelection && !IsSystemSelected;

   /// <summary>Whether the editor differs from the saved product; a draft always counts as changed.</summary>
   public bool IsDirty => SelectedNode is { } node && (node.IsDraft || EditCode != node.Product.cTenantProductCode || EditName != node.Product.cTenantProductName ||
      (EditNote ?? "") != (node.Product.cTenantProductNote ?? "") || !Same(EditType?.Id, node.Product.cTenantProductTypeId) || !Same(EditUnit?.cUnitId, node.Product.cUnitId));

   /// <summary>Caption of the state button: Deactivate for an active product, Activate for an inactive one.</summary>
   public string ToggleStateCaption => SelectedNode?.IsInactive == true ? "Activate" : "Deactivate";

   /// <summary>Whether the toolbar and tree accept input.</summary>
   public bool CanBrowse => SelectedTenant is not null && IsLoaded && IsNotBusy;

   /// <summary>Whether the editor fields accept input.</summary>
   public bool CanEdit => CanBrowse && HasSelection;

   /// <summary>True after the first successful read of the tenant.</summary>
   public bool IsLoaded { get => Get<bool>(); private set => Set(value); }

   /// <summary>Result or server error shown below the toolbar.</summary>
   public string Status { get => Get<string>() ?? ""; private set => Set(value); }

   /// <summary>Checks whether unsaved changes may be discarded.</summary>
   public bool CanLeave() => !IsDirty || ConfirmDiscard?.Invoke() != false;

   /// <summary>Restores the last saved values after the owning screen confirmed discard.</summary>
   public void DiscardChanges() {
      if (!IsDirty) return;
      if (SelectedNode is { IsDraft: false } node) Open(node);
      else { RemoveDraft(); SelectedNode = null; ClearEditor(); }
   }

   #endregion

   #region Commands

   /// <summary>Whether a new root product can be started.</summary>
   public bool NewRootCommandAllowed() => CanBrowse && IsBrowsingTree && UnitOptions.Count > 0 && _types.Any(Offered);

   /// <summary>Starts a new root product as a draft node.</summary>
   public Task NewRootCommand() => StartDraft(null);

   /// <summary>Whether a new child of the selected saved product can be started.</summary>
   public bool AddChildCommandAllowed() => CanBrowse && IsBrowsingTree && SelectedNode is { IsDraft: false, IsSystem: false } && UnitOptions.Count > 0 && _types.Any(Offered);

   /// <summary>Starts a new product under the selected product as a draft node.</summary>
   public Task AddChildCommand() => SelectedNode is { IsDraft: false } parent ? StartDraft(parent) : Task.CompletedTask;

   /// <summary>Whether several children can be added to the selected saved product.</summary>
   public bool AddChildrenCommandAllowed() => CanBrowse && IsBrowsingTree && SelectedNode is { IsDraft: false, IsSystem: false } && !IsDirty;

   /// <summary>Lets the host collect new children of the selected product, then creates them in one save.</summary>
   public async Task AddChildrenCommand() {
      if (!AddChildrenCommandAllowed() || SelectedNode is not { } parent || PickChildren?.Invoke(parent.Product) is not { } batch) return;
      await RunAsync("Adding children...", async () => {
         if (Service is not { } service) return;
         var created = await service.PostGetMeta_TenantProduct_AddChildren(batch);
         await RefreshBranch(parent);
         Status = created.Length == 1 ? "Added 1 child." : $"Added {created.Length} children.";
      });
   }

   /// <summary>Whether children of another product can be copied under the selected saved product.</summary>
   public bool CopyChildrenCommandAllowed() => CanBrowse && IsBrowsingTree && SelectedNode is { IsDraft: false, IsSystem: false } && !IsDirty;

   /// <summary>Lets the host choose direct children of another product, then copies them under the selected product.</summary>
   public async Task CopyChildrenCommand() {
      if (!CopyChildrenCommandAllowed() || SelectedNode is not { } parent || PickCopy?.Invoke(parent.Product) is not { } copy) return;
      await RunAsync("Copying children...", async () => {
         if (Service is not { } service) return;
         var created = await service.PostGetMeta_TenantProduct_CopyChildren(copy);
         await RefreshBranch(parent);
         Status = created.Length == 1 ? "Copied 1 child." : $"Copied {created.Length} children.";
      });
   }

   /// <summary>Whether the editor holds changes that can be saved.</summary>
   public bool SaveCommandAllowed() => CanEdit && IsDirty && !string.IsNullOrWhiteSpace(EditCode) && !string.IsNullOrWhiteSpace(EditName) && EditType?.Id is not null && EditUnit is not null;

   /// <summary>Creates the draft or saves the changes of the selected product, then refreshes its place in the tree.</summary>
   public Task SaveCommand() => RunAsync("Saving product...", async () => {
      if (SelectedNode is not { } node || Service is not { } service || SelectedTenant is not { } tenant) return;
      var product = node.Product;
      var row = new ta_TenantProduct {
         cTenantProductId = node.IsDraft ? "" : product.cTenantProductId, cTenantId = tenant.cTenantId,
         cTenantProductTypeId = EditType!.Id!, cTenantProductParentId = node.Parent?.Product.cTenantProductId, cUnitId = EditUnit!.cUnitId,
         cTenantProductCode = EditCode, cTenantProductName = EditName, cTenantProductNote = string.IsNullOrEmpty(EditNote) ? null : EditNote,
         cTenantProductState = node.IsDraft ? ta_TenantProduct.StateActive : product.cTenantProductState,
         cTenantProductOrder = node.IsDraft ? -1 : product.cTenantProductOrder,
         // The extension data belongs to the application that builds on the product; it is sent back as it was read.
         json_object = product.json_object, ustamp = product.ustamp
      };
      if (node.IsDraft) {
         var saved = await service.PostGetTa_TenantProduct_New(row);
         var parent = node.Parent;
         _draft = null;
         await RefreshBranch(parent, saved.cTenantProductId);
         Status = $"Product '{saved.cTenantProductCode}' created.";
      }
      else {
         var saved = await service.PostGetTa_TenantProduct_Update(row);
         node.Update(saved);
         Open(node);
         Status = $"Product '{saved.cTenantProductCode}' saved.";
      }
   });

   /// <summary>Whether the selected saved product, without unsaved changes, can be deactivated or activated.</summary>
   public bool ToggleStateCommandAllowed() => CanBrowse && IsBrowsingTree && SelectedNode is { IsDraft: false, IsSystem: false } && !IsDirty;

   /// <summary>Deactivates an active product or activates an inactive one; its children keep their own state.</summary>
   public async Task ToggleStateCommand() {
      if (!ToggleStateCommandAllowed() || SelectedNode is not { } node) return;
      var p = node.Product; var activate = node.IsInactive;
      if (!activate && ConfirmDeactivate?.Invoke(p) == false) return;
      await RunAsync("Saving product...", async () => {
         if (Service is not { } service) return;
         var saved = await service.PostGetTa_TenantProduct_Update(new ta_TenantProduct {
            cTenantProductId = p.cTenantProductId, cTenantId = p.cTenantId, cTenantProductTypeId = p.cTenantProductTypeId, cTenantProductParentId = p.cTenantProductParentId,
            cUnitId = p.cUnitId, cTenantProductCode = p.cTenantProductCode, cTenantProductName = p.cTenantProductName, cTenantProductNote = p.cTenantProductNote,
            cTenantProductOrder = p.cTenantProductOrder, json_object = p.json_object,
            cTenantProductState = activate ? ta_TenantProduct.StateActive : ta_TenantProduct.StateInactive, ustamp = p.ustamp
         });
         node.Update(saved);
         Open(node);
         Status = activate ? $"Product '{saved.cTenantProductCode}' activated." : $"Product '{saved.cTenantProductCode}' deactivated.";
      });
   }

   /// <summary>Whether the tree can be read again.</summary>
   public bool RefreshCommandAllowed() => IsNotBusy && SelectedTenant is not null;

   /// <summary>Reads the tree again, asking first when the editor holds unsaved changes.</summary>
   public Task RefreshCommand() => CanLeave() ? ReloadAsync() : Task.CompletedTask;

   #endregion

   /// <summary>Reads the product types, the units and the root products again, then reopens the selected product when it still exists.</summary>
   public Task ReloadAsync() => RunAsync("Loading products...", async () => {
      if (Service is not { } service || SelectedTenant is not { } tenant) return;
      var selected = SelectedNode is { IsDraft: false } node ? node.Product.cTenantProductId : null;
      await LoadLookups(tenant.cTenantId);
      ResetTree();
      await LoadRoots();
      IsLoaded = true;
      SearchResults.Clear(); IsSearching = false;
      if (selected is not null) {
         vi_TenantProduct[] path;
         try { path = await service.GetMeta_TenantProduct_Path(selected); }
         // The product may be gone, or hidden by the filter; the tree then simply opens with nothing selected.
         catch (ActionException) { path = []; }
         await OpenPath([.. path.Select(x => x.cTenantProductId)]);
      }
      Status = "";
   });

   private async Task LoadLookups(string tenantId) {
      if (TypeService is not { } types || UnitService is not { } units) return;
      _types = await types.GetVi_TenantProductTypes(tenantId);
      var filter = TypeFilter?.Id;
      TypeFilterOptions.Clear();
      TypeFilterOptions.Add(AllTypes);
      foreach (var type in _types) TypeFilterOptions.Add(new(type.cTenantProductTypeId, type.cTenantProductTypeState < ta_TenantProductType.StateActive ? $"{type.cTenantProductTypeName} - inactive" : type.cTenantProductTypeName));
      SetFilterQuietly(TypeFilterOptions.FirstOrDefault(x => Same(x.Id, filter)) ?? AllTypes);
      UnitOptions.Clear();
      foreach (var unit in await units.GetVi_Units()) UnitOptions.Add(unit);
   }

   private async Task LoadRoots() {
      if (Service is not { } service || SelectedTenant is not { } tenant) return;
      Merge(RootNodes, null, await service.GetVi_TenantProducts_Children(tenant.cTenantId, null, TypeFilter?.Id));
   }

   private async Task LoadChildren(ProductNode node) {
      if (Service is not { } service || SelectedTenant is not { } tenant) return;
      try {
         var rows = await service.GetVi_TenantProducts_Children(tenant.cTenantId, node.Product.cTenantProductId, TypeFilter?.Id);
         node.ChildrenLoaded = true;
         Merge(node.Children, node, rows);
      }
      catch (ActionException ex) { Status = ex.Message.Replace("Server Error: ", ""); }
      catch (Exception ex) {
         Status = "The children could not be read. Refresh and try again.";
         if (EmApp is not null) AlertError(ex);
      }
   }

   // Brings a level of the tree in line with the rows read, keeping the nodes (and what was opened below them) that are still there.
   private void Merge(ObservableCollection<ProductNode> nodes, ProductNode? parent, vi_TenantProduct[] rows) {
      var known = nodes.Where(n => !n.IsDraft && !n.IsPlaceholder).ToDictionary(n => n.Product.cTenantProductId, StringComparer.OrdinalIgnoreCase);
      var drafts = nodes.Where(n => n.IsDraft).ToArray();
      nodes.Clear();
      foreach (var row in rows) {
         if (known.TryGetValue(row.cTenantProductId, out var node)) node.Update(row);
         else node = new ProductNode(this, row, parent);
         // A node that has children but was never opened shows one placeholder, so the tree offers to open it.
         if (!node.ChildrenLoaded) {
            node.Children.Clear();
            if (row.ccTenantProductChildCount > 0) node.Children.Add(new ProductNode(this, row, node, isPlaceholder: true));
         }
         nodes.Add(node);
      }
      foreach (var draft in drafts) nodes.Add(draft);
   }

   // Reads a product and its children again after its children changed, then selects a product of that level.
   private async Task RefreshBranch(ProductNode? parent, string? selectId = null) {
      if (Service is not { } service || SelectedTenant is not { } tenant) return;
      if (parent is null) await LoadRoots();
      else {
         if (await service.GetVi_TenantProduct_ById(parent.Product.cTenantProductId) is { } row) parent.Update(row);
         await LoadChildren(parent);
         parent.ExpandQuietly();
      }
      var level = parent?.Children ?? RootNodes;
      var select = selectId is null ? parent : level.FirstOrDefault(n => !n.IsDraft && Same(n.Product.cTenantProductId, selectId)) ?? parent;
      foreach (var other in AllNodes) other.SelectQuietly(false);
      if (select is null) { SelectedNode = null; ClearEditor(); return; }
      select.SelectQuietly(true);
      Open(select);
   }

   // Opens the tree along a list of ids, root first, and selects the last one that is found.
   private async Task OpenPath(string[] ids) {
      ProductNode? found = null;
      var level = RootNodes;
      foreach (var id in ids) {
         if (level.FirstOrDefault(n => !n.IsDraft && !n.IsPlaceholder && Same(n.Product.cTenantProductId, id)) is not { } node) break;
         found = node;
         if (Same(id, ids[^1])) break;
         if (!node.ChildrenLoaded) await LoadChildren(node);
         node.ExpandQuietly();
         level = node.Children;
      }
      foreach (var other in AllNodes) other.SelectQuietly(false);
      if (found is null) { SelectedNode = null; ClearEditor(); return; }
      found.SelectQuietly(true);
      Open(found);
   }

   private async Task StartDraft(ProductNode? parent) {
      if (!CanBrowse || (IsDirty && ConfirmDiscard?.Invoke() == false)) return;
      RemoveDraft();
      await RunAsync("Loading products...", async () => {
         if (parent is { ChildrenLoaded: false }) await LoadChildren(parent);
         // A new child takes the type and unit of its parent; a new root takes the type of the filter.
         var draft = new ProductNode(this, new vi_TenantProduct {
            cTenantId = SelectedTenant!.cTenantId, cTenantProductParentId = parent?.Product.cTenantProductId, cTenantProductState = ta_TenantProduct.StateActive,
            cTenantProductTypeId = parent?.Product.cTenantProductTypeId ?? TypeFilter?.Id ?? "", cUnitId = parent?.Product.cUnitId ?? ""
         }, parent, isDraft: true);
         _draft = draft;
         if (parent is null) RootNodes.Add(draft);
         else { parent.Children.Add(draft); parent.ExpandQuietly(); }
         foreach (var other in AllNodes.Where(n => !ReferenceEquals(n, draft))) other.SelectQuietly(false);
         draft.SelectQuietly(true);
         Open(draft);
         Status = "";
      });
   }

   private void RemoveDraft() {
      if (_draft is not { } draft) return;
      _draft = null;
      if (draft.Parent is { } parent) parent.Children.Remove(draft); else RootNodes.Remove(draft);
   }

   private void ResetTree() {
      _draft = null;
      RootNodes.Clear();
      SelectedNode = null;
      ClearEditor();
   }

   private void SetFilterQuietly(ProductTypeOption option) {
      _settingFilter = true;
      try { TypeFilter = option; }
      finally { _settingFilter = false; }
   }

   // A type a new product may take: active and not the system type.
   private static bool Offered(vi_TenantProductType type) => type.cTenantProductTypeState == ta_TenantProductType.StateActive && type.cTenantProductTypeKind != ProductKind.GenericTransaction;

   private void Open(ProductNode node) {
      SelectedNode = node;
      var p = node.Product;
      TypeOptions.Clear();
      foreach (var type in _types.Where(t => Offered(t) || Same(t.cTenantProductTypeId, p.cTenantProductTypeId)))
         TypeOptions.Add(new(type.cTenantProductTypeId, type.cTenantProductTypeState < ta_TenantProductType.StateActive ? $"{type.cTenantProductTypeName} - inactive" : type.cTenantProductTypeName));
      EditCode = p.cTenantProductCode;
      EditName = p.cTenantProductName;
      EditNote = p.cTenantProductNote;
      EditType = TypeOptions.FirstOrDefault(o => Same(o.Id, p.cTenantProductTypeId)) ?? (node.IsDraft ? TypeOptions.FirstOrDefault() : null);
      EditUnit = UnitOptions.FirstOrDefault(u => Same(u.cUnitId, p.cUnitId)) ?? (node.IsDraft ? UnitOptions.FirstOrDefault() : null);
      UpdateState();
   }

   private void ClearEditor() {
      TypeOptions.Clear();
      EditType = null; EditUnit = null; EditCode = ""; EditName = ""; EditNote = null;
      UpdateState();
   }

   private static bool Same(string? a, string? b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

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
      foreach (var name in (string[])[nameof(HasSelection), nameof(HasNoSelection), nameof(IsDraftSelected), nameof(IsSystemSelected), nameof(CanEditIdentity), nameof(IsDirty),
                  nameof(ToggleStateCaption), nameof(PathCaption), nameof(ParentCaption), nameof(StateCaption), nameof(CanBrowse), nameof(CanEdit)])
         NotifyChanged(name);
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
   }
}
