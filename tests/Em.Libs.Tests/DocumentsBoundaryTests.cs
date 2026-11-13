using Em.Grid;

namespace Em.Libs.Tests
{
   /// <summary>
   /// Keeps the renderer-neutral document engine synced from MatrixGrid free of UI frameworks, so a server can use it.
   /// </summary>
   public class DocumentsBoundaryTests
   {
      private static readonly string[] UiAssemblies =
         ["PresentationFramework", "PresentationCore", "WindowsBase", "System.Xaml", "ReachFramework", "System.Printing",
          "System.Windows.Forms", "Microsoft.Maui", "Microsoft.Maui.Controls", "Avalonia"];

      [Fact]
      public void Em_Libs_does_not_reference_ui_assemblies() {
         var references = typeof(GridValue).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToArray();
         Assert.Empty(references.Where(name => UiAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase)));
      }

      [Fact]
      public void Em_Libs_has_no_types_in_System_Windows_namespaces() {
         var types = typeof(GridValue).Assembly.GetTypes();
         Assert.DoesNotContain(types, type => type.Namespace?.StartsWith("System.Windows", StringComparison.Ordinal) == true);
      }

      [Fact]
      public void Bundled_document_font_is_embedded_under_its_documented_name() {
         using var stream = typeof(GridValue).Assembly.GetManifestResourceStream("Em.Documents.Assets.NotoSans-Regular.ttf");
         Assert.NotNull(stream);
         Assert.True(stream!.Length > 100_000);
      }
   }
}
