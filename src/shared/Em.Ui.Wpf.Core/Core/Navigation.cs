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
   /// <summary>Definition of a screen that can be opened by name: its title, body type, menu place, and access binding.</summary>
   public sealed class Navigation : INavigation, INotifyPropertyChanged
   {
      /// <summary>The name.</summary>
      public required string Name { get; init; }
      /// <summary>The title.</summary>
      public string Title { get; set; } = string.Empty;
      /// <summary>The subtitle.</summary>
      public string Subtitle { get; set; } = string.Empty;
      /// <summary>The description.</summary>
      public string Description { get; set; } = string.Empty;
      /// <summary>The menu path.</summary>
      public MenuPath? MenuPath { get; set; }
      IBodyType INavigation.BodyType => BodyType;
      /// <summary>The body type.</summary>
      public required BodyType BodyType { get; init; }

      /// <summary>
      /// The kind of this screen - <see cref="NavigationKind.Manager"/> or <see cref="NavigationKind.Editor"/>.
      /// Required every time a navigation is registered, in any layout.
      /// </summary>
      public required NavigationKind Kind { get; init; }
      INavigationHost INavigation.NavigationHost => EmApp;
      /// <summary>The em app.</summary>
      public EmApp EmApp { get; internal set; } = null!;
      /// <summary>Indicates menu visible.</summary>
      public bool IsMenuVisible {
         get;
         set => SetField(ref field, value);
      }

      // Per-navigation toolbar switches, read by the SPA host when it shows this navigation.
      // They default to true so a navigation only has to name what it wants hidden.
      /// <summary>Indicates toolbar visible.</summary>
      public bool IsToolbarVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      /// <summary>Indicates title visible.</summary>
      public bool IsTitleVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      /// <summary>Indicates back visible.</summary>
      public bool IsBackVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      /// <summary>Indicates forward visible.</summary>
      public bool IsForwardVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      /// <summary>Indicates reload visible.</summary>
      public bool IsReloadVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      /// <summary>Indicates home visible.</summary>
      public bool IsHomeVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      /// <summary>Indicates detach visible.</summary>
      public bool IsDetachVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      /// <summary>Indicates color theme visible.</summary>
      public bool IsColorThemeVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      /// <summary>Indicates user visible.</summary>
      public bool IsUserVisible {
         get;
         set => SetField(ref field, value);
      } = true;
      /// <summary>The order index.</summary>
      public required int OrderIndex { get; init; }

      /// <summary>
      /// The name of the module that owns this navigation, set by <see cref="Shared.EmAppBuilder"/> through
      /// the module-bound <c>AddNavigation</c> overload - <c>null</c> when this navigation was registered
      /// without a binding. Only the builder may set it, so the module binding always comes from registration,
      /// not guessed afterwards.
      /// </summary>
      public string? ModuleName { get; internal set; }

      /// <summary>
      /// The claim a user must hold to open this navigation, set by <see cref="Shared.EmAppBuilder"/> through
      /// the claim-bound <c>AddNavigation</c> overload - <c>null</c> when this navigation is only bound to the
      /// module without a particular claim.
      /// </summary>
      public ClaimAction? RequiredClaim { get; internal set; }

      /// <summary>The navigation icon.</summary>
      public ImageSource NavigationIcon {
         get => field ??= EFontAwesomeIcon.Regular_WindowRestore.CreateImageSource(Brushes.Gray);
         set;
      }

      /// <summary>Indicates require parameter.</summary>
      public required bool RequireParameter { get; init; }

      #region INotifyPropertyChanged Implementation Methods
      
      /// <summary>Raised for property changed.</summary>
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
