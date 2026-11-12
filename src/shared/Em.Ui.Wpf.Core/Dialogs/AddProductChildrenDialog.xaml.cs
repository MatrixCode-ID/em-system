using System.Collections.ObjectModel;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs;

/// <summary>Collects several new children of one product: one child per line, checked against the codes in use.</summary>
public partial class AddProductChildrenDialog : EmWindow
{
   /// <summary>Creates the dialog without a parent, for XAML checks.</summary>
   public AddProductChildrenDialog() {
      InitializeComponent();
      Vm.MainWindow = this;
      Vm.RequestClose += result => DialogResult = result;
   }

   /// <summary>Creates the dialog for a parent product.</summary>
   /// <param name="service">Checks which codes the tenant already uses.</param>
   /// <param name="parent">Product receiving the children.</param>
   public AddProductChildrenDialog(ITenantProductServices service, vi_TenantProduct parent) : this() {
      Vm.Configure(service, parent);
   }

   /// <summary>Dialog state and the chosen batch.</summary>
   public AddProductChildrenDialogVm Vm => (AddProductChildrenDialogVm)DataContext;
}

/// <summary>One line of the batch, as shown in the preview.</summary>
public sealed class AddChildItem : MvvmModelBase
{
   private readonly AddProductChildrenDialogVm _owner;

   internal AddChildItem(AddProductChildrenDialogVm owner, string code, string name, string mark) {
      _owner = owner; Code = code; Name = name; Mark = mark;
   }

   /// <summary>Code of the new child.</summary>
   public string Code { get; }

   /// <summary>Name of the new child.</summary>
   public string Name { get; }

   /// <summary>Why the line cannot be added (EXISTS, REPEATED or INVALID); empty when it can.</summary>
   public string Mark { get; }

   /// <summary>Whether the line carries a mark.</summary>
   public bool HasMark => Mark.Length > 0;

   /// <summary>Whether the line can be checked.</summary>
   public bool CanCheck => Mark.Length == 0;

   /// <summary>Whether the child is created.</summary>
   public bool IsChecked { get => Get<bool>(); set => Set(value, _ => _owner.OnCheckedChanged()); }
}

/// <summary>Lines typed by the user, their preview and the children checked for creation.</summary>
public sealed class AddProductChildrenDialogVm : MvvmModelBase
{
   private static readonly char[] Separators = [',', ';', '\t'];
   private ITenantProductServices? _service;
   private vi_TenantProduct? _parent;

   /// <summary>Creates the commands.</summary>
   public AddProductChildrenDialogVm() {
      RegisterCommand(nameof(PreviewCommand), PreviewCommand, PreviewCommandAllowed);
      RegisterCommand(nameof(AddCommand), AddCommand, AddCommandAllowed);
      RegisterCommand(nameof(CancelCommand), CancelCommand, CancelCommandAllowed);
   }

   /// <summary>Asks the dialog to close; <c>true</c> = add.</summary>
   public event Action<bool>? RequestClose;

   /// <summary>The lines of the preview.</summary>
   public ObservableCollection<AddChildItem> Items { get; } = [];

   /// <summary>The children as typed: one per line, code and name separated by a comma, a semicolon or a tab.</summary>
   public string Lines {
      get => Get<string>() ?? "";
      set => Set(value, _ => { Items.Clear(); Status = ""; Refresh(); });
   }

   /// <summary>Latest preview, awaitable without rendering.</summary>
   public Task Loading { get; private set; } = Task.CompletedTask;

   /// <summary>Parent of the new children as text.</summary>
   public string ParentCaption => _parent is null ? "" : $"{_parent.cTenantProductName} ({_parent.cTenantProductCode})";

   /// <summary>Explains what the dialog does.</summary>
   public string Intro => "Enter one child per line as code, name. New children take the product type and unit of the parent and are saved as active.";

   /// <summary>Server error, or a note about the preview.</summary>
   public string Status { get => Get<string>() ?? ""; private set => Set(value); }

