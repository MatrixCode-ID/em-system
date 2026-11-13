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
      public void Xaml_namespaces_resolve_controls_and_engine_types() {
         RunOnSta(() => {
            const string xaml = """
               <emg:MatrixGrid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                               xmlns:emg="https://MatrixCode/wpf/grid" AutoGenerateColumns="False">
                 <emg:MatrixGrid.Columns>
                   <emg:GridColumn Key="Name" Header="Name" Kind="Text" Width="150" />
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
