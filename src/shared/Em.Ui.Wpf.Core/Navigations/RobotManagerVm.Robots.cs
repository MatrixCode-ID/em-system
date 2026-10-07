using System.Collections.ObjectModel;
using System.Windows;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Navigations
{
   // The Robots tab part: the robot list, tokens, and rights per manager/resource.
   public partial class RobotManagerVm
   {
      private void RegisterRobotCommands() {
         RegisterCommand(nameof(NewRobotCommand), NewRobotCommand, NewRobotCommandAllowed);
         RegisterCommand(nameof(EditRobotCommand), EditRobotCommand, EditRobotCommandAllowed);
         RegisterCommand(nameof(RegenerateRobotCommand), RegenerateRobotCommand, RegenerateRobotCommandAllowed);
         RegisterCommand(nameof(DeleteRobotCommand), DeleteRobotCommand, DeleteRobotCommandAllowed);
      }

      #region Data

      /// <summary>All robots, ordered by name.</summary>
      public ObservableCollection<RobotItem> Robots { get; } = [];

      /// <summary>The robot chosen in the left list of the Robots tab.</summary>
      public RobotItem? SelectedRobot {
         get => Get<RobotItem?>();
         set => Set(value, _ => {
            NotifyChanged(nameof(ShowRobotDetail));
            RaiseCommandsChanged();
         });
      }

      /// <summary>A robot is chosen, so its detail panel is shown.</summary>
      public bool ShowRobotDetail => SelectedRobot is not null;

      /// <summary>The server has no robot at all yet.</summary>
      public bool HasNoRobots => IsLoaded && Robots.Count == 0;

      #endregion

      #region Commands

      /// <summary>
      /// Asks for a name, description, and validity, creates the robot, then shows its token once in
      /// <see cref="RobotTokenDialog"/>. A new robot has no rights on any resource yet.
      /// </summary>
      public async Task NewRobotCommand() {
         RobotOwnerInfo[]? owners = null;
         await RunBusyAsync("Loading owner accounts...", async () => owners = await Service.GetMeta_RobotOwners());
         if (owners is null) return;
         var dialog = new RobotDialog(RobotDialogMode.Create, owners: owners) { Owner = DialogOwner };
         if (dialog.ShowDialog() != true) return;

         var name = dialog.Vm.NameResult;
         var description = dialog.Vm.DescriptionResult;
         var expiry = dialog.Vm.TokenExpiry;
         RobotToken? created = null;
         await RunMutationAsync("Creating robot...", "New Robot",
            async () => created = await Service.PostGetMeta_RobotCreate(name, description, expiry, dialog.Vm.OwnerUserId),
            () => ReadRobotsAsync(created?.Robot.Id));

         ShowToken(created);
      }

      /// <summary>Only when the data has been read and the screen is not busy.</summary>
      public bool NewRobotCommandAllowed() => CanAct;

      /// <summary>Changes the description, active status, and token validity of the chosen robot.</summary>
      public async Task EditRobotCommand() {
         if (SelectedRobot is not { } robot) return;

         var dialog = new RobotDialog(RobotDialogMode.Edit, robot.Info) { Owner = DialogOwner };
         if (dialog.ShowDialog() != true) return;

         var description = dialog.Vm.DescriptionResult;
         var isActive = dialog.Vm.IsActive;
         var expiry = dialog.Vm.TokenExpiry;
         await RunMutationAsync("Saving robot...", "Edit Robot",
            () => Service.PostMeta_RobotUpdate(robot.Id, description, isActive, expiry),
            () => ReadRobotsAsync(robot.Id));
      }

      /// <summary>Only for the chosen robot.</summary>
      public bool EditRobotCommandAllowed() => CanAct && SelectedRobot is not null;

      /// <summary>
      /// Creates a new token for the chosen robot, then shows it once. The old token stops being valid at
      /// once: the dialog says so and asks for the token's validity at the same time.
      /// </summary>
      public async Task RegenerateRobotCommand() {
         if (SelectedRobot is not { } robot) return;

         var dialog = new RobotDialog(RobotDialogMode.Regenerate, robot.Info) { Owner = DialogOwner };
         if (dialog.ShowDialog() != true) return;

         var expiry = dialog.Vm.TokenExpiry;
         RobotToken? created = null;
         await RunMutationAsync("Regenerating token...", "Regenerate Token",
            async () => created = await Service.PostGetMeta_RobotRegenerate(robot.Id, expiry),
            () => ReadRobotsAsync(robot.Id));

         ShowToken(created);
      }

      /// <summary>Only for the chosen robot.</summary>
      public bool RegenerateRobotCommandAllowed() => CanAct && SelectedRobot is not null;

      /// <summary>Deletes the chosen robot together with its rights, after confirmation.</summary>
      public async Task DeleteRobotCommand() {
         if (SelectedRobot is not { } robot || DialogOwner is not { } owner) return;

         var answer = owner.ShowMboxDecideWarning(
            $"Delete robot '{robot.Name}' and all its access?\n\n" +
            "Its token stops working immediately. Images it pushed stay where they are, and their pusher " +
            "is shown as 'deleted robot'.\n\nThis cannot be undone.", "Delete Robot");
         if (answer != MessageBoxResult.Yes) return;

         await RunMutationAsync("Deleting robot...", "Delete Robot",
            () => Service.PostMeta_RobotDelete(robot.Id),
            () => ReadRobotsAsync());
      }

      /// <summary>Only for the chosen robot.</summary>
      public bool DeleteRobotCommandAllowed() => CanAct && SelectedRobot is not null;

      #endregion

      #region Reading

      // Reads the robots and arranges the table of their rights against the manager definitions currently
      // read, so it is called after the manager definitions have been read. selectId chooses a specific
      // robot, e.g. one that was just created.
      private async Task ReadRobotsAsync(string? selectId = null) {
         var robots = await Service.GetMeta_Robots();

         var keep = selectId ?? SelectedRobot?.Id;
         Robots.Clear();
         foreach (var info in robots.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            Robots.Add(new RobotItem(info, _managers, this));

         SelectedRobot = Robots.FirstOrDefault(r => r.Id == keep) ?? Robots.FirstOrDefault();
         NotifyChanged(nameof(HasNoRobots));
      }

      #endregion

      #region Access

      internal void OnAccessSelected(RobotAccessItem row) => _ = ApplyAccessAsync(row);

      // A single rights change is sent right away, with an indicator on its row only and without a
      // full-screen wait layer. If it fails, the choice is returned to the last one the server accepted;
      // everyday answers (400/404/409) are shown and the robot list is read again because it may be stale.
      private async Task ApplyAccessAsync(RobotAccessItem row) {
         var requested = row.SelectedAccess;
         var owner = DialogOwner;
         if (IsBusy || row.IsSaving) { row.Rollback(); return; }
         IsBusy = true;
         RaiseCommandsChanged();
         row.IsSaving = true;
         try {
            await Service.PostMeta_RobotAccessSet(row.Robot.Id, row.ManagerId, row.ResourceId, requested.Code);

            row.Commit(requested);
            row.Robot.RefreshAccessSummary();
         }
         catch (ActionException x) when (x.StatusCode is 400 or 404 or 409) {
            row.Rollback();
            owner?.ShowMboxWarning(ServerMessage(x), "Robot Access");
            await ReadRobotsAsync();
         }
         catch (Exception x) {
            row.Rollback();
            AlertError(x);
         }
         finally {
            row.IsSaving = false;
            IsBusy = false;
            RaiseCommandsChanged();
         }
      }

      #endregion

      // The token only exists in the creation answer; this dialog is the only place it is shown, and it is
      // not kept in this screen's view model after the dialog is closed.
      private void ShowToken(RobotToken? created) {
         if (created is null) return;

         var dialog = new RobotTokenDialog(created.Robot.Name, created.Token, RegistryHost, IsInsecureHost, _managers.Any(m => m.Id == "Container")) {
            Owner = DialogOwner
         };
         dialog.ShowDialog();
      }
   }
}
