using System.Collections.ObjectModel;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs;

/// <summary>Picks direct children of another product to copy under the current one, each with the code of its copy.</summary>
public partial class CopyProductChildrenDialog : EmWindow
{
   /// <summary>Creates the dialog without a target, for XAML checks.</summary>
   public CopyProductChildrenDialog() {
      InitializeComponent();
      Vm.MainWindow = this;
      Vm.RequestClose += result => DialogResult = result;
   }

   /// <summary>Creates the dialog for a target product.</summary>
   /// <param name="service">Finds the source product, reads its children and checks the codes in use.</param>
   /// <param name="target">Product receiving the copies.</param>
   public CopyProductChildrenDialog(ITenantProductServices service, vi_TenantProduct target) : this() {
      Vm.Configure(service, target);
   }

   /// <summary>Dialog state and the chosen copy.</summary>
   public CopyProductChildrenDialogVm Vm => (CopyProductChildrenDialogVm)DataContext;
}

/// <summary>One direct child of the source product.</summary>
public sealed class CopyChildItem : MvvmModelBase
{
   private readonly CopyProductChildrenDialogVm _owner;

   internal CopyChildItem(CopyProductChildrenDialogVm owner, vi_TenantProduct source, string code) {
      _owner = owner; Source = source;
      Set(code, propertyName: nameof(Code));
   }

   /// <summary>The product being copied.</summary>
   public vi_TenantProduct Source { get; }

   /// <summary>Code of the copy; proposed from the prefixes and editable per row.</summary>
   public string Code { get => Get<string>() ?? ""; set => Set(value, _ => _owner.OnCodeEdited()); }

   /// <summary>Why the row cannot be copied (EXISTS, REPEATED or INVALID); empty when it can.</summary>
   public string Mark { get => Get<string>() ?? ""; private set => Set(value, _ => { NotifyChanged(nameof(HasMark)); NotifyChanged(nameof(CanCheck)); }); }

   /// <summary>Whether the row carries a mark.</summary>
   public bool HasMark => Mark.Length > 0;

   /// <summary>Whether the row can be checked.</summary>
   public bool CanCheck => Mark.Length == 0;

   /// <summary>Whether the child is copied.</summary>
   public bool IsChecked { get => Get<bool>(); set => Set(value, _ => _owner.OnCheckedChanged()); }

   // Changes the code without starting a check of every row.
   internal void SetCodeQuietly(string code) => Set(code, propertyName: nameof(Code));

   // A row that cannot be copied loses its check; one that can again is not checked by itself.
   internal void SetMark(string mark) {
      Mark = mark;
      if (mark.Length > 0 && IsChecked) Set(false, propertyName: nameof(IsChecked));
   }
}

/// <summary>Source product, its direct children and the children checked for copying.</summary>
public sealed class CopyProductChildrenDialogVm : MvvmModelBase
{
   private ITenantProductServices? _service;
   private vi_TenantProduct? _target;
   private bool _settingPrefix;
   private int _checkVersion;

   /// <summary>Creates the commands.</summary>
   public CopyProductChildrenDialogVm() {
      RegisterCommand(nameof(FindCommand), FindCommand, FindCommandAllowed);
      RegisterCommand(nameof(CopyCommand), CopyCommand, CopyCommandAllowed);
      RegisterCommand(nameof(CancelCommand), CancelCommand, CancelCommandAllowed);
   }

   /// <summary>Asks the dialog to close; <c>true</c> = copy.</summary>
   public event Action<bool>? RequestClose;

   /// <summary>Text looked for in the codes and names of the tenant's products.</summary>
   public string FindText { get => Get<string>() ?? ""; set => Set(value, _ => Refresh()); }

   /// <summary>Products found, to choose the source from.</summary>
   public ObservableCollection<vi_TenantProduct> Sources { get; } = [];

   /// <summary>Product whose direct children are copied; choosing one reads its children.</summary>
   public vi_TenantProduct? Source {
      get => Get<vi_TenantProduct>();
      set { if (Set(value)) Loading = LoadChildrenAsync(); }
   }

   /// <summary>Direct children of the source product.</summary>
   public ObservableCollection<CopyChildItem> Items { get; } = [];

   /// <summary>Start of the source codes that is replaced; the code of the source product at first.</summary>
   public string OldPrefix { get => Get<string>() ?? ""; set => Set(value, _ => OnPrefixChanged()); }

   /// <summary>What replaces <see cref="OldPrefix"/> in every copied code; the code of the target product at first.</summary>
   public string NewPrefix { get => Get<string>() ?? ""; set => Set(value, _ => OnPrefixChanged()); }

   /// <summary>Latest search, child load or code check, awaitable without rendering.</summary>
   public Task Loading { get; private set; } = Task.CompletedTask;

   /// <summary>Target of the copies as text.</summary>
   public string TargetCaption => _target is null ? "" : $"{_target.cTenantProductName} ({_target.cTenantProductCode})";

   /// <summary>Explains what the dialog does.</summary>
   public string Intro => "Find the product to copy from, then check its direct children. A copy takes the name, unit, product type and note of its source and is saved as active; children of the children are not copied.";

   /// <summary>Server error, or a note about the search or the source.</summary>
   public string Status { get => Get<string>() ?? ""; private set => Set(value); }

   /// <summary>How many children are checked and how many cannot be copied.</summary>
   public string SelectionCaption => $"{CheckedCount} selected · {Items.Count(x => x.HasMark)} cannot be copied";

   /// <summary>Caption of the copy button.</summary>
   public string CopyCaption => CheckedCount == 1 ? "Copy 1 child" : $"Copy {CheckedCount} children";

