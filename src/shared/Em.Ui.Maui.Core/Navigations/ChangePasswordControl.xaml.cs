using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Controls;
using Em.Ui.Maui.Shared;
using INavigation = Em.Ui.Core.Shared.INavigation;
using NavigationEventArgs = Em.Ui.Core.Shared.NavigationEventArgs;

namespace Em.Ui.Maui.Navigations
{
   /// <summary>
   /// Layar ganti kata sandi milik pengguna yang sedang masuk: sandi lama, sandi baru, dan
   /// pengulangannya. Dibuka dari panel account, dan hanya mengubah sandi pemiliknya sendiri - bukan
   /// sandi orang lain, yang tempatnya di layar pengelola pengguna.
   /// </summary>
   public partial class ChangePasswordControl : ContentView, INavigationBody
   {
      public ChangePasswordControl() {
         InitializeComponent();
      }

      /// <summary>View model layar ini, dibaca balik dari BindingContext yang dipasang di XAML.</summary>
      public ChangePasswordControlVm Vm => (ChangePasswordControlVm)BindingContext;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) => Task.CompletedTask;
   }

   /// <summary>
   /// Satu aturan kekuatan sandi sebagaimana ditampilkan di layar: kalimatnya, dan apakah sandi yang
   /// sedang diketik sudah memenuhinya.
   /// </summary>
   public sealed class PasswordRuleVm : NotifyPropertyBase
   {
      /// <summary>Kalimat aturannya, mis. "At least 8 characters".</summary>
      public required string Caption { get; init; }

      /// <summary>Apakah sandi yang sedang diketik sudah memenuhi aturan ini.</summary>
      public bool IsMet {
         get => Get<bool>();
         set => Set(value, _ => {
            NotifyChanged(nameof(Glyph));
            NotifyChanged(nameof(MarkColor));
         });
      }

      /// <summary>Centang kalau sudah terpenuhi, silang kalau belum.</summary>
      public string Glyph => IsMet ? FontIcons.Check : FontIcons.Xmark;

      /// <summary>
      /// Warna penanda aturan ini. Abu-abu tembus pandang saat belum terpenuhi - bukan merah - karena
      /// aturan yang belum dipenuhi selagi sandinya masih diketik bukan kesalahan, cuma belum selesai.
      /// </summary>
      public Color MarkColor => IsMet ? Colors.SeaGreen : Color.FromRgba(128, 128, 128, 140);
   }

   /// <summary>View model <see cref="ChangePasswordControl"/>.</summary>
   public class ChangePasswordControlVm : MvvmModelBase
   {
      /// <summary>
      /// Satu-satunya jawaban saat sandi lama ditolak server. Sama seperti di layar login, tidak
      /// menyebut lebih jauh dari itu.
      /// </summary>
      public const string InvalidCurrentPasswordMessage = "The current password is not correct.";

      public ChangePasswordControlVm() {
         RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
      }

      /// <summary>Aturan kekuatan sandi yang berlaku, sudah disaring ke yang benar-benar ditampilkan.</summary>
      public ObservableCollection<PasswordRuleVm> Rules { get; } = [];

      /// <summary>Akun yang sandinya sedang diganti.</summary>
      public string AccountCaption => EmApp?.ActiveUser is { } user
         ? $"Signed in as {user.cUserAccount}"
         : string.Empty;

      /// <summary>Kata sandi yang berlaku sekarang.</summary>
      public string CurrentPassword {
         get => Get<string>(string.Empty);
         set => Set(value, _ => {
            SaveError = null;
            RaiseSaveCommandChanged();
         });
      }

      /// <summary>Kata sandi baru yang diminta.</summary>
      public string NewPassword {
         get => Get<string>(string.Empty);
         set => Set(value, password => {
            SaveError = null;
            RefreshRules(password);
            NotifyChanged(nameof(Strength));
            NotifyChanged(nameof(HasConfirmMismatch));
            RaiseSaveCommandChanged();
         });
      }

      /// <summary>Pengulangan kata sandi baru.</summary>
      public string ConfirmPassword {
         get => Get<string>(string.Empty);
         set => Set(value, _ => {
            SaveError = null;
            NotifyChanged(nameof(HasConfirmMismatch));
            RaiseSaveCommandChanged();
         });
      }

      /// <summary>
      /// <c>true</c> kalau kedua kotak sandi baru sudah diisi tapi isinya berbeda. Selagi kotak
      /// pengulangannya masih kosong tidak ada yang salah - orangnya belum selesai mengetik.
      /// </summary>
      public bool HasConfirmMismatch =>
         ConfirmPassword.Length > 0 && ConfirmPassword != NewPassword;

      /// <summary><c>true</c> kalau ada aturan kekuatan sandi yang perlu digambar sama sekali.</summary>
      public bool HasRules => Rules.Count > 0;

      /// <summary>
      /// Seberapa banyak aturan yang sudah dipenuhi, 0 sampai 1, buat pengukur kekuatan sandi.
      /// </summary>
      public double Strength {
         get {
            if (EmApp?.PasswordPolicy is not { ShownRuleCount: > 0 } policy) return 0;
            return (double)policy.CountMetShownRules(NewPassword) / policy.ShownRuleCount;
         }
      }

      /// <summary>Pesan kegagalan terakhir, atau <c>null</c> kalau tidak ada.</summary>
      public string? SaveError {
         get => Get<string?>();
         set => Set(value, _ => NotifyChanged(nameof(HasSaveError)));
      }

      /// <summary><c>true</c> kalau ada pesan kegagalan yang perlu ditampilkan.</summary>
      public bool HasSaveError => !string.IsNullOrWhiteSpace(SaveError);

      /// <summary>
      /// Mengosongkan ketiga kotak sandi dan menyusun ulang daftar aturan. Dipanggil setiap kali layar
      /// ini dibuka: sandi yang tertinggal dari kunjungan sebelumnya tidak punya alasan untuk hidup
      /// lebih lama dari kunjungan itu.
      /// </summary>
      public Task ReloadAsync() {
         CurrentPassword = NewPassword = ConfirmPassword = string.Empty;
         SaveError = null;

         RefreshRules(string.Empty);
         NotifyChanged(nameof(AccountCaption));
         NotifyChanged(nameof(Strength));
         RaiseSaveCommandChanged();
         return Task.CompletedTask;
      }

      // Daftar aturannya dibangun ulang, bukan cuma diperbarui nilainya: aturan mana saja yang tampil
      // ditentukan PasswordPolicy milik aplikasi, dan itu baru bisa dibaca sesudah EmApp terpasang.
      private void RefreshRules(string password) {
         if (EmApp?.PasswordPolicy is not { } policy) return;

         Rules.Clear();
         if (policy.IsMinLengthShown) {
            Rules.Add(new PasswordRuleVm {
               Caption = $"At least {policy.MinLength} characters",
               IsMet = policy.HasMinLength(password)
            });
         }

         if (policy.IsMixedCaseShown) {
            Rules.Add(new PasswordRuleVm {
               Caption = "Upper and lower case letters",
               IsMet = PasswordPolicy.HasMixedCase(password)
            });
         }

         if (policy.IsDigitShown) {
            Rules.Add(new PasswordRuleVm {
               Caption = "At least one digit",
               IsMet = PasswordPolicy.HasDigit(password)
            });
         }

         if (policy.IsSymbolShown) {
            Rules.Add(new PasswordRuleVm {
               Caption = "At least one symbol",
               IsMet = PasswordPolicy.HasSymbol(password)
            });
         }

         NotifyChanged(nameof(HasRules));
      }

      /// <summary>
      /// Mengirim penggantian sandi ke server. Kalau berhasil, layar ini ditinggalkan; kalau tidak,
      /// <see cref="SaveError"/> yang terisi dan layarnya tetap di tempat - command ini tidak pernah
      /// melempar exception ke pemanggilnya.
      /// </summary>
      // Nothing may escape this method. ICommand.Execute is void, so UiCommandAsync runs it as
      // async void: an exception leaving here is rethrown on the dispatcher, and there is nothing
      // above it to catch it - the process ends.
      public async Task SaveCommand() {
         SaveError = null;

         try {
            WaiterText = "Saving...";
            IsBusy = InWaiting = true;
            RaiseSaveCommandChanged();

            var app = EmApp!;
            var services = app.ServiceProvider.GetRequiredService<ICredentialServices>();
            await services.PostMeta_ChangeMyPassword(CurrentPassword, NewPassword);

            // Sandinya sudah berganti, jadi tidak ada lagi yang perlu dikerjakan di sini. Ketiga kotak
            // dikosongkan lebih dulu supaya sandi yang baru saja diketik tidak tertinggal di layar
            // yang sebentar lagi dilepas.
            CurrentPassword = NewPassword = ConfirmPassword = string.Empty;
            ShowInfo("Password changed", "Your password has been changed.");
            if (NavigationEntry is { } entry) await entry.Stack.Backward();
         }
         catch (ActionException x) when (x.StatusCode == 401) {
            CurrentPassword = string.Empty;
            SaveError = InvalidCurrentPasswordMessage;
         }
         catch (Exception x) {
            SaveError = x.SerializedMessagesDefault();
         }
         finally {
            IsBusy = InWaiting = false;
            RaiseSaveCommandChanged();
         }
      }

      /// <summary>
      /// Simpan baru boleh dijalankan kalau ketiga kotak terisi, kedua sandi baru sama, sandi barunya
      /// berbeda dari yang lama, dan setiap aturan yang berstatus wajib sudah terpenuhi.
      /// </summary>
      public bool SaveCommandAllowed() =>
         IsNotBusy
         && !string.IsNullOrWhiteSpace(CurrentPassword)
         && !string.IsNullOrWhiteSpace(NewPassword)
         && ConfirmPassword == NewPassword
         && NewPassword != CurrentPassword
         && (EmApp?.PasswordPolicy.IsSatisfiedBy(NewPassword) ?? false);

      private void RaiseSaveCommandChanged() =>
         Commands[nameof(SaveCommand)]?.RaiseCanExecuteChanged();
   }
}
