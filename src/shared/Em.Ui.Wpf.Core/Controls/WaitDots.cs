using System.Windows;
using Control = System.Windows.Controls.Control;

namespace Em.Ui.Wpf.Controls
{
   /// <summary>
   /// Penanda tunggu kecil: tiga titik yang bergantian membesar dan mengecil sambil bergeser
   /// ke kiri dan ke kanan. Dipakai di mana pun ada pekerjaan yang ditunggu tapi layarnya tidak
   /// perlu ditutup - di sebelah tombol, di toolbar, di dalam tab, atau di sudut sebuah kartu.
   /// </summary>
   /// <remarks>
   /// Kontrol ini lookless: tampilannya seluruhnya berasal dari default style di
   /// <c>Themes/Generic.xaml</c>. Ukurannya mengikuti <c>Width</c> dan <c>Height</c> yang
   /// diberikan pemakainya (bawaan 40 x 12), warna titiknya mengikuti <c>Foreground</c> (bawaan
   /// warna aksen tema). Penanda ini hanya terlihat, tidak menahan klik; untuk menutupi layar
   /// selama pekerjaan berjalan pakai <see cref="WaitOverlay"/>, yang sendirinya memakai penanda
   /// ini.
   /// <code>
   /// &lt;local:WaitDots IsActive="{Binding InWaiting}" /&gt;
   /// </code>
   /// </remarks>
   public class WaitDots : Control
   {
      static WaitDots() {
         DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WaitDots), new FrameworkPropertyMetadata(typeof(WaitDots)));
      }

      /// <summary>Mengidentifikasi property <see cref="IsActive"/>.</summary>
      public static readonly DependencyProperty IsActiveProperty =
         DependencyProperty.Register(
            nameof(IsActive), typeof(bool), typeof(WaitDots),
            new FrameworkPropertyMetadata(true));

      /// <summary>
      /// Menyalakan dan mematikan penanda ini. Biasanya diikat ke
      /// <c>NotifyPropertyBase.InWaiting</c> milik view model layarnya.
      /// </summary>
      /// <remarks>
      /// Saat <c>false</c>, titiknya tidak digambar dan animasinya berhenti, tetapi tempatnya di
      /// layout tetap ada, jadi isi di sekitarnya tidak bergeser setiap kali pekerjaan mulai atau
      /// selesai. Bawaannya <c>true</c>, supaya penanda yang ditaruh tanpa pengikatan langsung
      /// terlihat bergerak.
      /// </remarks>
      public bool IsActive {
         get => (bool)GetValue(IsActiveProperty);
         set => SetValue(IsActiveProperty, value);
      }
   }
}
