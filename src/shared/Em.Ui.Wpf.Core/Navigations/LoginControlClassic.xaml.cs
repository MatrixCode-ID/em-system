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
   /// Layar login aplikasi, dibangun sistem navigasi sebagai body navigasi <c>admin.logon</c> di kedua
   /// layout: pada layout multi-tab ia memenuhi <see cref="Windows.TabbedMainWindow"/>, pada layout
   /// satu halaman ia tampil di dalam host navigasi window itu. Yang diserahkan cuma objek aplikasi
   /// lewat constructor; sisanya — tampilan brand maupun sambungan ViewModel ke aplikasi — disiapkan
   /// sendiri oleh control ini, jadi tampilannya tidak bisa berbeda antar layout.
   /// </summary>
   public partial class LoginControlClassic : UserControl, ILoginScreen
   {
      private readonly EmApp _app;
      private readonly LoginScreenBinding _binding;

      /// <summary>
      /// Membuat layar login untuk <paramref name="app"/>. Objek aplikasi diterima di sini supaya
      /// control ini tidak bergantung pada host-nya untuk apa pun: ViewModel-nya langsung dihubungkan
      /// ke aplikasi, dan panel brand langsung digambar lewat <see cref="RenderBranding"/>.
      /// </summary>
      /// <param name="app">Objek aplikasi tempat layar login ini berjalan.</param>
      public LoginControlClassic(EmApp app) {
         _app = app;
         InitializeComponent();

         _binding = new LoginScreenBinding(app, Vm, passwordEdit);
         RenderBranding();
      }

      /// <summary>
      /// ViewModel control ini.
      /// </summary>
      public LoginControlVm Vm => (LoginControlVm)DataContext;

      #region Render Branding

      /// <summary>
      /// Menggambar panel brand di sisi kiri layar login dari pengaturan brand aplikasi: logo, judul,
      /// sub-judul, deskripsi, dan teks hak cipta.
      /// <para>
      /// Isinya ditulis langsung ke elemen XAML-nya, bukan lewat binding. Panel brand cuma dibaca dan
      /// tidak pernah diubah user, sumbernya satu — <c>EmApp.Branding</c> — dan tidak ada control
      /// lain yang perlu mengaturnya dari luar, jadi tidak ada keadaan yang perlu diikuti UI. Nilai
      /// yang tertulis di XAML adalah tampilan tanpa objek aplikasi (mis. di designer), dan method ini
      /// yang menggantinya.
      /// </para>
      /// <para>
      /// Dipanggil sekali dari constructor, jadi layar login selalu tampil lengkap siapa pun host-nya.
      /// Warna panel tidak ditulis di sini: warnanya diambil dari tema aktif lewat resource tema, jadi
      /// ikut berganti sendiri setiap kali tema aplikasi berganti.
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
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Task.CompletedTask;

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
