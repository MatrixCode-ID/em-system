using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Clipboard = System.Windows.Clipboard;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   public partial class UserEditor : UserControl, INavigationBody
   {
      private EmApp _app;
      public UserEditor(EmApp app) {
         InitializeComponent();
         _app = app;
         Vm.AttachApp(_app);
         Vm.PasswordBoxSyncRequested += SyncPasswordBoxes;
      }

      public UserEditorVm Vm => (UserEditorVm)DataContext;

      // Only the payload is taken here, never the record: the host raises OnNavigatingIn on every
      // way into this screen, back and forward included, and those two re-enter a form the user may
      // have half filled in. Opening the record belongs to OnReloadRequested, which only a real
      // navigation raises.
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) {
         if (args.Data is not UserEditorNavigationPayload payload) {
            args.Cancel = true;
            args.Message = "The user editor can only be opened with a user payload.";
            return Task.CompletedTask;
         }

         Vm.Payload = payload;
         return Task.CompletedTask;
      }

      // Leaving the screen is what throws unsaved edits away, so the body being left is the one
      // that has to ask - which is exactly the chance the host gives it here.
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) {
         if (!Vm.HasUnsavedChanges) return Task.CompletedTask;

         var answer = (Vm.DialogOwner ?? _app.MainWindow).ShowMboxDecideWarning(
            "This user has changes that have not been saved. Leave the screen and lose them?",
            "Unsaved changes");

         if (answer == MessageBoxResult.Yes) return Task.CompletedTask;

         args.Cancel = true;
         args.Message = "The user still has unsaved changes.";
         return Task.CompletedTask;
      }

      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) {
         if (args.Data is UserEditorNavigationPayload payload)
            Vm.LoadFrom(payload);

         return Task.CompletedTask;
      }

      public Task OnRelease(INavigation sender) {
         Vm.PasswordBoxSyncRequested -= SyncPasswordBoxes;
         Vm.Release();
         return Task.CompletedTask;
      }

      #region Password boxes

      // A PasswordBox keeps its value out of the property system on purpose, so there is no
      // Password dependency property to bind to. These two handlers are the whole exception: each
      // one only hands the typed value to the view model, which owns every rule built on top of it.

      private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
         Vm.NewPassword = ((PasswordBox)sender).Password;

      private void RepeatPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
         Vm.RepeatPassword = ((PasswordBox)sender).Password;

      // The other half of the same exception: a value the view model changed on its own - the text
      // typed into the revealed box, or a password cleared after it was set - cannot reach a
      // PasswordBox through a binding, so the view model asks for it to be written here instead.
      // The comparison is what stops the write coming straight back as a PasswordChanged.
      private void SyncPasswordBoxes() {
         if (NewPasswordBox.Password != Vm.NewPassword)
            NewPasswordBox.Password = Vm.NewPassword;

         if (RepeatPasswordBox.Password != Vm.RepeatPassword)
            RepeatPasswordBox.Password = Vm.RepeatPassword;
      }

      #endregion
   }

   public class UserEditorVm : MvvmModelBase
   {
      // How many bars the strength meter draws. Fixed, unlike the number of rules: a password that
      // meets every rule in force fills the meter, whether that is one rule or four.
      private const int PasswordRuleCount = 4;

      // Shown wherever a value belongs to a record that does not exist yet, or to a part of the
      // system that is not storing anything so far.
      private const string NoValue = "-";

      public UserEditorVm() {
         RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
         RegisterCommand(nameof(DiscardCommand), DiscardCommand, DiscardCommandAllowed);
         RegisterCommand(nameof(SuspendCommand), SuspendCommand, SuspendCommandAllowed);
         RegisterCommand(nameof(CopyRecordIdCommand), CopyRecordIdCommand, CopyRecordIdCommandAllowed);
         RegisterCommand(nameof(PickContactCommand), PickContactCommand, PickContactCommandAllowed);
         RegisterCommand(nameof(SetPasswordCommand), SetPasswordCommand, SetPasswordCommandAllowed);
         RegisterCommand(nameof(TogglePasswordRevealCommand), TogglePasswordRevealCommand);
      }

      /// <summary>
      /// Diminta view model saat isi kotak sandi di layar perlu disamakan lagi dengan nilai yang
      /// dipegangnya. PasswordBox sengaja tidak membuka nilainya lewat binding, jadi hanya
      /// code-behind yang bisa menuliskannya balik.
      /// </summary>
      public event Action? PasswordBoxSyncRequested;

      /// <summary>
      /// Menyambungkan ViewModel ini ke aplikasi, lalu memberi tahu UI supaya binding aturan kata
      /// sandi dievaluasi ulang. Notifikasinya wajib: XAML sudah membuat ViewModel ini berikut seluruh
      /// binding-nya sebelum <see cref="MvvmModelBase.EmApp"/> sempat di-set, jadi tanpa ini daftar
      /// aturannya keburu terbaca dari nilai bawaan dan tidak pernah mengikuti aturan aplikasi.
      /// </summary>
      /// <param name="app">Objek aplikasi pemilik ViewModel ini.</param>
      public void AttachApp(EmApp app) {
         EmApp = app;
         RefreshPasswordPolicy();
      }

      // Aturannya sendiri tidak berubah selama layar terbuka, jadi ini hanya dipanggil sekali - tapi
      // seluruh tampilan yang dibentuknya dibacakan sebagai satu set, sama seperti RefreshPasswordRules.
      private void RefreshPasswordPolicy() {
         NotifyChanged(nameof(IsPasswordPolicyShown));
         NotifyChanged(nameof(IsMinLengthRuleShown));
         NotifyChanged(nameof(IsMixedCaseRuleShown));
         NotifyChanged(nameof(IsDigitRuleShown));
         NotifyChanged(nameof(IsSymbolRuleShown));
         NotifyChanged(nameof(MinLengthRuleCaption));

         RefreshPasswordRules();
      }

      /// <summary>
      /// Data navigasi yang sedang dibuka layar ini: satu user yang mau diubah, atau permintaan
      /// membuat user baru. Diisi control saat navigasi masuk, dan menjadi satu-satunya penghubung
      /// ke daftar user yang membukanya.
      /// </summary>
      public UserEditorNavigationPayload? Payload { get; internal set; }

      #region Record

      /// <summary>
      /// Baris user yang sedang dibuka, dan satu-satunya tempat isian layar ini disimpan - termasuk
      /// untuk user baru, yang sudah punya objeknya sendiri sejak form dibuka walau barisnya belum
      /// ada di database. Karena itu seluruh field di layar bisa mengikat langsung ke sini, tanpa
      /// salinan yang harus disalin balik saat menyimpan.
      /// </summary>
      public User? Data {
         get => Get<User?>();
         private set => Set(value);
      }

      /// <summary>
      /// Menandakan ada perubahan yang belum tersimpan. Diambil apa adanya dari baris yang sedang
      /// dibuka, yang memang sudah melacaknya sendiri.
      /// </summary>
      public bool HasUnsavedChanges => Data?.IsDirty == true;

      /// <summary>
      /// Menandakan barisnya sudah benar-benar ada di database. Baris yang sudah pernah ditulis
      /// membawa waktu server saat ia dibuat, sedangkan baris baru tanggalnya masih kosong.
      /// </summary>
      public bool IsStored => Data != null && Data.datestamp != default;

      // The colon is escaped rather than typed plain: left to itself a custom format takes the
      // time separator from the current culture, and an Indonesian one writes 23.15 where the
      // screen is meant to read 23:15. Escaping pins the clock to the same shape everywhere.

      /// <summary>Keterangan kapan baris ini terakhir disimpan, untuk chip di tool strip.</summary>
      public string SavedCaption =>
         IsStored ? $"Saved {Data!.ustamp:dd MMM yyyy, HH\\:mm}" : "Not saved yet";

      /// <summary>Keterangan kapan baris ini dibuat.</summary>
      public string CreatedCaption => IsStored ? $"{Data!.datestamp:dd MMM yyyy}" : NoValue;

      /// <summary>
      /// Keterangan kapan baris ini terakhir diubah, lengkap dengan jamnya dalam format 24 jam.
      /// </summary>
      public string LastUpdatedCaption =>
         IsStored ? $"{Data!.ustamp:dd MMM yyyy, HH\\:mm}" : NoValue;

      #endregion

      #region Field rules

      // A field nobody has typed into yet is not wrong, it is only empty, so every rule below
      // passes on an empty value: what is required is decided by SaveCommandAllowed, and what is
      // well formed is decided here. Only a value that is actually there has to hold its shape.

      /// <summary>
      /// Bentuk alamat e-mail yang diterima: ada satu tanda @, ada isi di kiri dan kanannya, dan
      /// bagian domainnya punya minimal satu titik dengan huruf di kedua sisinya. Sengaja tidak
      /// memakai aturan penuh RFC - yang dicegat di sini adalah salah ketik yang kelihatan, bukan
      /// alamat aneh yang secara teori sah.
      /// </summary>
      private static readonly Regex EmailPattern =
         new(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$", RegexOptions.Compiled);

      /// <summary>
      /// Bentuk kode pos yang diterima: 3 sampai 10 karakter, huruf dan angka, boleh diselingi
      /// spasi atau tanda hubung di tengah. Dibuat longgar dengan sengaja karena kode pos di tiap
      /// negara bentuknya berbeda - yang dijaga hanya panjangnya, sesuai lebar kolomnya.
      /// </summary>
      private static readonly Regex ZipPattern =
         new(@"^[A-Za-z0-9][A-Za-z0-9 -]{1,8}[A-Za-z0-9]$", RegexOptions.Compiled);

      /// <summary>
      /// Menandakan alamat e-mail yang diketik belum berbentuk alamat yang sah. Dipakai layar untuk
      /// memerahkan kotak isiannya sekaligus menahan tombol simpan.
      /// </summary>
      public bool HasEmailError =>
         !string.IsNullOrWhiteSpace(Data?.cCommValue) && !EmailPattern.IsMatch(Data!.cCommValue.Trim());

      /// <summary>
      /// Menandakan kode pos yang diketik belum berbentuk kode pos yang masuk akal. Dipakai layar
      /// untuk memerahkan kotak isiannya sekaligus menahan tombol simpan.
      /// </summary>
      public bool HasZipError =>
         !string.IsNullOrWhiteSpace(Data?.cAddressZip) && !ZipPattern.IsMatch(Data!.cAddressZip!.Trim());

      #endregion

      #region Account state

      // The state is one value on the record, but the segmented control that sets it is four
      // buttons, so each button gets its own view of that one value. Only the button being switched
      // on says anything: the one being switched off is just the group making room for it.

      /// <summary>Penanda tombol "Active" pada pemilih keadaan akun.</summary>
      public bool IsStateActive {
         get => Data?.cUserState == UserState.Active;
         set => SetStateWhenChecked(value, UserState.Active);
      }

      /// <summary>Penanda tombol "Pending" pada pemilih keadaan akun.</summary>
      public bool IsStatePending {
         get => Data?.cUserState == UserState.Pending;
         set => SetStateWhenChecked(value, UserState.Pending);
      }

      /// <summary>Penanda tombol "Suspended" pada pemilih keadaan akun.</summary>
      public bool IsStateSuspended {
         get => Data?.cUserState == UserState.Suspended;
         set => SetStateWhenChecked(value, UserState.Suspended);
      }

      /// <summary>Penanda tombol "Inactive" pada pemilih keadaan akun.</summary>
      public bool IsStateInactive {
         get => Data?.cUserState == UserState.Inactive;
         set => SetStateWhenChecked(value, UserState.Inactive);
      }

      #endregion

      #region Fields with nowhere to be stored yet

      // Everything in this region is asked for by the screen but has no column, no service call and
      // no table behind it yet, so for now it lives on the view model and nowhere else. Each one is
      // a question still to be settled - whether it becomes a column of its own, or rides along in
      // the free-form data column - and until that is decided none of them is written anywhere.

      /// <summary>
      /// Pilihan layar yang bisa dijadikan tujuan pertama setelah masuk aplikasi. Sementara ini
      /// baru satu, tapi daftarnya sudah di sini supaya tinggal ditambah.
      /// </summary>
      public IReadOnlyList<string> LandingNavigations { get; } = ["Home"];

      /// <summary>
      /// Navigasi yang dibuka tepat setelah akun ini masuk aplikasi. Untuk sekarang hanya ada satu
      /// pilihan dan pilihannya belum tersimpan ke mana pun.
      /// </summary>
      public string LandingNavigation {
         get => Get("Home");
         set => Set(value);
      }

      /// <summary>
      /// Keterangan kapan akun ini terakhir masuk aplikasi. Belum ada yang mencatatnya, jadi isinya
      /// masih tetap - tempatnya disediakan supaya tinggal diisi begitu pencatatannya ada.
      /// </summary>
      public string LastSignInCaption => "Not recorded yet";

      /// <summary>Kata sandi baru yang diketik, diteruskan dari kotak sandi di layar.</summary>
      public string NewPassword {
         get => Get(string.Empty);
         set => Set(value, _ => RefreshPasswordRules());
      }

      /// <summary>Ulangan kata sandi baru, dipakai memastikan tidak ada salah ketik.</summary>
      public string RepeatPassword {
         get => Get(string.Empty);
         set => Set(value, _ => RefreshPasswordRules());
      }

      /// <summary>
      /// Menandakan kata sandi baru sedang ditampilkan apa adanya, bukan berupa titik-titik. Saat
      /// kembali disembunyikan, isian yang tadi diketik dikirim balik ke kotak sandi supaya kedua
      /// tampilan tidak berbeda isi.
      /// </summary>
      public bool IsPasswordRevealed {
         get => Get<bool>();
         private set => Set(value, _ => {
            NotifyChanged(nameof(IsPasswordMasked));
            NotifyChanged(nameof(PasswordRevealCaption));
            PasswordBoxSyncRequested?.Invoke();
         });
      }

      /// <summary>Kebalikan <see cref="IsPasswordRevealed"/>, dipakai kotak sandi yang tertutup.</summary>
      public bool IsPasswordMasked => !IsPasswordRevealed;

      /// <summary>Keterangan tombol mata: menyebut apa yang akan terjadi kalau tombolnya ditekan.</summary>
      public string PasswordRevealCaption => IsPasswordRevealed ? "Hide the password" : "Show the password";

      /// <summary>
      /// Aturan kata sandi yang berlaku. Dibaca dari aplikasi, dengan nilai bawaan sebagai jaring
      /// pengaman: XAML membuat ViewModel ini berikut seluruh binding-nya sebelum
      /// <see cref="MvvmModelBase.EmApp"/> sempat di-set - lihat <see cref="AttachApp"/>.
      /// </summary>
      private PasswordPolicy Policy => EmApp?.PasswordPolicy ?? FallbackPolicy;

      private static readonly PasswordPolicy FallbackPolicy = new();

      /// <summary>Menandakan panjang kata sandi sudah memenuhi batas minimal.</summary>
      public bool PasswordHasMinLength => Policy.HasMinLength(NewPassword);

      /// <summary>Menandakan kata sandi memuat huruf besar dan huruf kecil sekaligus.</summary>
      public bool PasswordHasMixedCase => PasswordPolicy.HasMixedCase(NewPassword);

      /// <summary>Menandakan kata sandi memuat setidaknya satu angka.</summary>
      public bool PasswordHasDigit => PasswordPolicy.HasDigit(NewPassword);

      /// <summary>Menandakan kata sandi memuat setidaknya satu tanda baca atau simbol.</summary>
      public bool PasswordHasSymbol => PasswordPolicy.HasSymbol(NewPassword);

      /// <summary>Menandakan aturan panjang minimal sedang berlaku, jadi barisnya perlu ditampilkan.</summary>
      public bool IsMinLengthRuleShown => Policy.IsMinLengthShown;

      /// <summary>Menandakan aturan huruf besar-kecil sedang berlaku.</summary>
      public bool IsMixedCaseRuleShown => Policy.IsMixedCaseShown;

      /// <summary>Menandakan aturan angka sedang berlaku.</summary>
      public bool IsDigitRuleShown => Policy.IsDigitShown;

      /// <summary>Menandakan aturan simbol sedang berlaku.</summary>
      public bool IsSymbolRuleShown => Policy.IsSymbolShown;

      /// <summary>
      /// Menandakan masih ada aturan yang berlaku. Kalau tidak ada satu pun, pengukur kekuatan sandi
      /// dan daftar aturannya sama-sama disembunyikan - keduanya tidak punya apa pun untuk dikatakan.
      /// </summary>
      public bool IsPasswordPolicyShown => Policy.ShownRuleCount > 0;

      /// <summary>
      /// Bunyi baris aturan panjang minimal, mis. "At least 12 characters". Dirakit di sini karena
      /// angkanya berasal dari aturan yang berlaku, bukan angka tetap di layar.
      /// </summary>
      public string MinLengthRuleCaption => $"At least {Policy.MinLength} characters";

      /// <summary>
      /// Menandakan kata sandi sudah memenuhi seluruh aturan yang wajib. Aturan yang cuma saran tidak
      /// ikut menahan, jadi ini bukan hal yang sama dengan pengukur kekuatan sandi terisi penuh.
      /// </summary>
      public bool PasswordMeetsPolicy => Policy.IsSatisfiedBy(NewPassword);

      /// <summary>Menandakan kedua kotak sandi berisi teks yang sama dan tidak kosong.</summary>
      public bool PasswordsMatch => NewPassword.Length > 0 && NewPassword == RepeatPassword;

      /// <summary>
      /// Jumlah balok yang menyala pada pengukur kekuatan sandi, 0 sampai 4. Bukan jumlah aturan yang
      /// terpenuhi melainkan porsinya: aturannya bisa tinggal dua, dan memenuhi keduanya tetap berarti
      /// pengukurnya penuh. Dengan keempat aturan bawaan aktif, keduanya kebetulan sama persis.
      /// </summary>
      public int PasswordStrength {
         get {
            var shown = Policy.ShownRuleCount;
            if (shown == 0) return 0;

            var met = Policy.CountMetShownRules(NewPassword);
            return (int)Math.Round((double)PasswordRuleCount * met / shown, MidpointRounding.AwayFromZero);
         }
      }

      /// <summary>Nama kekuatan sandi yang sedang tercapai, mis. "Fair" atau "Strong".</summary>
      public string PasswordStrengthCaption => PasswordStrength switch {
         1 => "Weak",
         2 => "Fair",
         3 => "Good",
         PasswordRuleCount => "Strong",
         _ => string.Empty
      };

      /// <summary>Menandakan balok pertama pengukur kekuatan sandi menyala.</summary>
      public bool PasswordBar1 => PasswordStrength >= 1;

      /// <summary>Menandakan balok kedua pengukur kekuatan sandi menyala.</summary>
      public bool PasswordBar2 => PasswordStrength >= 2;

      /// <summary>Menandakan balok ketiga pengukur kekuatan sandi menyala.</summary>
      public bool PasswordBar3 => PasswordStrength >= 3;

      /// <summary>Menandakan balok keempat pengukur kekuatan sandi menyala.</summary>
      public bool PasswordBar4 => PasswordStrength >= PasswordRuleCount;

      #endregion

      #region Commands

      /// <summary>
      /// Menyimpan baris yang sedang dibuka. Baris baru dan baris lama lewat pintu yang sama:
      /// modelnya sendiri yang tahu mana di antara keduanya yang berlaku.
      /// </summary>
      public async Task SaveCommand() {
         if (Data == null) return;

         try {
            WaiterText = "Saving user...";
            IsBusy = InWaiting = true;
            RaiseCommandsChanged();

            var wasNew = Payload?.DataState == DataState.NewData;
            await Data.SaveAsync();

            // Reported only after the row really exists, and only once - which is also what turns
            // the payload from a request for a new user into one that carries a stored user.
            if (wasNew) {
               Payload!.SetNewUser(Data);
               // The screen was opened under the title of a user that did not exist yet; now that it
               // does, the entry takes the stored user's title, so opening that user again lands here.
               if (Payload.Title is { } title) NavigationEntry?.SetTitle(title);
            }
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
         finally {
            IsBusy = InWaiting = false;
            RaiseRecordChanged();
         }
      }

      /// <summary>Hanya boleh dijalankan kalau ada perubahan dan field wajibnya sudah terisi.</summary>
      public bool SaveCommandAllowed() =>
         IsNotBusy
         && Data != null
         && Data.IsDirty
         && !string.IsNullOrWhiteSpace(Data.cUserAccount)
         && !string.IsNullOrWhiteSpace(Data.cContactFullName)
         && !HasEmailError
         && !HasZipError;

      /// <summary>
      /// Mengembalikan seluruh isian ke nilai yang tersimpan. Untuk user baru yang belum pernah
      /// disimpan, itu berarti form dikosongkan kembali seperti saat pertama dibuka.
      /// </summary>
      public void DiscardCommand() => Data?.RollBack();

      /// <summary>Hanya boleh dijalankan kalau memang ada perubahan yang bisa dibuang.</summary>
      public bool DiscardCommandAllowed() => IsNotBusy && HasUnsavedChanges;

      /// <summary>Menangguhkan akun sehingga tidak bisa dipakai masuk, lalu menyimpannya.</summary>
      public Task SuspendCommand() {
         Data!.cUserState = UserState.Suspended;
         return SaveCommand();
      }

      /// <summary>Hanya boleh dijalankan untuk baris yang sudah tersimpan dan belum ditangguhkan.</summary>
      public bool SuspendCommandAllowed() =>
         IsNotBusy && IsStored && Data!.cUserState != UserState.Suspended;

      /// <summary>Menyalin nomor identitas baris ini ke papan klip.</summary>
      public void CopyRecordIdCommand() => Clipboard.SetText(Data!.cUserId);

      /// <summary>Hanya boleh dijalankan kalau barisnya memang sudah punya nomor identitas.</summary>
      public bool CopyRecordIdCommandAllowed() => IsStored;

      /// <summary>Memilih kontak yang diwakili akun ini dari daftar kontak.</summary>
      public void PickContactCommand() {
      }

      /// <summary>
      /// Selalu tertutup untuk sekarang: dialog pemilih kontaknya belum ada, dan kontak tidak boleh
      /// diketik bebas - satu akun harus menunjuk ke kontak yang benar-benar sudah tercatat.
      /// </summary>
      public bool PickContactCommandAllowed() => false;

      /// <summary>
      /// Menetapkan kata sandi baru untuk akun ini. Sandinya diserahkan apa adanya ke server —
      /// server yang menghash dan menyimpannya, karena hash sandi memang tidak pernah boleh keluar
      /// dari sana. Kedua kotak sandi dikosongkan setelah berhasil supaya sandinya tidak tertinggal
      /// di layar.
      /// </summary>
      public async Task SetPasswordCommand() {
         if (Data == null) return;

         try {
            WaiterText = "Updating password...";
            IsBusy = InWaiting = true;
            RaiseCommandsChanged();

            var services = EmApp!.ServiceProvider.GetRequiredService<ICredentialServices>();
            await services.PostMeta_ResetPassword(Data.cUserId, NewPassword);
            ClearPassword();
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
         finally {
            IsBusy = InWaiting = false;
            RaiseRecordChanged();
         }
      }

      /// <summary>
      /// Hanya boleh dijalankan untuk akun yang barisnya sudah ada di database - kata sandi
      /// menempel pada akun, jadi akunnya harus sudah tersimpan lebih dulu - dan kalau kedua kotak
      /// sandinya sama serta panjangnya sudah mencukupi. Tiga aturan sisanya hanya ditampilkan
      /// lewat pengukur kekuatan sandi, tidak menghalangi.
      /// </summary>
      public bool SetPasswordCommandAllowed() =>
         IsNotBusy && IsStored && PasswordsMatch && PasswordMeetsPolicy;

      /// <summary>
      /// Membuka dan menutup tampilan kata sandi baru. Selalu boleh dipakai: yang diubah hanya cara
      /// isian itu ditampilkan, bukan isinya.
      /// </summary>
      public void TogglePasswordRevealCommand() => IsPasswordRevealed = !IsPasswordRevealed;

      #endregion

      #region Methods

      /// <summary>
      /// Membuka baris yang dibawa data navigasi. Permintaan user baru datang tanpa baris, dan
      /// barisnya dibuat di sini - kosong dan belum ada di database, tapi sudah berupa objek utuh
      /// sehingga layar tidak perlu memperlakukan user baru berbeda dari user lama.
      /// </summary>
      /// <param name="payload">Data navigasi berisi user yang mau diubah, atau permintaan user baru.</param>
      public void LoadFrom(UserEditorNavigationPayload payload) {
         Payload = payload;
         Attach(payload.Data ?? User.CreateNewUser(EmApp!));

         ClearPassword();
      }

      /// <summary>
      /// Melepas layar ini dari baris yang sedang dibuka. Dipanggil saat control-nya dilepas host,
      /// supaya baris yang masih hidup di daftar user tidak lagi memegang layar yang sudah tidak ada.
      /// </summary>
      public void Release() => Attach(null);

      // The screen listens to the record rather than copying it, so anything that changes a value -
      // a field being typed into, a rollback, a save - reaches the parts of the screen that are
      // worked out from it. Swapping records has to unhook the old one first: a record outlives the
      // screen, and a subscription left behind would keep the whole editor alive with it.
      private void Attach(User? user) {
         if (Data is { } previous)
            previous.PropertyChanged -= RecordPropertyChanged;

         Data = user;

         if (user != null)
            user.PropertyChanged += RecordPropertyChanged;

         RaiseRecordChanged();
      }

      private void RecordPropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseRecordChanged();

      // Everything the screen works out from the record is re-announced as one set. The record has
      // few enough columns that telling the screen exactly which of them moved would cost more to
      // maintain than it saves.
      private void RaiseRecordChanged() {
         NotifyChanged(nameof(HasUnsavedChanges));
         NotifyChanged(nameof(IsStored));
         NotifyChanged(nameof(SavedCaption));
         NotifyChanged(nameof(CreatedCaption));
         NotifyChanged(nameof(LastUpdatedCaption));
         NotifyChanged(nameof(HasEmailError));
         NotifyChanged(nameof(HasZipError));
         NotifyChanged(nameof(IsStateActive));
         NotifyChanged(nameof(IsStatePending));
         NotifyChanged(nameof(IsStateSuspended));
         NotifyChanged(nameof(IsStateInactive));

         RaiseCommandsChanged();
      }

      private void SetStateWhenChecked(bool isChecked, UserState state) {
         // A segmented control switches the old button off before it switches the new one on, and
         // that first half carries no information: only the button turning on names the state.
         if (isChecked && Data != null) Data.cUserState = state;
      }

      // Clearing the two values is only half the job: the boxes on screen hold their own copy, and
      // a password left revealed would carry over to whatever record is opened next.
      private void ClearPassword() {
         NewPassword = string.Empty;
         RepeatPassword = string.Empty;
         IsPasswordRevealed = false;

         PasswordBoxSyncRequested?.Invoke();
      }

      // Every rule, the meter and its caption are read off the same typed password, so they are
      // re-announced as one set.
      private void RefreshPasswordRules() {
         NotifyChanged(nameof(PasswordHasMinLength));
         NotifyChanged(nameof(PasswordHasMixedCase));
         NotifyChanged(nameof(PasswordHasDigit));
         NotifyChanged(nameof(PasswordHasSymbol));
         NotifyChanged(nameof(PasswordMeetsPolicy));
         NotifyChanged(nameof(PasswordsMatch));
         NotifyChanged(nameof(PasswordStrength));
         NotifyChanged(nameof(PasswordStrengthCaption));
         NotifyChanged(nameof(PasswordBar1));
         NotifyChanged(nameof(PasswordBar2));
         NotifyChanged(nameof(PasswordBar3));
         NotifyChanged(nameof(PasswordBar4));

         RaiseCommandsChanged();
      }

      // Every command on this screen is limited either by what is on the record or by whether a
      // save is already running, so they are re-evaluated as one set.
      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

      #endregion
   }
}