   /// <summary>How many children are checked and how many lines cannot be added.</summary>
   public string SelectionCaption => Items.Count == 0 ? "Choose Preview to check the lines." : $"{CheckedCount} selected · {Items.Count(x => x.HasMark)} cannot be added";

   /// <summary>Caption of the add button.</summary>
   public string AddCaption => CheckedCount == 1 ? "Add 1 child" : $"Add {CheckedCount} children";

   /// <summary>The chosen batch; set when the dialog closes with <c>true</c>.</summary>
   public TenantProductBatch? Result { get; private set; }

   private int CheckedCount => Items.Count(x => x.IsChecked);

   /// <summary>Sets the parent the children are created under.</summary>
   public void Configure(ITenantProductServices service, vi_TenantProduct parent) {
      _service = service; _parent = parent;
      NotifyChanged(nameof(ParentCaption));
   }

   internal void OnCheckedChanged() => Refresh();

   /// <summary>Whether the lines can be checked.</summary>
   public bool PreviewCommandAllowed() => IsNotBusy && !string.IsNullOrWhiteSpace(Lines);

   /// <summary>Reads the lines and marks the codes that are repeated, not valid or already used in the tenant.</summary>
   public Task PreviewCommand() => Loading = PreviewAsync();

   /// <summary>Whether the checked children can be added.</summary>
   public bool AddCommandAllowed() => IsNotBusy && CheckedCount > 0;

   /// <summary>Closes the dialog with the checked children.</summary>
   public void AddCommand() {
      if (!AddCommandAllowed() || _parent is null) return;
      Result = new TenantProductBatch {
         TenantId = _parent.cTenantId, ParentId = _parent.cTenantProductId,
         Rows = [.. Items.Where(x => x.IsChecked).Select(x => new TenantProductBatchRow { Code = x.Code, Name = x.Name })]
      };
      RequestClose?.Invoke(true);
   }

   /// <summary>Whether the dialog can be cancelled.</summary>
   public bool CancelCommandAllowed() => true;

   /// <summary>Closes the dialog without adding.</summary>
   public void CancelCommand() => RequestClose?.Invoke(false);

   private async Task PreviewAsync() {
      Items.Clear(); Status = "";
      var parsed = Lines.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).Select(Parse).ToArray();
      if (parsed.Length == 0 || _service is null || _parent is null) { Refresh(); return; }
      try {
         IsBusy = true; Refresh();
         var codes = parsed.Where(x => x.Valid).Select(x => x.Code).ToArray();
         var used = (codes.Length == 0 ? Array.Empty<string>() : await _service.PostGetMeta_TenantProduct_CodesExist(_parent.cTenantId, codes)).ToHashSet(StringComparer.OrdinalIgnoreCase);
         var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
         foreach (var line in parsed) {
            var mark = !line.Valid ? "INVALID" : used.Contains(line.Code) ? "EXISTS" : !seen.Add(line.Code) ? "REPEATED" : "";
            var item = new AddChildItem(this, line.Code, line.Name, mark);
            Items.Add(item);
            if (item.CanCheck) item.IsChecked = true;
         }
         if (Items.Any(x => x.Mark == "INVALID")) Status = "A line marked INVALID has no name after the code, or a code longer than 50 characters.";
      }
      catch (ActionException ex) { Status = ex.Message.Replace("Server Error: ", ""); }
      catch (Exception) { Status = "The codes could not be checked. Close this dialog and try again."; }
      finally { IsBusy = false; Refresh(); }
   }

   // The first separator splits the code from the name, so a name may contain commas.
   private static (string Code, string Name, bool Valid) Parse(string line) {
      var at = line.IndexOfAny(Separators);
      if (at < 0) return (line, "", false);
      var code = line[..at].Trim(); var name = line[(at + 1)..].Trim();
      return (code, name, code.Length is > 0 and <= 50 && name.Length is > 0 and <= 255);
   }

   private void Refresh() {
      NotifyChanged(nameof(SelectionCaption)); NotifyChanged(nameof(AddCaption));
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
   }
}
