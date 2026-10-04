namespace Em.Ui.Maui.Styles
{
   /// <summary>
   /// Kamus sumber daya yang memuat seluruh bahasa desain aplikasi. Ditulis sebagai class supaya bisa
   /// disebut dari project aplikasi - kamus yang hanya berupa berkas XAML tidak bisa dirujuk lintas
   /// assembly.
   /// </summary>
   public partial class MaterialDesign : ResourceDictionary
   {
      public MaterialDesign() {
         InitializeComponent();
      }
   }
}
