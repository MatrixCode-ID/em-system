using FontAwesome6;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Dasar ViewModel dialog bentuk-isian Container Manager (root, container, robot): judul, nama yang
   /// hanya bisa diisi saat membuat, deskripsi, dan status aktif. Pemeriksaan namanya ringan dan hanya
   /// supaya tombol konfirmasi tidak menyala untuk nama yang jelas salah; server tetap yang berwenang.
   /// </summary>
   public abstract class CtnFormVmBase : MvvmModelBase
   {
      /// <summary>Membuat ViewModel dan mendaftarkan command konfirmasi.</summary>
      protected CtnFormVmBase() {
         RegisterCommand(nameof(OkCommand), OkCommand, OkCommandAllowed);
      }

      /// <summary>Dipicu saat dialog hendak ditutup; <c>true</c> kalau pengguna mengonfirmasi.</summary>
      public event Action<bool>? RequestClose;

      /// <summary>Judul dialog.</summary>
      public string Title {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>Kalimat penjelas di bawah judul.</summary>
      public string Caption {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>Tulisan tombol konfirmasi.</summary>
      public string OkCaption {
         get => Get<string>() ?? "OK";
         set => Set(value);
      }

      /// <summary>Ikon di banner dialog.</summary>
      public EFontAwesomeIcon Icon {
         get => Get<EFontAwesomeIcon>();
         set => Set(value);
      }

      /// <summary>Label isian nama.</summary>
      public string NameLabel {
         get => Get<string>() ?? "Name";
         set => Set(value);
      }

      /// <summary>Teks samar di isian nama.</summary>
      public string NamePlaceholder {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>Aturan nama, ditampilkan di bawah isian.</summary>
      public string NameHelp {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>
      /// <c>true</c> kalau nama boleh diketik (membuat baru); <c>false</c> menampilkannya sebagai teks
      /// baca-saja, karena nama tidak bisa diganti sesudah dibuat.
      /// </summary>
      public bool IsNameEditable {
         get => Get<bool>();
         set => Set(value, _ => NotifyChanged(nameof(IsNameReadOnly)));
      }

      /// <summary>Kebalikan <see cref="IsNameEditable"/>, untuk binding.</summary>
      public bool IsNameReadOnly => !IsNameEditable;

      /// <summary>Apakah bagian nama tampil sama sekali.</summary>
      public bool ShowName {
         get => Get<bool>(true);
         set => Set(value);
      }

      /// <summary>Apakah isian deskripsi tampil.</summary>
      public bool ShowDescription {
         get => Get<bool>(true);
         set => Set(value);
      }

      /// <summary>Apakah pilihan Active tampil (hanya saat mengedit).</summary>
      public bool ShowActive {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>Nama yang diketik, apa adanya. Pakai <see cref="NameResult"/> untuk hasil akhirnya.</summary>
      public string Name {
         get => Get<string>() ?? "";
         set => Set(value, _ => OnInputChanged());
      }

      /// <summary>Nama tanpa spasi di kedua ujungnya.</summary>
      public string NameResult => Name.Trim();

      /// <summary>Deskripsi yang diketik, apa adanya.</summary>
      public string Description {
         get => Get<string>() ?? "";
         set => Set(value, _ => OnInputChanged());
      }

      /// <summary>Deskripsi tanpa spasi di kedua ujungnya; <c>null</c> kalau kosong.</summary>
      public string? DescriptionResult => string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();

      /// <summary>Status aktif.</summary>
      public bool IsActive {
         get => Get<bool>(true);
         set => Set(value);
      }

      /// <summary>Pesan kesalahan nama; kosong selama nama belum diketik atau sudah sah.</summary>
      public string NameError =>
         ShowName && IsNameEditable && NameResult.Length > 0 ? ValidateName(NameResult) ?? "" : "";

      /// <summary>Pesan kesalahan deskripsi; kosong kalau panjangnya wajar.</summary>
      public string DescriptionError =>
         ShowDescription && Description.Trim().Length > CtnInput.MaxDescription
            ? $"The description may be at most {CtnInput.MaxDescription} characters."
            : "";

      /// <summary>Dipanggil dialog setelah mengisi nilai awal, supaya tombol konfirmasi dinilai ulang.</summary>
      internal void Refresh() => OnInputChanged();

      /// <summary>Menutup dialog dengan hasil <c>true</c>.</summary>
      public void OkCommand() => RequestClose?.Invoke(true);

      /// <summary>Hanya kalau nama (bila diketik) dan deskripsi sah, dan isian lain di turunannya sah.</summary>
      public virtual bool OkCommandAllowed() {
         if (ShowName && IsNameEditable && (NameResult.Length == 0 || ValidateName(NameResult) is not null)) return false;

         return DescriptionError.Length == 0;
      }

      /// <summary>Jawaban <c>null</c> berarti nama sah; kalau tidak, kalimat yang menjelaskan kenapa.</summary>
      protected abstract string? ValidateName(string name);

      /// <summary>Dipanggil tiap isian berubah: memperbarui pesan kesalahan dan tombol konfirmasi.</summary>
      protected virtual void OnInputChanged() {
         NotifyChanged(nameof(NameError));
         NotifyChanged(nameof(DescriptionError));
         Commands[nameof(OkCommand)]?.RaiseCanExecuteChanged();
      }
   }
}
