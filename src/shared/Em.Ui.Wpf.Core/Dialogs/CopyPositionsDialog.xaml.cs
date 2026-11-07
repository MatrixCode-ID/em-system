using System.Collections.ObjectModel;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs;

/// <summary>Picks active positions of another tenant to copy into the current one.</summary>
public partial class CopyPositionsDialog : EmWindow
{
   /// <summary>Creates the dialog without loading records, for XAML checks.</summary>
   public CopyPositionsDialog() {
      InitializeComponent();
      Vm.MainWindow = this;
      Vm.RequestClose += result => DialogResult = result;
   }

   /// <summary>Creates the dialog for a target tenant.</summary>
   /// <param name="service">Reads the positions of the source tenant.</param>
   /// <param name="target">Tenant receiving the copies.</param>
   /// <param name="tenants">Tenants of the screen; the target is left out of the sources.</param>
   /// <param name="existing">Positions the target already has, including inactive ones.</param>
   public CopyPositionsDialog(ITenantPositionServices service, vi_Tenant target, IEnumerable<vi_Tenant> tenants, IEnumerable<vi_TenantPosition> existing) : this() {
      Vm.Configure(service, target, tenants, existing);
   }

   /// <summary>Dialog state and the chosen copy.</summary>
   public CopyPositionsDialogVm Vm => (CopyPositionsDialogVm)DataContext;
}

/// <summary>One position of the source tenant.</summary>
public sealed class CopyPositionItem : MvvmModelBase
{
   private readonly CopyPositionsDialogVm _owner;

   internal CopyPositionItem(CopyPositionsDialogVm owner, vi_TenantPosition position, bool exists) {
      _owner = owner; Position = position; Exists = exists;
   }

   /// <summary>The source position.</summary>
   public vi_TenantPosition Position { get; }

   /// <summary>Whether the target already has this code; such a position cannot be checked.</summary>
   public bool Exists { get; }

   /// <summary>Whether the position can be checked.</summary>
   public bool CanCheck => !Exists;

   /// <summary>Whether the position is copied.</summary>
   public bool IsChecked { get => Get<bool>(); set => Set(value, _ => _owner.OnCheckedChanged()); }
}

/// <summary>Source tenant, its active positions and the positions checked for copying.</summary>
public sealed class CopyPositionsDialogVm : MvvmModelBase
{
   private ITenantPositionServices? _service;
   private vi_Tenant? _target;
   private HashSet<string> _existingCodes = new(StringComparer.OrdinalIgnoreCase);

   /// <summary>Creates the commands.</summary>
   public CopyPositionsDialogVm() {
      RegisterCommand(nameof(CopyCommand), CopyCommand, CopyCommandAllowed);
      RegisterCommand(nameof(CancelCommand), CancelCommand, CancelCommandAllowed);
   }

   /// <summary>Asks the dialog to close; <c>true</c> = copy.</summary>
   public event Action<bool>? RequestClose;

   /// <summary>Tenants that can be copied from.</summary>
   public ObservableCollection<vi_Tenant> SourceTenants { get; } = [];

   /// <summary>Active positions of the source tenant.</summary>
   public ObservableCollection<CopyPositionItem> Items { get; } = [];

   /// <summary>Tenant the positions are read from; choosing one loads its positions.</summary>
   public vi_Tenant? SourceTenant {
      get => Get<vi_Tenant>();
      set { if (Set(value)) Loading = LoadAsync(); }
   }

   /// <summary>Latest source load, awaitable without rendering.</summary>
   public Task Loading { get; private set; } = Task.CompletedTask;

   /// <summary>Explains what the dialog does for the target tenant.</summary>
   public string Intro => $"Pick the positions to add to {_target?.cTenantName}. Codes that already exist here are skipped; copied positions are saved as active.";

   /// <summary>Server error, or the note that the source has no active position.</summary>
   public string Status { get => Get<string>() ?? ""; private set => Set(value); }

   /// <summary>How many positions are checked and how many already exist.</summary>
   public string SelectionCaption => $"{CheckedCount} selected · {Items.Count(x => x.Exists)} already exist in this tenant";

   /// <summary>Caption of the copy button.</summary>
   public string CopyCaption => CheckedCount == 1 ? "Copy 1 position" : $"Copy {CheckedCount} positions";

   /// <summary>The chosen copy; set when the dialog closes with <c>true</c>.</summary>
   public TenantPositionCopy? Result { get; private set; }

   private int CheckedCount => Items.Count(x => x.IsChecked);

   /// <summary>Sets the target and the tenants to choose from.</summary>
   public void Configure(ITenantPositionServices service, vi_Tenant target, IEnumerable<vi_Tenant> tenants, IEnumerable<vi_TenantPosition> existing) {
      _service = service; _target = target;
      _existingCodes = existing.Where(x => x.cTenantPositionId.Length > 0).Select(x => x.cTenantPositionCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
      foreach (var tenant in tenants.Where(x => !string.Equals(x.cTenantId, target.cTenantId, StringComparison.OrdinalIgnoreCase))) SourceTenants.Add(tenant);
      NotifyChanged(nameof(Intro));
      SourceTenant = SourceTenants.FirstOrDefault();
   }

   internal void OnCheckedChanged() => Refresh();

   /// <summary>Whether the copy can start.</summary>
   public bool CopyCommandAllowed() => IsNotBusy && CheckedCount > 0;

   /// <summary>Closes the dialog with the checked positions.</summary>
   public void CopyCommand() {
      if (!CopyCommandAllowed() || SourceTenant is null || _target is null) return;
      Result = new TenantPositionCopy {
         SourceTenantId = SourceTenant.cTenantId, TargetTenantId = _target.cTenantId,
         PositionIds = [.. Items.Where(x => x.IsChecked).Select(x => x.Position.cTenantPositionId)]
      };
      RequestClose?.Invoke(true);
   }

   /// <summary>Whether the dialog can be cancelled.</summary>
   public bool CancelCommandAllowed() => true;

   /// <summary>Closes the dialog without copying.</summary>
   public void CancelCommand() => RequestClose?.Invoke(false);

   private async Task LoadAsync() {
      Items.Clear(); Status = ""; Refresh();
      if (_service is null || SourceTenant is not { } source) return;
      try {
         IsBusy = true; Refresh();
         var rows = await _service.GetVi_TenantPositions(source.cTenantId);
         // A newer choice replaced this load while it was waiting for the server.
         if (!ReferenceEquals(source, SourceTenant)) return;
         foreach (var row in rows.Where(x => x.cTenantPositionState == ta_TenantPosition.StateActive))
            Items.Add(new CopyPositionItem(this, row, _existingCodes.Contains(row.cTenantPositionCode)));
         if (Items.Count == 0) Status = "This tenant has no active positions.";
      }
      catch (ActionException ex) { Status = ex.Message.Replace("Server Error: ", ""); }
      catch (Exception) { Status = "The positions could not be read. Close this dialog and try again."; }
      finally { IsBusy = false; Refresh(); }
   }

   private void Refresh() {
      NotifyChanged(nameof(SelectionCaption)); NotifyChanged(nameof(CopyCaption));
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
   }
}
