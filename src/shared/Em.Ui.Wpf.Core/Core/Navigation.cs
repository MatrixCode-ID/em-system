using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using FontAwesome6.Fonts;
using Em.Shared;
using Em.Ui.Core.Shared;
using FontAwesome6;
using FontAwesome6.Fonts.Extensions;
using Brush = System.Drawing.Brush;
using Brushes = System.Windows.Media.Brushes;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Core
{
   public sealed class Navigation : INavigation, INotifyPropertyChanged
   {
      public required string Name { get; init; }
      public string Title { get; set; } = string.Empty;
      public string Subtitle { get; set; } = string.Empty;
      public string Description { get; set; } = string.Empty;
      public MenuPath? MenuPath { get; set; }
      IBodyType INavigation.BodyType => BodyType;
      public required BodyType BodyType { get; init; }

      /// <summary>
      /// Jenis layar ini - <see cref="NavigationKind.Manager"/> atau <see cref="NavigationKind.Editor"/>.
      /// Wajib diisi setiap kali navigasi didaftarkan, di layout mana pun.
      /// </summary>
      public required NavigationKind Kind { get; init; }
      INavigationHost INavigation.NavigationHost => EmApp;
      public EmApp EmApp { get; internal set; } = null!;
      public bool IsMenuVisible {
         get;
         set => SetField(ref field, value);
      }

      // Per-navigation toolbar switches, read by the SPA host when it shows this navigation.
      // They default to true so a navigation only has to name what it wants hidden.
      public bool IsToolbarVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      public bool IsTitleVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      public bool IsBackVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      public bool IsForwardVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      public bool IsReloadVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      public bool IsHomeVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      public bool IsDetachVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      public bool IsColorThemeVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      public bool IsUserVisible {
         get;
         set => SetField(ref field, value);
      } = true;
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

      public ImageSource NavigationIcon {
         get => field ??= EFontAwesomeIcon.Regular_WindowRestore.CreateImageSource(Brushes.Gray);
         set;
      }

      public required bool RequireParameter { get; init; }

      #region INotifyPropertyChanged Implementation Methods
      
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

      private void NotifyChanged([CallerMemberName] string? propertyName = null) =>
         OnPropertyChanged(propertyName);
      
      #endregion
   }
}
