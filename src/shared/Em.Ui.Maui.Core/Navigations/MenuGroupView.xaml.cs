using Microsoft.Maui.Controls;

namespace Em.Ui.Maui.Navigations
{
   /// <summary>
   /// The drawer of one <see cref="MenuGroup"/> on the home screen: the group header that can be opened
   /// and closed, its screen cards, then its child groups - which this control also draws.
   /// </summary>
   public partial class MenuGroupView : ContentView
   {
      public MenuGroupView() {
         InitializeComponent();
      }
   }
}
