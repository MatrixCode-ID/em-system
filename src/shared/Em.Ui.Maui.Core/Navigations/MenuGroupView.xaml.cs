using Microsoft.Maui.Controls;

namespace Em.Ui.Maui.Navigations
{
   /// <summary>
   /// Penggambar satu <see cref="MenuGroup"/> di layar home: kepala grup yang bisa dibuka-tutup,
   /// kartu-kartu layar miliknya, lalu grup anaknya - yang digambar control ini juga.
   /// </summary>
   public partial class MenuGroupView : ContentView
   {
      public MenuGroupView() {
         InitializeComponent();
      }
   }
}
