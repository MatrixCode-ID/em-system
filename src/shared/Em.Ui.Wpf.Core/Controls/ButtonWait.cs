using System.Windows;

namespace Em.Ui.Wpf.Controls
{
   /// <summary>
   /// Membuat sebuah tombol menunjukkan sendiri bahwa pekerjaannya sedang ditunggu: selama
   /// <see cref="IsWaitingProperty"/> bernilai <c>true</c>, tulisan atau ikon di dalam tombol
   /// diganti titik tunggu kecil (<see cref="WaitDots"/>) dan klik pada tombol itu tertahan.
   /// Lebar tombol tidak berubah, jadi tombol lain di sebelahnya tidak bergeser.
   /// </summary>
   /// <remarks>
   /// Berlaku untuk semua tombol berbingkai dari <c>Styles/Buttons.xaml</c> (filled, tonal,
   /// outlined, danger, compact). Titiknya berwarna aksen tema, sama dengan penanda tunggu utama;
   /// hanya di tombol filled (latarnya sudah aksen) titiknya putih. Tombol tetap bisa ditekan lewat keyboard selama
   /// menunggu; kalau pekerjaannya tidak boleh dijalankan dua kali, jaga juga di command-nya.
   /// <code>
   /// &lt;Button Content="Refresh" Style="{StaticResource tonalButtonStyle}"
   ///         local:ButtonWait.IsWaiting="{Binding IsRefreshing}"
   ///         Command="{Binding Commands[RefreshCommand]}" /&gt;
   /// </code>
   /// </remarks>
   public static class ButtonWait
   {
      /// <summary>Mengidentifikasi attached property <c>IsWaiting</c>.</summary>
      public static readonly DependencyProperty IsWaitingProperty =
         DependencyProperty.RegisterAttached(
            "IsWaiting", typeof(bool), typeof(ButtonWait),
            new FrameworkPropertyMetadata(false));

      /// <summary>Mengidentifikasi attached property <c>OnAccent</c>.</summary>
      public static readonly DependencyProperty OnAccentProperty =
         DependencyProperty.RegisterAttached(
            "OnAccent", typeof(bool), typeof(ButtonWait),
            new FrameworkPropertyMetadata(false));

      /// <summary>
      /// Membaca apakah tombol berlatar warna aksen, sehingga titiknya harus putih. Diisi oleh
      /// style tombol filled; tombol lain memakai warna aksen yang sama dengan penanda tunggu utama.
      /// </summary>
      public static bool GetOnAccent(DependencyObject element) =>
         (bool)element.GetValue(OnAccentProperty);

      /// <summary>Menandai tombol sebagai berlatar warna aksen.</summary>
      public static void SetOnAccent(DependencyObject element, bool value) =>
         element.SetValue(OnAccentProperty, value);

      /// <summary>Membaca apakah tombol sedang menampilkan titik tunggu.</summary>
      public static bool GetIsWaiting(DependencyObject element) =>
         (bool)element.GetValue(IsWaitingProperty);

      /// <summary>Menyalakan atau mematikan titik tunggu di dalam tombol.</summary>
      public static void SetIsWaiting(DependencyObject element, bool value) =>
         element.SetValue(IsWaitingProperty, value);
   }
}
