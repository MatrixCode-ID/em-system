using System.Runtime.CompilerServices;
using System.Windows;

namespace Em.Ui.Wpf.Core.Tests
{
   public class SharedStylesTests
   {
      private const string MaterialDesignUri = "pack://application:,,,/Em.Ui.Wpf.Core;component/Styles/MaterialDesign.xaml";

      // Theme tokens are read through DynamicResource, so the shared dictionary loads without a theme applied;
      // a broken StaticResource or a missing merged file fails here instead of on the first screen that opens.
      [WpfTheory]
      [InlineData("surfaceListBoxStyle")]
      [InlineData("surfaceTreeViewStyle")]
      public void MaterialDesign_ContainsSharedStyle(string key) {
         // Application's static constructor registers the pack:// handler that ResourceDictionary.Source reads
         // through; running it is enough, an Application instance (one per process) is not needed.
         RuntimeHelpers.RunClassConstructor(typeof(Application).TypeHandle);
         var resources = new ResourceDictionary { Source = new Uri(MaterialDesignUri, UriKind.Absolute) };

         Assert.IsType<Style>(resources[key]);
      }
   }
}
