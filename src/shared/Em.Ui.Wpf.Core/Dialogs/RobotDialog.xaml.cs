using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Em.Api.Core.Models;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
using FontAwesome6;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>Untuk apa <see cref="RobotDialog"/> dibuka.</summary>
   public enum RobotDialogMode
   {
      /// <summary>Membuat robot: nama, deskripsi, masa berlaku token.</summary>
      Create = 0,

      /// <summary>Mengedit robot: deskripsi, status aktif, masa berlaku token. Nama tidak bisa diganti.</summary>
      Edit = 1,

      /// <summary>Membuat token baru untuk robot yang ada: hanya masa berlaku tokennya.</summary>
      Regenerate = 2
   }

   /// <summary>Jenis pilihan masa berlaku token.</summary>
   public enum CtnExpiryKind
   {
      /// <summary>Pertahankan masa berlaku yang sekarang.</summary>
      Keep = 0,

      /// <summary>Tidak kedaluwarsa.</summary>
      Never = 1,

      /// <summary>Sekian hari dari sekarang.</summary>
      Days = 2,

      /// <summary>Tanggal pilihan sendiri.</summary>
      Custom = 3
   }

   /// <summary>Satu pilihan di ComboBox masa berlaku token.</summary>
   public sealed class CtnExpiryOption
   {
      /// <summary>Tulisan di pilihan.</summary>
      public string Label { get; init; } = "";

      /// <summary>Jenis pilihannya.</summary>
      public CtnExpiryKind Kind { get; init; }

      /// <summary>Jumlah hari untuk <see cref="CtnExpiryKind.Days"/>.</summary>
      public int Days { get; init; }
   }

   /// <summary>
   /// Dialog robot container registry: membuat robot, mengedit robot, atau membuat token baru
   /// (<see cref="RobotDialogMode"/>). Hasilnya dibaca dari <see cref="Vm"/> setelah
   /// <c>ShowDialog()</c> mengembalikan <c>true</c>.
   /// </summary>
   public partial class RobotDialog : EmWindow
   {
      /// <summary>Membuat dialog; <paramref name="existing"/> wajib untuk mode Edit dan Regenerate.</summary>
      public RobotDialog(RobotDialogMode mode, RobotInfo? existing = null, RobotOwnerInfo[]? owners = null) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Initialize(mode, existing);
         foreach (var owner in owners ?? []) Vm.OwnerOptions.Add(owner);
         Vm.Refresh();
         Vm.RequestClose += result => DialogResult = result;
      }

      private void OwnerDropDownOpened(object sender, EventArgs e) {
         Vm.OwnerSearch = "";
         Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => {
            if (!ownerCombo.IsDropDownOpen) return;
            if (ownerCombo.Template.FindName("ownerResults", ownerCombo) is ListBox list) list.SelectedItem = null;
            if (ownerCombo.Template.FindName("ownerSearchBox", ownerCombo) is TextBox search) search.Focus();
         }));
      }

      private void OwnerResultClicked(object sender, MouseButtonEventArgs e) {
         if (!ownerCombo.IsDropDownOpen || sender is not ListBox list || e.OriginalSource is not DependencyObject source ||
             ItemsControl.ContainerFromElement(list, source) is not ListBoxItem { Content: RobotOwnerInfo owner }) return;
         Vm.SelectedOwner = owner;
         ownerCombo.IsDropDownOpen = false;
         ownerCombo.Focus();
      }

      private void OwnerSearchKeyDown(object sender, KeyEventArgs e) {
         if (e.Key == Key.Escape) { ownerCombo.IsDropDownOpen = false; ownerCombo.Focus(); e.Handled = true; }
         else if (e.Key == Key.Enter) {
            var match = Vm.FilteredOwners.Cast<RobotOwnerInfo>().FirstOrDefault(o => !string.IsNullOrEmpty(o.Id));
            if (match is not null) { Vm.SelectedOwner = match; ownerCombo.IsDropDownOpen = false; ownerCombo.Focus(); }
            e.Handled = true;
         }
         else if (e.Key == Key.Down && ownerCombo.Template.FindName("ownerResults", ownerCombo) is ListBox list) {
            // Focus a row without changing the committed owner until the user selects it.
            list.UpdateLayout();
            var index = list.Items.Count > 1 ? 1 : 0;
            list.SelectedIndex = index;
            if (list.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item) item.Focus();
            e.Handled = true;
         }
      }

      private void OwnerResultsKeyDown(object sender, KeyEventArgs e) {
         if (e.Key == Key.Enter && sender is ListBox { SelectedItem: RobotOwnerInfo owner }) Vm.SelectedOwner = owner;
         else if (e.Key != Key.Escape) return;
         ownerCombo.IsDropDownOpen = false;
         ownerCombo.Focus();
         e.Handled = true;
      }

      /// <summary>ViewModel dialog ini.</summary>
      public RobotDialogVm Vm => (RobotDialogVm)DataContext;
   }

   /// <summary>ViewModel untuk <see cref="RobotDialog"/>.</summary>
   public class RobotDialogVm : CtnFormVmBase
   {
      private static readonly CtnExpiryOption NeverOption = new() { Label = "No expiry", Kind = CtnExpiryKind.Never };

      public RobotDialogVm() {
         FilteredOwners = new ListCollectionView(OwnerOptions) {
            Filter = value => value is RobotOwnerInfo owner && (string.IsNullOrEmpty(owner.Id) ||
               owner.Account.Contains(OwnerSearch.Trim(), StringComparison.OrdinalIgnoreCase))
         };
         OwnerOptions.CollectionChanged += (_, _) => NotifyChanged(nameof(HasNoMatchingOwners));
      }

      /// <summary>Daftar hasil pencarian; tidak mengubah pilihan owner yang tersimpan.</summary>
      public ICollectionView FilteredOwners { get; }
      public string OwnerSearch {
         get => Get<string>() ?? "";
         set => Set(value, _ => {
            FilteredOwners.Refresh();
            NotifyChanged(nameof(HasNoMatchingOwners));
         });
      }
      public bool HasNoMatchingOwners => !FilteredOwners.Cast<RobotOwnerInfo>().Any(o => !string.IsNullOrEmpty(o.Id));

      private DateTime? _currentExpiry;

      /// <summary>Pilihan masa berlaku token.</summary>
      public ObservableCollection<CtnExpiryOption> ExpiryOptions { get; } = [];

      /// <summary>Pilihan masa berlaku yang dipilih.</summary>
      public CtnExpiryOption? SelectedExpiry {
         get => Get<CtnExpiryOption?>();
         set => Set(value, _ => {
            NotifyChanged(nameof(ShowCustomDate));
            OnInputChanged();
         });
      }

      /// <summary>Tanggal untuk pilihan <see cref="CtnExpiryKind.Custom"/>.</summary>
      public DateTime? CustomDate {
         get => Get<DateTime?>();
         set => Set(value, _ => OnInputChanged());
      }

      /// <summary>Isian tanggal tampil hanya untuk pilihan <see cref="CtnExpiryKind.Custom"/>.</summary>
      public bool ShowCustomDate => SelectedExpiry?.Kind == CtnExpiryKind.Custom;

      /// <summary>Tanggal paling awal yang boleh dipilih: hari ini.</summary>
      public DateTime EarliestDate => DateTime.Today;

      /// <summary>Kalimat peringatan di atas pilihan masa berlaku; hanya untuk Regenerate.</summary>
      public string Warning {
         get => Get<string>() ?? "";
         set => Set(value, _ => NotifyChanged(nameof(HasWarning)));
      }

      /// <summary><c>true</c> kalau ada peringatan untuk ditampilkan.</summary>
      public bool HasWarning => Warning.Length > 0;

      /// <summary>Pesan kesalahan pilihan tanggal; kosong kalau tidak ada.</summary>
      public string ExpiryError {
         get {
            if (SelectedExpiry?.Kind != CtnExpiryKind.Custom) return "";

            if (CustomDate is not { } date) return "Pick the last day the token may be used.";

            return EndOfDayUtc(date) <= DateTime.UtcNow ? "The date has to be today or later." : "";
         }
      }

      /// <summary>
      /// Masa berlaku token yang dipilih, dalam UTC; <c>null</c> berarti tidak kedaluwarsa. Tanggal
      /// pilihan sendiri berlaku sampai akhir hari itu menurut waktu lokal.
      /// </summary>
      public DateTime? TokenExpiry => SelectedExpiry?.Kind switch {
         CtnExpiryKind.Keep => _currentExpiry,
         CtnExpiryKind.Days => DateTime.UtcNow.AddDays(SelectedExpiry!.Days),
         CtnExpiryKind.Custom when CustomDate is { } date => EndOfDayUtc(date),
         _ => null
      };

      public bool ShowOwner { get => Get<bool>(); private set => Set(value); }
      public ObservableCollection<RobotOwnerInfo> OwnerOptions { get; } = [new() { Account = "No owner" }];
      public RobotOwnerInfo? SelectedOwner { get => Get<RobotOwnerInfo>(); set => Set(value); }
      public string? OwnerUserId => string.IsNullOrEmpty(SelectedOwner?.Id) ? null : SelectedOwner.Id;

      internal void Initialize(RobotDialogMode mode, RobotInfo? existing) {
         ShowOwner = mode == RobotDialogMode.Create;
         SelectedOwner = OwnerOptions[0];
         NameLabel = "Robot name";
         NamePlaceholder = "ci-bot";
         NameHelp = $"Up to {CtnInput.MaxRobotName} lowercase letters, digits, '.', '_' or '-', starting with a letter " +
                    "or digit. It is the login identity for robot-enabled managers and cannot be changed later.";

         switch (mode) {
            case RobotDialogMode.Create:
               Title = "New Robot";
               Caption = "A robot is a login identity shared by managers. It gets a token that is shown only once, " +
                         "and no access to any manager resource until you grant it.";
               OkCaption = "Create";
               Icon = EFontAwesomeIcon.Solid_Robot;
               IsNameEditable = true;
               break;
            case RobotDialogMode.Edit:
               Title = "Edit Robot";
               Caption = "Change the description, switch the robot on or off, or change when its token expires. " +
                         "A disabled robot cannot log in.";
               OkCaption = "Save";
               Icon = EFontAwesomeIcon.Solid_PenToSquare;
               IsNameEditable = false;
               ShowActive = true;
               break;
            default:
               Title = "Regenerate Token";
               Caption = existing is null ? "" : $"Create a new token for '{existing.Name}'.";
               Warning = "The current token stops working immediately. Every manager login that uses it will " +
                         "fail until it is replaced with the new one.";
               OkCaption = "Regenerate";
               Icon = EFontAwesomeIcon.Solid_ArrowsRotate;
               IsNameEditable = false;
               ShowName = false;
               ShowDescription = false;
               break;
         }

         if (existing is not null) {
            Name = existing.Name;
            Description = existing.Description ?? "";
            IsActive = existing.IsActive;
            _currentExpiry = existing.TokenExpiry is { } expiry ? CtnInput.AsUtc(expiry) : null;
         }

         BuildExpiryOptions(mode);
      }

      // "Keep current" is only offered while the current expiry is still ahead: keeping a date that has
      // passed would be refused by the server anyway.
      private void BuildExpiryOptions(RobotDialogMode mode) {
         CtnExpiryOption? initial = NeverOption;
         if (mode != RobotDialogMode.Create && _currentExpiry is { } current && current > DateTime.UtcNow) {
            initial = new CtnExpiryOption {
               Label = "Keep current (" + current.ToLocalTime().ToString("dd MMM yyyy") + ")",
               Kind = CtnExpiryKind.Keep
            };
            ExpiryOptions.Add(initial);
         }

         ExpiryOptions.Add(NeverOption);
         ExpiryOptions.Add(new CtnExpiryOption { Label = "30 days from now", Kind = CtnExpiryKind.Days, Days = 30 });
         ExpiryOptions.Add(new CtnExpiryOption { Label = "90 days from now", Kind = CtnExpiryKind.Days, Days = 90 });
         ExpiryOptions.Add(new CtnExpiryOption { Label = "365 days from now", Kind = CtnExpiryKind.Days, Days = 365 });
         ExpiryOptions.Add(new CtnExpiryOption { Label = "Choose a date...", Kind = CtnExpiryKind.Custom });
         SelectedExpiry = initial;
      }

      private static DateTime EndOfDayUtc(DateTime date) =>
         DateTime.SpecifyKind(date.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Local).ToUniversalTime();

      /// <inheritdoc />
      protected override string? ValidateName(string name) =>
         CtnInput.IsValidRobotName(name)
            ? null
            : $"Use up to {CtnInput.MaxRobotName} lowercase letters, digits, '.', '_' or '-', starting with a letter or digit.";

      /// <inheritdoc />
      public override bool OkCommandAllowed() => base.OkCommandAllowed() && ExpiryError.Length == 0;

      /// <inheritdoc />
      protected override void OnInputChanged() {
         base.OnInputChanged();
         NotifyChanged(nameof(ExpiryError));
      }
   }
}
