using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Em.Api.Core.Models;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;
using Em.Ui.Wpf.Dialogs;

namespace Em.Ui.Wpf.Navigations;

/// <summary>Configures primary and backup signers; changes affect only future requests.</summary>
public partial class ApprovalSignerSettings : UserControl, INavigationBody {
   private Point _dragStart;
   /// <summary>Creates the System Approval Editor.</summary>
   public ApprovalSignerSettings(EmApp app) { InitializeComponent(); Vm.EmApp = app; }
   /// <summary>Screen view model.</summary>
   public ApprovalSignerSettingsVm Vm => (ApprovalSignerSettingsVm)DataContext;
   /// <inheritdoc />
   public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Vm.RefreshCommand();
   /// <inheritdoc />
   public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;
   /// <inheritdoc />
   public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.RefreshCommand();
   /// <inheritdoc />
   public Task OnRelease(INavigation sender) => Task.CompletedTask;
   private void OnDragStart(object sender, MouseButtonEventArgs args) => _dragStart = args.GetPosition(this);
   private void OnPoolDoubleClick(object sender, MouseButtonEventArgs args) {
      if (args.ClickCount != 2 || sender is not FrameworkElement { DataContext: ApprovalPoolItem item }) return;
      var command = Vm.Commands[nameof(Vm.AddToSelectedStepCommand)];
      if (command?.CanExecute(item) == true) command.Execute(item);
   }
   private void OnPoolMove(object sender, MouseEventArgs args) {
      if (args.LeftButton != MouseButtonState.Pressed || Vm.IsBusy || sender is not FrameworkElement { DataContext: ApprovalPoolItem { Active: true } item }) return;
      var point = args.GetPosition(this);
      if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
      DragDrop.DoDragDrop((DependencyObject)sender, item, DragDropEffects.Copy);
   }
   private void OnDragOver(object sender, DragEventArgs args) {
      args.Effects = !Vm.IsBusy && args.Data.GetData(typeof(ApprovalPoolItem)) is ApprovalPoolItem { Active: true } ? DragDropEffects.Copy : DragDropEffects.None;
      args.Handled = true;
   }
   private void OnDrop(object sender, DragEventArgs args) {
      if (sender is FrameworkElement { DataContext: ApprovalSignerStepVm step } && args.Data.GetData(typeof(ApprovalPoolItem)) is ApprovalPoolItem item) {
         var command = Vm.Commands[nameof(Vm.DropSignerCommand)]; var input = new ApprovalSignerDrop(step, item);
         if (command?.CanExecute(input) == true) command.Execute(input);
      }
      args.Handled = true;
   }
}
/// <summary>One user or the static Dynamic Signer offered by the pool.</summary>
public sealed record ApprovalPoolItem(string? UserId, string Name, bool Active, bool Dynamic = false) {
   /// <summary>Explicit display text, also marking inactive users.</summary>
   public string Caption => Name + (Active ? string.Empty : " · inactive");
}
/// <summary>A selected signer keeps its owning step so its commands are unambiguous.</summary>
public sealed record ApprovalAssignedSigner(ApprovalSignerStepVm Step, ApprovalPoolItem User) {
   /// <summary>Whether this chip represents the primary signer.</summary>
   public bool IsPrimary => ReferenceEquals(Step.Primary, this);
   /// <summary>Role label beneath the user's name.</summary>
   public string Caption => IsPrimary ? (User.Dynamic ? "Dynamic signer" : "Signer") : "Four-eyes backup";
   /// <summary>Compact marker for dynamic or inactive identities.</summary>
   public string Marker => User.Dynamic ? "DS" : User.Active ? string.Empty : "!";
}
/// <summary>One drag operation from the user pool to a step.</summary>
public sealed record ApprovalSignerDrop(ApprovalSignerStepVm Step, ApprovalPoolItem User);
/// <summary>Draft signers for one server-declared step.</summary>
public sealed class ApprovalSignerStepVm : MvvmModelBase {
   /// <summary>Creates a draft for one declared step.</summary>
   public ApprovalSignerStepVm(ApprovalSignerSettingsVm owner, ApprovalSystemStep step) {
      Owner = owner; Step = step; Backups.CollectionChanged += (_, _) => Changed();
   }
   /// <summary>Owning editor.</summary>
   public ApprovalSignerSettingsVm Owner { get; }
   /// <summary>Server-declared level and name.</summary>
   public ApprovalSystemStep Step { get; }
   /// <summary>Primary signer, followed by ordered backups in the same chip row.</summary>
   public ObservableCollection<ApprovalAssignedSigner> Candidates { get; } = [];
   /// <summary>Exactly one user or DS is required.</summary>
   public ApprovalAssignedSigner? Primary { get => Get<ApprovalAssignedSigner?>(); set { Set(value); Changed(); } }
   /// <summary>Ordered four-eyes backups.</summary>
   public ObservableCollection<ApprovalAssignedSigner> Backups { get; } = [];
   /// <summary>Current primary signer source.</summary>
   public string KindText => Primary is null ? "Unassigned" : Primary.User.Dynamic ? "Dynamic" : "User";
   /// <summary>Whether the row has no primary assignment.</summary>
   public bool IsEmpty => Primary is null;
   private void Changed() {
      Candidates.Clear(); if (Primary is not null) Candidates.Add(Primary);
      foreach (var user in Backups) Candidates.Add(user);
      NotifyChanged(nameof(KindText)); NotifyChanged(nameof(IsEmpty)); Owner.Changed(this);
   }
}
/// <summary>Draft-only editing across definitions; Save writes changed definitions.</summary>
public sealed class ApprovalSignerSettingsVm : MvvmModelBase {
   private bool _loading;
   private readonly Dictionary<string, ApprovalSignerStepVm[]> _drafts = new();
   private readonly HashSet<string> _dirty = [];
   private readonly List<ApprovalPoolItem> _users = [];
   private IApprovalServices Service => EmApp!.ServiceProvider.GetRequiredService<IApprovalServices>();
   /// <summary>Creates the editor's commands.</summary>
   public ApprovalSignerSettingsVm() {
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, () => !IsBusy);
      RegisterCommand(nameof(RevertCommand), RevertCommand, () => !IsBusy && IsDirty);
      RegisterCommand(nameof(SaveCommand), SaveCommand, () => !IsBusy && IsDirty);
      RegisterCommand<ApprovalSignerDrop>(nameof(DropSignerCommand), DropSignerCommand, x => !IsBusy && x?.User.Active == true);
      RegisterCommand<ApprovalPoolItem>(nameof(AddToSelectedStepCommand), AddToSelectedStepCommand, x => !IsBusy && SelectedStep is not null && x?.Active == true);
      RegisterCommand<ApprovalAssignedSigner>(nameof(MakeSignerCommand), MakeSignerCommand, x => !IsBusy && x?.User.Active == true && !x.IsPrimary);
      RegisterCommand<ApprovalAssignedSigner>(nameof(RemoveCommand), RemoveCommand, x => !IsBusy && x is not null);
   }
   /// <summary>System definitions shown in the left rail.</summary>
   public ObservableCollection<ApprovalCapability> Definitions { get; } = [];
   /// <summary>Current definition's step table.</summary>
   public ObservableCollection<ApprovalSignerStepVm> Steps { get; } = [];
   /// <summary>Filtered pool; inactive users stay visible.</summary>
   public ObservableCollection<ApprovalPoolItem> Pool { get; } = [];
   /// <summary>Static unfiltered Dynamic Signer.</summary>
   public ApprovalPoolItem DynamicSigner { get; } = new(null, "Dynamic Signer", true, true);
   /// <summary>Selected definition; its unsaved draft is retained while switching.</summary>
   public ApprovalCapability? Selected { get => Get<ApprovalCapability?>(); set { if (Set(value) && !_loading) _ = LoadSelectionAsync(); } }
   /// <summary>Double-click adds to this selected step.</summary>
   public ApprovalSignerStepVm? SelectedStep { get => Get<ApprovalSignerStepVm?>(); set { Set(value); RaiseCommands(); } }
   /// <summary>User filter.</summary>
   public string Search { get => Get(string.Empty); set { Set(value); Filter(); } }
   /// <summary>Whether a service operation is running.</summary>
   public bool IsBusy { get => Get(false); private set { Set(value); RaiseCommands(); } }
   /// <summary>Whether any definition has unsaved changes.</summary>
   public bool IsDirty => _dirty.Count > 0;
   /// <summary>Last successful server refresh.</summary>
   public string RefreshedText { get => Get(string.Empty); private set => Set(value); }
   /// <summary>Number of pool users matching the filter.</summary>
   public string PoolCaption => $"{Pool.Count} of {_users.Count} users";
   /// <summary>Inline result or refusal.</summary>
   public string Message { get => Get(string.Empty); private set => Set(value); }
   private void RaiseCommands() { foreach (var command in Commands) command.RaiseCanExecuteChanged(); NotifyChanged(nameof(IsDirty)); }
   private void Filter() { Pool.Clear(); foreach (var user in _users.Where(x => x.Name.Contains(Search, StringComparison.CurrentCultureIgnoreCase))) Pool.Add(user); NotifyChanged(nameof(PoolCaption)); }
   internal void Changed(ApprovalSignerStepVm step) {
      if (_loading) return;
      var draft = _drafts.FirstOrDefault(x => x.Value.Contains(step));
      if (draft.Key is not null) _dirty.Add(draft.Key); RaiseCommands();
   }
   /// <summary>Reloads definitions; asks before discarding edited drafts.</summary>
   public async Task RefreshCommand() {
      if (IsBusy) return;
      if (IsDirty && DialogOwner?.ShowMboxDecideWarning("Discard unsaved signer changes and reload?", "Refresh") != MessageBoxResult.Yes) return;
      IsBusy = true; Message = string.Empty;
      try {
         var definitions = await Service.GetMeta_ApprovalCapabilities(); var name = Selected?.Name;
         _loading = true; _drafts.Clear(); _dirty.Clear(); Definitions.Clear();
         foreach (var definition in definitions.Where(x => x.Kind == ApprovalKind.System)) Definitions.Add(definition);
         Selected = Definitions.FirstOrDefault(x => x.Name == name) ?? Definitions.FirstOrDefault();
         await LoadAsync(); RefreshedText = $"Refreshed {DateTime.Now:HH:mm:ss}";
      } catch (Exception e) { Message = e.Message; } finally { _loading = false; IsBusy = false; }
   }
   /// <summary>Discards all unsaved drafts and reloads server settings.</summary>
   public Task RevertCommand() => RefreshCommand();
   private async Task LoadSelectionAsync() {
      if (IsBusy) return; IsBusy = true; Message = string.Empty; _loading = true;
      try { await LoadAsync(); } catch (Exception e) { Message = e.Message; } finally { _loading = false; IsBusy = false; }
   }
   private async Task LoadAsync() {
      Steps.Clear(); SelectedStep = null; if (Selected is null) return;
      if (!_drafts.TryGetValue(Selected.Name, out var draft)) {
         var saved = await Service.GetMeta_ApprovalSignerSettings(Selected.Name);
         _users.Clear(); _users.AddRange(saved.Users.Select(x => new ApprovalPoolItem(x.UserId, x.Name, x.Active))); Filter();
         ApprovalPoolItem User(string id) => _users.FirstOrDefault(x => x.UserId == id) ?? new(id, id, false);
         draft = saved.Steps.OrderBy(x => x.Level).Select(step => {
            var row = new ApprovalSignerStepVm(this, step); var setting = saved.Settings.SingleOrDefault(x => x.StepName == step.Name);
            if (setting is not null) {
               if (setting.Dynamic) row.Primary = new ApprovalAssignedSigner(row, DynamicSigner);
               else if (setting.UserId is { } primary) row.Primary = new ApprovalAssignedSigner(row, User(primary));
               foreach (var id in setting.Backups) row.Backups.Add(new ApprovalAssignedSigner(row, User(id)));
            }
            return row;
         }).ToArray();
         _drafts.Add(Selected.Name, draft);
      }
      foreach (var row in draft) Steps.Add(row); SelectedStep = Steps.FirstOrDefault();
   }
   /// <summary>Double-click adds to the selected step.</summary>
   public void AddToSelectedStepCommand(ApprovalPoolItem user) { if (SelectedStep is { } step) DropSignerCommand(new ApprovalSignerDrop(step, user)); }
   /// <summary>DS becomes primary; another user becomes a backup when a primary exists.</summary>
   public void DropSignerCommand(ApprovalSignerDrop input) {
      if (!input.User.Active || IsBusy) return;
      var step = input.Step; SelectedStep = step;
      if (step.Primary?.User == input.User || step.Backups.Any(x => x.User.UserId == input.User.UserId)) { Message = "This user is already assigned to the selected step."; return; }
      if (input.User.Dynamic || step.Primary is null) MakeSignerCommand(new ApprovalAssignedSigner(step, input.User));
      else step.Backups.Add(new ApprovalAssignedSigner(step, input.User));
   }
   /// <summary>Promotes an assignment and demotes the previous primary user to backup.</summary>
   public void MakeSignerCommand(ApprovalAssignedSigner input) {
      if (!input.User.Active || IsBusy) return; var step = input.Step;
      if (step.Primary?.User == input.User) return; var old = step.Primary;
      foreach (var candidate in step.Backups.Where(x => x.User.UserId == input.User.UserId).ToArray()) step.Backups.Remove(candidate);
      step.Primary = input;
      if (old is { User.Dynamic: false } && !step.Backups.Any(x => x.User.UserId == old.User.UserId)) step.Backups.Insert(0, old);
   }
   /// <summary>Removes a draft assignment.</summary>
   public void RemoveCommand(ApprovalAssignedSigner input) { if (input.IsPrimary) input.Step.Primary = null; else input.Step.Backups.Remove(input); }
   /// <summary>Writes each edited definition; successful saves are removed from the dirty set.</summary>
   public async Task SaveCommand() {
      if (IsBusy || !IsDirty) return;
      if (_dirty.Any(name => _drafts[name].Any(x => x.Primary is null))) { Message = "Every step needs exactly one signer: a user or DS."; return; }
      IsBusy = true; Message = string.Empty;
      try {
         foreach (var name in _dirty.ToArray()) {
            await Service.PostMeta_ApprovalSignerSettings(name, _drafts[name].Select(x => new ApprovalSignerSetting(x.Step.Name,
               x.Primary!.User.Dynamic, x.Primary.User.UserId, x.Backups.Select(b => b.User.UserId!).ToArray())).ToArray());
            _dirty.Remove(name);
         }
         Message = "Saved. New requests use these settings; running requests keep their snapshot until reinstated.";
      } catch (Exception e) { Message = e.Message; } finally { IsBusy = false; }
   }
}
