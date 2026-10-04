using System.Windows;
using Control = System.Windows.Controls.Control;

namespace Em.Ui.Wpf.Controls
{
   /// <summary>
   /// Lapisan tunggu yang menutupi layar selama ada pekerjaan yang harus ditunggu - memuat daftar,
   /// menyimpan baris, atau apa pun yang menunggu jawaban server. Selain memberi tahu bahwa
   /// aplikasi sedang bekerja, lapisan ini juga menahan klik, sehingga pekerjaan yang sama tidak
   /// bisa dijalankan dua kali hanya karena tombolnya sempat ditekan lagi.
   /// </summary>
   /// <remarks>
   /// Kontrol ini lookless: tampilannya seluruhnya berasal dari default style di
   /// <c>Themes/Generic.xaml</c>, jadi pemakainya cukup menaruhnya sebagai anak terakhir dari
   /// panel yang mau ditutupi dan mengikat <see cref="IsWaiting"/>. Menaruhnya di urutan terakhir
   /// itu penting: yang terakhir digambarlah yang berada paling atas.
   /// <code>
   /// &lt;local:WaitOverlay IsWaiting="{Binding InWaiting}" Caption="{Binding WaiterText}" /&gt;
   /// </code>
   /// Lapisan ini hanya menutupi panel tempat ia ditaruh, jadi satu layar boleh punya beberapa
   /// lapisan sekaligus - misalnya satu per grid - masing-masing diikat ke property miliknya
   /// sendiri (<c>LeftGridWaiting</c>, <c>RightGridWaiting</c>, ...) dengan <see cref="Heading"/>,
   /// <see cref="Caption"/> dan <c>Background</c> sendiri. Bidang yang kecil otomatis hanya
   /// menampilkan titik tunggu (lihat <see cref="IsCompact"/>).
   /// <c>InWaiting</c> bawaan view model cukup untuk satu lapisan; untuk beberapa lapisan buat
   /// property bool per bidang.
   /// </remarks>
   public class WaitOverlay : Control
   {
      static WaitOverlay() {
         DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WaitOverlay), new FrameworkPropertyMetadata(typeof(WaitOverlay)));
      }

      /// <summary>Mengidentifikasi property <see cref="IsBare"/>.</summary>
      public static readonly DependencyProperty IsBareProperty =
         DependencyProperty.Register(
            nameof(IsBare), typeof(bool), typeof(WaitOverlay),
            new FrameworkPropertyMetadata(false));

      /// <summary>Mengidentifikasi property <see cref="CornerRadius"/>.</summary>
      public static readonly DependencyProperty CornerRadiusProperty =
         DependencyProperty.Register(
            nameof(CornerRadius), typeof(CornerRadius), typeof(WaitOverlay),
            new FrameworkPropertyMetadata(new CornerRadius(0)));

      /// <summary>
      /// Kalau <c>true</c>, lapisan hanya menampilkan titik tunggu yang besar langsung di atas
      /// scrim: tanpa kartu, <see cref="Heading"/>, dan <see cref="Caption"/>. Cocok untuk grid
      /// atau daftar yang cukup digelapkan sementara datanya dimuat ulang.
      /// </summary>
      public bool IsBare {
         get => (bool)GetValue(IsBareProperty);
         set => SetValue(IsBareProperty, value);
      }

      /// <summary>
      /// Kelengkungan sudut scrim. Samakan dengan <c>CornerRadius</c> panel yang ditutupi supaya
      /// sudut scrim tidak menyembul keluar dari panel yang bersudut membulat.
      /// </summary>
      public CornerRadius CornerRadius {
         get => (CornerRadius)GetValue(CornerRadiusProperty);
         set => SetValue(CornerRadiusProperty, value);
      }

      private static readonly DependencyPropertyKey IsCompactPropertyKey =
         DependencyProperty.RegisterReadOnly(
            nameof(IsCompact), typeof(bool), typeof(WaitOverlay),
            new FrameworkPropertyMetadata(false));

      /// <summary>Mengidentifikasi property <see cref="IsCompact"/>.</summary>
      public static readonly DependencyProperty IsCompactProperty = IsCompactPropertyKey.DependencyProperty;

      /// <summary>
      /// Bernilai <c>true</c> selama bidang yang ditutupi terlalu kecil untuk memuat kartu lengkap
      /// (lebih sempit dari 220 atau lebih pendek dari 140). Kartunya lalu hanya menampilkan titik
      /// tunggu, tanpa <see cref="Heading"/> dan <see cref="Caption"/>. Dihitung ulang setiap
      /// ukuran bidangnya berubah, jadi lapisan yang sama bisa dipakai di grid besar maupun kecil.
      /// </summary>
      public bool IsCompact => (bool)GetValue(IsCompactProperty);

      /// <inheritdoc />
      protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) {
         base.OnRenderSizeChanged(sizeInfo);
         SetValue(IsCompactPropertyKey, sizeInfo.NewSize.Width < 220 || sizeInfo.NewSize.Height < 140);
      }

      /// <summary>Mengidentifikasi property <see cref="IsWaiting"/>.</summary>
      public static readonly DependencyProperty IsWaitingProperty =
         DependencyProperty.Register(
            nameof(IsWaiting), typeof(bool), typeof(WaitOverlay),
            new FrameworkPropertyMetadata(false));

      /// <summary>Mengidentifikasi property <see cref="Heading"/>.</summary>
      public static readonly DependencyProperty HeadingProperty =
         DependencyProperty.Register(
            nameof(Heading), typeof(string), typeof(WaitOverlay),
            new FrameworkPropertyMetadata("Please wait"));

      /// <summary>Mengidentifikasi property <see cref="Caption"/>.</summary>
      public static readonly DependencyProperty CaptionProperty =
         DependencyProperty.Register(
            nameof(Caption), typeof(string), typeof(WaitOverlay),
            new FrameworkPropertyMetadata(default(string)));

      /// <summary>
      /// Menyalakan dan mematikan lapisan ini. Biasanya diikat ke
      /// <c>NotifyPropertyBase.InWaiting</c> milik view model layarnya.
      /// </summary>
      /// <remarks>
      /// Lapisan mulai menahan klik begitu bernilai <c>true</c>, tapi baru terlihat setelah
      /// jeda pendek. Jadi pekerjaan yang selesai dalam sekejap tidak menyisakan kedipan
      /// penanda tunggu di layar, sementara klik kedua tetap tertahan sejak detik pertama.
      /// </remarks>
      public bool IsWaiting {
         get => (bool)GetValue(IsWaitingProperty);
         set => SetValue(IsWaitingProperty, value);
      }

      /// <summary>
      /// Baris pertama di bawah titik tunggu. Isinya tetap sepanjang layar itu hidup, jadi
      /// pakailah kalimat yang berlaku untuk semua pekerjaan di layar tersebut.
      /// </summary>
      public string Heading {
         get => (string)GetValue(HeadingProperty);
         set => SetValue(HeadingProperty, value);
      }

      /// <summary>
      /// Baris kedua, yang menyebut pekerjaan yang sedang berjalan sekarang - biasanya diikat ke
      /// <c>NotifyPropertyBase.WaiterText</c>. Kalau kosong, barisnya tidak digambar sama sekali.
      /// </summary>
      public string? Caption {
         get => (string?)GetValue(CaptionProperty);
         set => SetValue(CaptionProperty, value);
      }
   }
}
