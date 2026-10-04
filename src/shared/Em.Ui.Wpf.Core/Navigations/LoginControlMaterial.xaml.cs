using System.Windows;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   /// <summary>Layar login Material dengan card di atas background bertema.</summary>
   public partial class LoginControlMaterial : UserControl, ILoginScreen
   {
      private readonly EmApp _app;
      private readonly LoginScreenBinding _binding;

      /// <summary>Membuat layar login untuk aplikasi.</summary>
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

      /// <summary>ViewModel login bersama.</summary>
      public LoginControlVm Vm => (LoginControlVm)DataContext;

      /// <summary>Menggambar logo, judul, tagline, dan hak cipta aplikasi.</summary>
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

      /// <summary>Menangani masuk navigasi.</summary>
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;
      /// <summary>Menangani keluar navigasi.</summary>
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;
      /// <summary>Menangani permintaan reload.</summary>
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Task.CompletedTask;
      /// <summary>Melepas langganan aplikasi dan ViewModel.</summary>
      public Task OnRelease(INavigation sender) {
         _app.ThemeChanged -= ThemeChanged;
         _binding.Release();
         return Task.CompletedTask;
      }
   }
}
