using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   /// <summary>
   /// The application's login screen, built by the navigation system as the body of navigation
   /// <c>admin.logon</c> in both layouts: in the multi-tab layout it fills the
   /// <see cref="Windows.TabbedMainWindow"/>, in the single-page layout it appears inside that window's
   /// navigation host. Only the application object is handed over, through the constructor; the rest - the
   /// brand display and the connection of the view model to the application - is prepared by this control
   /// itself, so its look cannot differ between layouts.
   /// </summary>
   public partial class LoginControlClassic : UserControl, ILoginScreen
   {
      private readonly EmApp _app;
      private readonly LoginScreenBinding _binding;

      /// <summary>
      /// Creates the login screen for <paramref name="app"/>. The application object is received here so this
      /// control depends on its host for nothing: its view model is connected to the application right away,
      /// and the brand panel is drawn right away through <see cref="RenderBranding"/>.
      /// </summary>
      /// <param name="app">The application object in which this login screen runs.</param>
      public LoginControlClassic(EmApp app) {
         _app = app;
         InitializeComponent();

         _binding = new LoginScreenBinding(app, Vm, passwordEdit);
         RenderBranding();
      }

      /// <summary>
      /// The view model of this control.
      /// </summary>
      public LoginControlVm Vm => (LoginControlVm)DataContext;

      #region Render Branding

      /// <summary>
      /// Draws the brand panel on the left side of the login screen from the application's brand settings: the
      /// logo, title, subtitle, description, and copyright text.
      /// <para>
      /// Its content is written straight to its XAML elements, not through binding. The brand panel is only
      /// read and never changed by the user, its source is one - <c>EmApp.Branding</c> - and no other control
      /// needs to set it from outside, so there is no state for the UI to follow. The values written in XAML
      /// are the display without an application object (e.g. in the designer), and this method replaces them.
      /// </para>
      /// <para>
      /// It is called once from the constructor, so the login screen always appears complete whoever its host
      /// is. The panel color is not written here: its color is taken from the active theme through theme
      /// resources, so it changes by itself every time the application theme changes.
      /// </para>
      /// </summary>
      public void RenderBranding() {
         var branding = _app.Branding;

         brandLogo.Source = BrandingImages.LoadLogo(branding, _app.ServiceProvider);
         brandTitle.Text = branding.DisplayTitle;
         brandTagline.Text = branding.DisplayTagline;
         brandDescription.Text = branding.DisplayDescription;
         brandCopyright.Text = branding.DisplayCopyright;
      }

      #endregion

      #region Navigation Body

      // Which parts of the navigation toolbar this screen offers is declared once, on the
      // admin.logon navigation itself (see EmApp.InitInternalNavigation), instead of being
      // switched on and off from here - the navigation these two receive is the one being left,
      // not this one, so toggling through it would repaint the wrong screen's toolbar.
      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) {
         _binding.Release();
         return Task.CompletedTask;
      }

      #endregion

      private void PasswordEdit_PasswordChanged(object sender, RoutedEventArgs e) =>
         _binding?.PasswordChanged();

      private void CredentialField_PreviewKeyDown(object sender, KeyEventArgs e) =>
         _binding.CredentialKeyDown(sender, e);
   }
}
