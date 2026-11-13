using System.Windows;
using System.Windows.Markup;
using Em.Ui.Wpf.Documents.Grid;

namespace Em.Ui.Wpf.Documents.Tests
{
   /// <summary>
   /// Checks the assembly boundary of the WPF document controls and their XAML namespaces.
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
      public void Compiled_xaml_resolves_controls_through_the_namespaces() {
         RunOnSta(() => {
            var sample = new XamlNamespaceSample();
            var grid = Assert.IsType<MatrixGrid>(sample.FindName("Orders"));
            Assert.Equal(["Customer", "Units"], grid.Columns.Select(column => column.Key));
            Assert.IsType<Editors.TextEditor>(sample.FindName("Note"));
            Assert.IsType<Sheet.MatrixSheet>(sample.FindName("Sheet"));
            Assert.IsType<Writer.MatrixWriter>(sample.FindName("Writer"));
            Assert.IsType<Printing.PagePresenter>(sample.FindName("Preview"));
         });
      }

      [Fact]
      public void Runtime_xaml_resolves_controls_through_the_namespaces() {
         // Engine types from Em.Libs are named with clr-namespace; see doc/engine/engine-documents.md#xaml-namespaces.
         RunOnSta(() => {
            const string xaml = """
               <emg:MatrixGrid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                               xmlns:emg="https://MatrixCode/wpf/grid" xmlns:grid="clr-namespace:Em.Grid;assembly=Em.Libs"
                               AutoGenerateColumns="False">
                 <emg:MatrixGrid.Columns>
                   <grid:GridColumn Key="Name" Header="Name" Kind="Text" Width="150" />
                 </emg:MatrixGrid.Columns>
               </emg:MatrixGrid>
               """;
            var grid = Assert.IsType<MatrixGrid>(XamlReader.Parse(xaml));
            Assert.Equal("Name", Assert.Single(grid.Columns).Key);

            Assert.IsType<Editors.TextEditor>(XamlReader.Parse(
               """<eme:TextEditor xmlns:eme="https://MatrixCode/wpf/editors" />"""));
            Assert.IsType<Sheet.MatrixSheet>(XamlReader.Parse(
               """<ems:MatrixSheet xmlns:ems="https://MatrixCode/wpf/sheet" />"""));
            Assert.IsType<Writer.MatrixWriter>(XamlReader.Parse(
               """<emw:MatrixWriter xmlns:emw="https://MatrixCode/wpf/writer" />"""));
            Assert.IsType<Printing.PagePresenter>(XamlReader.Parse(
               """<emp:PagePresenter xmlns:emp="https://MatrixCode/wpf/printing" />"""));
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
