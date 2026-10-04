using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;
using Em.Shared;
using Em.Ui.Core.Shared;
// Kedua namespace punya INavigation, dan yang dimaksud di sini selalu milik Em.
using INavigation = Em.Ui.Core.Shared.INavigation;

namespace Em.Ui.Maui.Core
{
   /// <summary>
   /// Definisi satu layar yang bisa dibuka lewat namanya, berikut saklar-saklar tampilan yang berlaku
   /// selama ia ditampilkan. Body dan datanya tidak dipegang di sini, melainkan oleh
   /// <see cref="NavigationEntry"/> yang tercipta setiap kali layar ini dibuka. Didaftarkan module lewat
   /// <c>EmAppBuilder.AddNavigation</c>.
   /// </summary>
   public sealed class Navigation : INavigation, INotifyPropertyChanged
   {
      /// <summary>Nama unik navigasi, yaitu yang dicari <c>NavigateTo(string)</c>.</summary>
      public required string Name { get; init; }

      /// <summary>Judul layar, ditampilkan di toolbar navigasi.</summary>
      public string Title { get; set; } = string.Empty;

      /// <summary>Keterangan singkat di bawah judul.</summary>
      public string Subtitle { get; set; } = string.Empty;

      /// <summary>Penjelasan panjang, dipakai kartu menu di home.</summary>
      public string Description { get; set; } = string.Empty;

      /// <summary>Letak layar ini di pohon menu home, atau <c>null</c> kalau ia tidak lewat menu.</summary>
      public MenuPath? MenuPath { get; set; }

      IBodyType INavigation.BodyType => BodyType;

      /// <summary>Tipe control yang jadi body layar ini, dideklarasikan lewat <c>BodyType.Of&lt;T&gt;()</c>.</summary>
      public required BodyType BodyType { get; init; }

      /// <summary>
      /// Jenis layar ini - <see cref="NavigationKind.Manager"/> atau <see cref="NavigationKind.Editor"/>.
      /// Wajib diisi setiap kali navigasi didaftarkan.
      /// </summary>
      public required NavigationKind Kind { get; init; }

      INavigationHost INavigation.NavigationHost => EmApp;

      /// <summary>Objek aplikasi pemilik navigasi ini, diisi sendiri saat navigasi didaftarkan.</summary>
      public EmApp EmApp { get; internal set; } = null!;

      /// <summary>Apakah layar ini ikut tampil di menu home.</summary>
      public bool IsMenuVisible {
         get;
         set => SetField(ref field, value);
      }

      // Per-navigation toolbar switches, read by the SPA host when it shows this navigation.
      // They default to true so a navigation only has to name what it wants hidden.

      /// <summary>Apakah toolbar navigasi ikut tampil saat layar ini dibuka.</summary>
      public bool IsToolbarVisible {
         get;
         set => SetField(ref field, value);
      } = true;

      /// <summary>Apakah judul layar ikut tampil di toolbar.</summary>
      public bool IsTitleVisible {
         get;
         set => SetField(ref field, value);
      } = true;

      /// <summary>Apakah tombol mundur ikut tampil.</summary>
      public bool IsBackVisible {
         get;
         set => SetField(ref field, value);
      } = true;

      /// <summary>Apakah tombol maju ikut tampil.</summary>
      public bool IsForwardVisible {
         get;
         set => SetField(ref field, value);
      } = true;

      /// <summary>Apakah tombol muat ulang ikut tampil.</summary>
      public bool IsReloadVisible {
         get;
         set => SetField(ref field, value);
      } = true;

      /// <summary>Apakah tombol pulang ke home ikut tampil.</summary>
      public bool IsHomeVisible {
         get;
         set => SetField(ref field, value);
      } = true;

      /// <summary>Apakah tombol ganti tema ikut tampil.</summary>
      public bool IsColorThemeVisible {
         get;
         set => SetField(ref field, value);
      } = true;

      /// <summary>Apakah tombol akun pengguna ikut tampil.</summary>
      public bool IsUserVisible {
         get;
         set => SetField(ref field, value);
      } = true;

      /// <summary>Urutan layar ini di menu home; semakin kecil semakin di depan.</summary>
      public required int OrderIndex { get; init; }

      /// <summary>
      /// Nama module pemilik navigasi ini, diisi <see cref="Shared.EmAppBuilder"/> lewat overload
      /// <c>AddNavigation</c> yang berikat module - <c>null</c> kalau navigasi ini didaftarkan tanpa
      /// ikatan. Hanya builder yang boleh mengisinya, supaya ikatan module selalu berasal dari
      /// pendaftaran, bukan ditebak belakangan.
      /// </summary>
      public string? ModuleName { get; internal set; }

      /// <summary>
      /// Claim yang wajib dimiliki user untuk membuka navigasi ini, diisi <see cref="Shared.EmAppBuilder"/>
      /// lewat overload <c>AddNavigation</c> yang berklaim - <c>null</c> kalau navigasi ini cukup
      /// terikat module tanpa claim tertentu.
      /// </summary>
      public ClaimAction? RequiredClaim { get; internal set; }

      /// <summary>Ikon layar ini di menu home, atau <c>null</c> kalau tidak punya.</summary>
      public ImageSource? NavigationIcon { get; set; }

      /// <summary>Apakah layar ini menolak dibuka tanpa parameter.</summary>
      public required bool RequireParameter { get; init; }

      #region INotifyPropertyChanged Implementation Methods

      /// <inheritdoc />
      public event PropertyChangedEventHandler? PropertyChanged;

      private void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
         PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
      }

      private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
         if (EqualityComparer<T>.Default.Equals(field, value)) return false;
         field = value;
         OnPropertyChanged(propertyName);
         return true;
      }

      #endregion
   }
}
