using System.Collections.ObjectModel;
using System.Windows;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Navigations
{
   // Bagian tab Robots: daftar robot, token, dan hak per manager/resource.
   public partial class RobotManagerVm
   {
      private void RegisterRobotCommands() {
         RegisterCommand(nameof(NewRobotCommand), NewRobotCommand, NewRobotCommandAllowed);
         RegisterCommand(nameof(EditRobotCommand), EditRobotCommand, EditRobotCommandAllowed);
         RegisterCommand(nameof(RegenerateRobotCommand), RegenerateRobotCommand, RegenerateRobotCommandAllowed);
         RegisterCommand(nameof(DeleteRobotCommand), DeleteRobotCommand, DeleteRobotCommandAllowed);
      }

      #region Data

      /// <summary>Seluruh robot, urut nama.</summary>
      public ObservableCollection<RobotItem> Robots { get; } = [];

      /// <summary>Robot yang dipilih di daftar kiri tab Robots.</summary>
      public RobotItem? SelectedRobot {
         get => Get<RobotItem?>();
         set => Set(value, _ => {
            NotifyChanged(nameof(ShowRobotDetail));
            RaiseCommandsChanged();
         });
      }

      /// <summary>Ada robot yang dipilih, jadi panel detailnya tampil.</summary>
      public bool ShowRobotDetail => SelectedRobot is not null;

      /// <summary>Server belum punya satu robot pun.</summary>
      public bool HasNoRobots => IsLoaded && Robots.Count == 0;

      #endregion

      #region Commands

      /// <summary>
      /// Meminta nama, deskripsi, dan masa berlaku, membuat robot, lalu menampilkan tokennya sekali di
      /// <see cref="RobotTokenDialog"/>. Robot baru belum punya hak di resource mana pun.
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

      /// <summary>Hanya saat data sudah terbaca dan layar tidak sibuk.</summary>
      public bool NewRobotCommandAllowed() => CanAct;

      /// <summary>Mengubah deskripsi, status aktif, dan masa berlaku token robot yang dipilih.</summary>
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

      /// <summary>Hanya untuk robot yang dipilih.</summary>
      public bool EditRobotCommandAllowed() => CanAct && SelectedRobot is not null;

      /// <summary>
      /// Membuat token baru untuk robot yang dipilih, lalu menampilkannya sekali. Token lama langsung tidak
      /// berlaku: dialognya mengatakan itu dan meminta masa berlaku tokennya sekaligus.
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

      /// <summary>Hanya untuk robot yang dipilih.</summary>
      public bool RegenerateRobotCommandAllowed() => CanAct && SelectedRobot is not null;

      /// <summary>Menghapus robot yang dipilih beserta hak-haknya, setelah dikonfirmasi.</summary>
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

      /// <summary>Hanya untuk robot yang dipilih.</summary>
      public bool DeleteRobotCommandAllowed() => CanAct && SelectedRobot is not null;

      #endregion

      #region Reading

      // Membaca robot dan menyusun tabel haknya terhadap definisi manager yang sedang terbaca, jadi dipanggil
      // sesudah definisi manager terbaca. selectId memilih robot tertentu, mis. yang baru dibuat.
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

      // Satu perubahan hak langsung dikirim, dengan indikator di barisnya saja dan tanpa lapisan tunggu
      // layar penuh. Kalau gagal, pilihannya dikembalikan ke yang terakhir diterima server; jawaban
      // sehari-hari (400/404/409) ditampilkan dan daftar robot dibaca ulang karena mungkin sudah usang.
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

      // Token hanya ada di jawaban pembuatan; dialog ini satu-satunya tempat ia tampil, dan tidak
      // disimpan di VM layar ini sesudah dialognya ditutup.
      private void ShowToken(RobotToken? created) {
         if (created is null) return;

         var dialog = new RobotTokenDialog(created.Robot.Name, created.Token, RegistryHost, IsInsecureHost, _managers.Any(m => m.Id == "Container")) {
            Owner = DialogOwner
         };
         dialog.ShowDialog();
      }
   }
}
