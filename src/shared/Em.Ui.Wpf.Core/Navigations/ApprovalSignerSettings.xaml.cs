using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Em.Api.Core.Models;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

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
public sealed record ApprovalAssignedSigner(ApprovalSignerStepVm Step, ApprovalPoolItem User);
/// <summary>One drag operation from the user pool to a step.</summary>
public sealed record ApprovalSignerDrop(ApprovalSignerStepVm Step, ApprovalPoolItem User);
/// <summary>Draft signers for one server-declared step.</summary>
public sealed class ApprovalSignerStepVm(ApprovalSignerSettingsVm owner, ApprovalSystemStep step) : MvvmModelBase {
   /// <summary>Owning editor.</summary>
   public ApprovalSignerSettingsVm Owner { get; } = owner;
   /// <summary>Step supplied by the shared definition.</summary>
   public ApprovalSystemStep Step { get; } = step;
   /// <summary>Human-readable level and name.</summary>
   public string Heading => $"L{Step.Level} · {Step.Name}";
   /// <summary>Exactly one primary user or DS is required before saving.</summary>
   public ApprovalAssignedSigner? Primary { get => Get<ApprovalAssignedSigner?>(); set => Set(value); }
   /// <summary>Ordered four-eyes backups; any one may sign the active step.</summary>
   public ObservableCollection<ApprovalAssignedSigner> Backups { get; } = [];
}
/// <summary>Draft-only editing of System signers; Save is the only write action.</summary>
public sealed class ApprovalSignerSettingsVm : MvvmModelBase {
   private bool _settingSelection;
   private readonly List<ApprovalPoolItem> _users = [];
   private IApprovalServices Service => EmApp!.ServiceProvider.GetRequiredService<IApprovalServices>();
   /// <summary>Creates commands for loading, assigning and saving signers.</summary>
   public ApprovalSignerSettingsVm() {
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, () => !IsBusy);
      RegisterCommand(nameof(SaveCommand), SaveCommand, () => !IsBusy && Selected is not null);
      RegisterCommand<ApprovalSignerDrop>(nameof(DropSignerCommand), DropSignerCommand, x => !IsBusy && x?.User.Active == true);
      RegisterCommand<ApprovalAssignedSigner>(nameof(MakeSignerCommand), MakeSignerCommand, x => !IsBusy && x?.User.Active == true);
      RegisterCommand<ApprovalAssignedSigner>(nameof(RemoveCommand), RemoveCommand, x => !IsBusy && x is not null);
   }
   /// <summary>Registered System definitions, including those without requests.</summary>
   public ObservableCollection<ApprovalCapability> Definitions { get; } = [];
   /// <summary>Draft steps.</summary>
   public ObservableCollection<ApprovalSignerStepVm> Steps { get; } = [];
   /// <summary>Filtered users; DS is displayed separately and never filtered.</summary>
   public ObservableCollection<ApprovalPoolItem> Pool { get; } = [];
   /// <summary>Static DS item accepted in any primary signer slot.</summary>
   public ApprovalPoolItem DynamicSigner { get; } = new(null, "DS · Dynamic Signer", true, true);
   /// <summary>Selected definition.</summary>
   public ApprovalCapability? Selected { get => Get<ApprovalCapability?>(); set { if (Set(value) && !_settingSelection) _ = LoadSelectionAsync(); } }
   /// <summary>User-name filter.</summary>
   public string Search { get => Get(string.Empty); set { Set(value); Filter(); } }
   /// <summary>Current operation state.</summary>
   public bool IsBusy { get => Get(false); private set { Set(value); foreach (var command in Commands) command.RaiseCanExecuteChanged(); } }
   /// <summary>Inline operation result.</summary>
   public string Message { get => Get(string.Empty); private set => Set(value); }
   private void Filter() { Pool.Clear(); foreach (var user in _users.Where(x => x.Name.Contains(Search, StringComparison.CurrentCultureIgnoreCase))) Pool.Add(user); }
   /// <summary>Reloads definitions and saved settings.</summary>
   public async Task RefreshCommand() {
      if (IsBusy) return; IsBusy = true; Message = string.Empty;
      try {
         var definitions = await Service.GetMeta_ApprovalCapabilities();
         var name = Selected?.Name; _settingSelection = true;
         Definitions.Clear(); foreach (var definition in definitions.Where(x => x.Kind == ApprovalKind.System)) Definitions.Add(definition);
         Selected = Definitions.FirstOrDefault(x => x.Name == name) ?? Definitions.FirstOrDefault(); _settingSelection = false;
         await LoadAsync();
      } catch (Exception e) { Message = e.Message; } finally { _settingSelection = false; IsBusy = false; }
   }
   private async Task LoadSelectionAsync() {
      if (IsBusy) return; IsBusy = true; Message = string.Empty;
      try { await LoadAsync(); } catch (Exception e) { Message = e.Message; } finally { IsBusy = false; }
   }
   private async Task LoadAsync() {
      Steps.Clear(); _users.Clear(); Filter(); if (Selected is null) return;
      var settings = await Service.GetMeta_ApprovalSignerSettings(Selected.Name);
      _users.AddRange(settings.Users.Select(x => new ApprovalPoolItem(x.UserId, x.Name, x.Active))); Filter();
      ApprovalPoolItem User(string id) => _users.FirstOrDefault(x => x.UserId == id) ?? new(id, id, false);
      foreach (var step in settings.Steps.OrderBy(x => x.Level)) {
         var row = new ApprovalSignerStepVm(this, step);
         var saved = settings.Settings.SingleOrDefault(x => x.StepName == step.Name);
         if (saved is not null) {
            if (saved.Dynamic) row.Primary = new ApprovalAssignedSigner(row, DynamicSigner);
            else if (saved.UserId is { } primary) row.Primary = new ApprovalAssignedSigner(row, User(primary));
            foreach (var id in saved.Backups) row.Backups.Add(new ApprovalAssignedSigner(row, User(id)));
         }
         Steps.Add(row);
      }
   }
   /// <summary>Assigns a pool user as backup, or DS as primary.</summary>
   public void DropSignerCommand(ApprovalSignerDrop input) {
      if (!input.User.Active || IsBusy) return;
      var step = input.Step;
      if (input.User.Dynamic || step.Primary is null) MakeSignerCommand(new ApprovalAssignedSigner(step, input.User));
      else if (step.Primary.User.UserId != input.User.UserId && !step.Backups.Any(x => x.User.UserId == input.User.UserId))
         step.Backups.Add(new ApprovalAssignedSigner(step, input.User));
   }
   /// <summary>Promotes a user or DS; the previous primary user becomes a backup.</summary>
   public void MakeSignerCommand(ApprovalAssignedSigner input) {
      if (!input.User.Active || IsBusy) return;
      var step = input.Step;
      if (step.Primary?.User == input.User) return;
      var old = step.Primary;
      foreach (var candidate in step.Backups.Where(x => x.User.UserId == input.User.UserId).ToArray()) step.Backups.Remove(candidate);
      step.Primary = input;
      if (old is { User.Dynamic: false } && !step.Backups.Any(x => x.User.UserId == old.User.UserId)) step.Backups.Insert(0, old);
   }
   /// <summary>Removes a draft assignment, without touching running requests.</summary>
   public void RemoveCommand(ApprovalAssignedSigner input) {
      if (ReferenceEquals(input.Step.Primary, input)) input.Step.Primary = null;
      else input.Step.Backups.Remove(input);
   }
   /// <summary>Saves a complete primary/backup snapshot for future requests.</summary>
   public async Task SaveCommand() {
      if (IsBusy || Selected is null) return;
      if (Steps.Any(x => x.Primary is null)) { Message = "Every step needs exactly one signer: a user or DS."; return; }
      IsBusy = true; Message = string.Empty;
      try {
         await Service.PostMeta_ApprovalSignerSettings(Selected.Name, Steps.Select(x => new ApprovalSignerSetting(x.Step.Name,
            x.Primary!.User.Dynamic, x.Primary.User.UserId, x.Backups.Select(b => b.User.UserId!).ToArray())).ToArray());
         Message = "Saved. New requests use these settings; running requests keep their snapshot until reinstated.";
      } catch (Exception e) { Message = e.Message; } finally { IsBusy = false; }
   }
}
