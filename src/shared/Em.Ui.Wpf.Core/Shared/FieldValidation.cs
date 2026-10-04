using System.Windows;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Penanda "isian ini belum benar" yang bisa dipasang ke control isian mana pun lewat attached
   /// property. Dipakai supaya layar tidak perlu tahu cara sebuah field digambar: view model cukup
   /// bilang isiannya salah, dan style field yang mengurus tampilannya (mis. garis tepi berubah
   /// merah).
   /// </summary>
   /// <remarks>
   /// Sengaja tidak memakai <c>Validation.HasError</c> bawaan WPF: aturan bawaan itu menahan nilai
   /// yang salah supaya tidak sampai ke model, sedangkan di sini nilainya tetap boleh masuk dan yang
   /// menilai benar-tidaknya adalah view model - satu tempat yang sama dengan yang menentukan tombol
   /// simpan boleh ditekan atau tidak.
   /// </remarks>
   public static class FieldValidation
   {
      /// <summary>
      /// Attached property penanda isian bermasalah. Pasang di control isian dan ikat ke property
      /// view model yang menilai isinya, mis.
      /// <c>shared:FieldValidation.HasError="{Binding HasEmailError}"</c>.
      /// </summary>
      public static readonly DependencyProperty HasErrorProperty =
         DependencyProperty.RegisterAttached(
            "HasError",
            typeof(bool),
            typeof(FieldValidation),
            new FrameworkPropertyMetadata(false));

      /// <summary>Membaca penanda isian bermasalah dari sebuah control.</summary>
      /// <param name="element">Control isian yang mau dibaca.</param>
      /// <returns><c>true</c> kalau isian control tersebut sedang dianggap salah.</returns>
      public static bool GetHasError(DependencyObject element) =>
         (bool)element.GetValue(HasErrorProperty);

      /// <summary>Menyetel penanda isian bermasalah pada sebuah control.</summary>
      /// <param name="element">Control isian yang mau ditandai.</param>
      /// <param name="value"><c>true</c> kalau isiannya salah, <c>false</c> kalau sudah benar.</param>
      public static void SetHasError(DependencyObject element, bool value) =>
         element.SetValue(HasErrorProperty, value);
   }
}
