using System.Windows;
using System.Windows.Markup;

namespace Em.Ui.Wpf.Controls.Tests
{
   /// <summary>
   /// Checks the assembly boundary of the WPF controls and their XAML namespace.
   /// </summary>
   public class DocumentsBoundaryTests
   {
      private static readonly string[] CommercialPrefixes =
         ["DevExpress", "Telerik", "Syncfusion", "Infragistics", "C1.", "ComponentOne", "Xceed", "Actipro", "SciChart"];

      [Fact]
      public void Documents_assembly_references_no_commercial_library() {
         var references = typeof(MatrixGrid).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToArray();
         Assert.DoesNotContain(references, name =>
            CommercialPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
      }

      [Fact]
      public void Documents_assembly_references_only_framework_and_engine_assemblies() {
         var references = typeof(MatrixGrid).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToArray();
         Assert.All(references, name => Assert.True(
            name.StartsWith("System", StringComparison.Ordinal) || name.StartsWith("Presentation", StringComparison.Ordinal)
            || name is "WindowsBase" or "ReachFramework" or "netstandard" || name.StartsWith("Em.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Win32", StringComparison.Ordinal),
            $"Unexpected reference: {name}"));
      }

      [Fact]
      public void Compiled_xaml_resolves_controls_through_the_namespace() {
         RunOnSta(() => {
            var sample = new XamlNamespaceSample();
            var grid = Assert.IsType<MatrixGrid>(sample.FindName("Orders"));
            Assert.Equal(["Customer", "Units"], grid.Columns.Select(column => column.Key));
            Assert.IsType<TextEditor>(sample.FindName("Note"));
            Assert.IsType<MatrixSheet>(sample.FindName("Sheet"));
            Assert.IsType<MatrixWriter>(sample.FindName("Writer"));
            Assert.IsType<PagePresenter>(sample.FindName("Preview"));
         });
      }

      [Fact]
      public void Runtime_xaml_resolves_controls_through_the_namespace() {
         // Engine types from Em.Libs are named with clr-namespace; see doc/engine/engine-documents.md#xaml-namespaces.
         RunOnSta(() => {
            const string xaml = """
               <emc:MatrixGrid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                               xmlns:emc="https://MatrixCode/wpf/controls" xmlns:grid="clr-namespace:Em.Controls.Grid;assembly=Em.Libs"
                               AutoGenerateColumns="False">
                 <emc:MatrixGrid.Columns>
                   <grid:GridColumn Key="Name" Header="Name" Kind="Text" Width="150" />
                 </emc:MatrixGrid.Columns>
               </emc:MatrixGrid>
               """;
            var grid = Assert.IsType<MatrixGrid>(XamlReader.Parse(xaml));
            Assert.Equal("Name", Assert.Single(grid.Columns).Key);

            Assert.IsType<TextEditor>(XamlReader.Parse("""<emc:TextEditor xmlns:emc="https://MatrixCode/wpf/controls" />"""));
            Assert.IsType<MatrixSheet>(XamlReader.Parse("""<emc:MatrixSheet xmlns:emc="https://MatrixCode/wpf/controls" />"""));
            Assert.IsType<MatrixWriter>(XamlReader.Parse("""<emc:MatrixWriter xmlns:emc="https://MatrixCode/wpf/controls" />"""));
            Assert.IsType<PagePresenter>(XamlReader.Parse("""<emc:PagePresenter xmlns:emc="https://MatrixCode/wpf/controls" />"""));
         });
      }

      internal static void RunOnSta(Action action) {
         Exception? failure = null;
         var thread = new Thread(() => {
            try {
               action();
            }
            catch (Exception error) {
               failure = error;
            }
         });
         thread.SetApartmentState(ApartmentState.STA);
         thread.Start();
         thread.Join();
         if (failure is not null) {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
         }
      }
   }
}
