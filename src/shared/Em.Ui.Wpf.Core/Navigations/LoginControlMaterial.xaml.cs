using System.Windows;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   /// <summary>The Material login screen with a card over a themed background.</summary>
   public partial class LoginControlMaterial : UserControl, ILoginScreen
   {
      private readonly EmApp _app;
      private readonly LoginScreenBinding _binding;

      /// <summary>Creates the login screen for the application.</summary>
      public LoginControlMaterial(EmApp app) {
         _app = app;
         InitializeComponent();
         FieldLabel.SetEnableAnimation(this, app.EnableFieldAnimation);
         _binding = new LoginScreenBinding(app, Vm, passwordEdit);
         Vm.RefreshThemeState();
         RenderBranding();
         RenderBackground();
         app.ThemeChanged += ThemeChanged;
      }

      /// <summary>The shared login view model.</summary>
      public LoginControlVm Vm => (LoginControlVm)DataContext;

      /// <summary>Draws the application's logo, title, tagline, and copyright.</summary>
      public void RenderBranding() {
         var branding = _app.Branding;
         brandLogo.Source = BrandingImages.LoadLogo(branding, _app.ServiceProvider);
         brandTitle.Text = branding.DisplayTitle;
         brandTagline.Text = branding.DisplayTagline;
         brandCopyright.Text = branding.DisplayCopyright;
      }

      private void RenderBackground() {
         backgroundImage.Source = BrandingImages.LoadLoginBackground(_app.Branding, _app.CurrentTheme,
            _app.ServiceProvider, out var dimmed);
         backgroundImage.Visibility = backgroundImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
         backgroundScrim.Visibility = dimmed ? Visibility.Visible : Visibility.Collapsed;
      }
      private void ThemeChanged(object? sender, EventArgs e) {
         Vm.RefreshThemeState();
         RenderBackground();
      }
      private void PasswordEdit_PasswordChanged(object sender, RoutedEventArgs e) => _binding?.PasswordChanged();
      private void CredentialField_PreviewKeyDown(object sender, KeyEventArgs e) => _binding.CredentialKeyDown(sender, e);

      /// <summary>Handles navigation coming in.</summary>
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;
      /// <summary>Handles navigation going away.</summary>
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;
      /// <summary>Handles a reload request.</summary>
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Task.CompletedTask;
      /// <summary>Releases the subscriptions to the application and the view model.</summary>
      public Task OnRelease(INavigation sender) {
         _app.ThemeChanged -= ThemeChanged;
         _binding.Release();
         return Task.CompletedTask;
      }
   }
}