   /// <summary>The chosen copy; set when the dialog closes with <c>true</c>.</summary>
   public TenantProductCopy? Result { get; private set; }

   private int CheckedCount => Items.Count(x => x.IsChecked);

   /// <summary>Sets the product the copies are created under.</summary>
   public void Configure(ITenantProductServices service, vi_TenantProduct target) {
      _service = service; _target = target;
      NotifyChanged(nameof(TargetCaption));
   }

   internal void OnCheckedChanged() => Refresh();

   internal void OnCodeEdited() => Loading = CheckCodesAsync();

   /// <summary>Whether a search for the source product can start.</summary>
   public bool FindCommandAllowed() => IsNotBusy && !string.IsNullOrWhiteSpace(FindText);

   /// <summary>Looks for products by code or name, to choose the source from.</summary>
   public Task FindCommand() => Loading = FindAsync();

   /// <summary>Whether the copy can start.</summary>
   public bool CopyCommandAllowed() => IsNotBusy && CheckedCount > 0;

   /// <summary>Closes the dialog with the checked children and their codes.</summary>
   public void CopyCommand() {
      if (!CopyCommandAllowed() || Source is null || _target is null) return;
      Result = new TenantProductCopy {
         TenantId = _target.cTenantId, SourceParentId = Source.cTenantProductId, TargetParentId = _target.cTenantProductId,
         Rows = [.. Items.Where(x => x.IsChecked).Select(x => new TenantProductCopyRow { SourceProductId = x.Source.cTenantProductId, Code = x.Code.Trim() })]
      };
      RequestClose?.Invoke(true);
   }

   /// <summary>Whether the dialog can be cancelled.</summary>
   public bool CancelCommandAllowed() => true;

   /// <summary>Closes the dialog without copying.</summary>
   public void CancelCommand() => RequestClose?.Invoke(false);

   private async Task FindAsync() {
      if (_service is null || _target is null || !FindCommandAllowed()) return;
      try {
         IsBusy = true; Status = ""; Refresh();
         var hits = await _service.GetMeta_TenantProduct_Search(_target.cTenantId, FindText.Trim(), null);
         Sources.Clear();
         // Only a product that has children can be copied from.
         foreach (var hit in hits.Where(x => x.Row.ccTenantProductChildCount > 0)) Sources.Add(hit.Row);
         Status = Sources.Count == 0 ? "No product with children has this text in its code or name." : "";
      }
      catch (ActionException ex) { Status = ex.Message.Replace("Server Error: ", ""); }
      catch (Exception) { Status = "The products could not be read. Close this dialog and try again."; }
      finally { IsBusy = false; Refresh(); }
      if (Sources.Count == 1) Source = Sources[0];
   }

   private async Task LoadChildrenAsync() {
      Items.Clear(); Refresh();
      if (_service is null || _target is null || Source is not { } source) return;
      try {
         IsBusy = true; Status = ""; Refresh();
         var rows = await _service.GetVi_TenantProducts_Children(_target.cTenantId, source.cTenantProductId, null);
         // A newer choice replaced this load while it was waiting for the server.
         if (!ReferenceEquals(source, Source)) return;
         _settingPrefix = true;
         try { OldPrefix = source.cTenantProductCode; NewPrefix = _target.cTenantProductCode; }
         finally { _settingPrefix = false; }
         foreach (var row in rows) Items.Add(new CopyChildItem(this, row, Propose(row.cTenantProductCode)));
         if (Items.Count == 0) Status = "This product has no children.";
      }
      catch (ActionException ex) { Status = ex.Message.Replace("Server Error: ", ""); }
      catch (Exception) { Status = "The children could not be read. Close this dialog and try again."; }
      finally { IsBusy = false; Refresh(); }
      await CheckCodesAsync(checkValid: true);
   }

   // New prefix, then what is left of the source code after the old prefix.
   private string Propose(string code) =>
      NewPrefix + (OldPrefix.Length > 0 && code.StartsWith(OldPrefix, StringComparison.OrdinalIgnoreCase) ? code[OldPrefix.Length..] : code);

   private void OnPrefixChanged() {
      if (_settingPrefix) return;
      foreach (var item in Items) item.SetCodeQuietly(Propose(item.Source.cTenantProductCode));
      Loading = CheckCodesAsync();
   }

   // Marks the rows whose code is not valid, repeated in the list or already used in the tenant.
   private async Task CheckCodesAsync(bool checkValid = false) {
      if (_service is null || _target is null) return;
      var version = ++_checkVersion;
      var codes = Items.Select(x => x.Code.Trim()).Where(x => x.Length is > 0 and <= 50).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
      HashSet<string> used;
      try {
         used = (codes.Length == 0 ? Array.Empty<string>() : await _service.PostGetMeta_TenantProduct_CodesExist(_target.cTenantId, codes)).ToHashSet(StringComparer.OrdinalIgnoreCase);
      }
      catch (ActionException ex) { Status = ex.Message.Replace("Server Error: ", ""); return; }
      catch (Exception) { Status = "The codes could not be checked. Close this dialog and try again."; return; }
      // A newer edit started another check while this one was waiting for the server.
      if (version != _checkVersion) return;
      var counts = Items.GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
      foreach (var item in Items) {
         var code = item.Code.Trim();
         item.SetMark(code.Length is 0 or > 50 ? "INVALID" : used.Contains(code) ? "EXISTS" : counts[code] > 1 ? "REPEATED" : "");
         if (checkValid && item.CanCheck) item.IsChecked = true;
      }
      Refresh();
   }

   private void Refresh() {
      NotifyChanged(nameof(SelectionCaption)); NotifyChanged(nameof(CopyCaption));
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
   }
}
