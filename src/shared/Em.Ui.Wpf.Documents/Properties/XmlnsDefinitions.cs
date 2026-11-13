using System.Windows.Markup;

// XAML namespaces of the document controls, one per control group. The renderer-neutral types live in Em.Libs and are
// mapped with AssemblyName; see doc/engine/engine-documents.md#xaml-namespaces.

[assembly: XmlnsPrefix("https://MatrixCode/wpf/grid", "emg")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/grid", "Em.Ui.Wpf.Documents.Grid")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/grid", "Em.Grid", AssemblyName = "Em.Libs")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/grid", "Em.Grid.Query", AssemblyName = "Em.Libs")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/grid", "Em.Grid.Menus", AssemblyName = "Em.Libs")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/grid", "Em.Ui.Wpf.Documents.Menus")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/grid", "Em.Ui.Wpf.Documents.Editing")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/grid", "Em.Ui.Wpf.Documents.Themes")]

[assembly: XmlnsPrefix("https://MatrixCode/wpf/editors", "eme")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/editors", "Em.Ui.Wpf.Documents.Editors")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/editors", "Em.Editors", AssemblyName = "Em.Libs")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/editors", "Em.Ui.Wpf.Documents.Menus")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/editors", "Em.Ui.Wpf.Documents.Editing")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/editors", "Em.Ui.Wpf.Documents.Themes")]

[assembly: XmlnsPrefix("https://MatrixCode/wpf/sheet", "ems")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/sheet", "Em.Ui.Wpf.Documents.Sheet")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/sheet", "Em.Ui.Wpf.Documents.Menus")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/sheet", "Em.Ui.Wpf.Documents.Editing")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/sheet", "Em.Ui.Wpf.Documents.Themes")]

[assembly: XmlnsPrefix("https://MatrixCode/wpf/writer", "emw")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/writer", "Em.Ui.Wpf.Documents.Writer")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/writer", "Em.Ui.Wpf.Documents.Menus")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/writer", "Em.Ui.Wpf.Documents.Editing")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/writer", "Em.Ui.Wpf.Documents.Themes")]

[assembly: XmlnsPrefix("https://MatrixCode/wpf/printing", "emp")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/printing", "Em.Ui.Wpf.Documents.Printing")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/printing", "Em.Ui.Wpf.Documents.Menus")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/printing", "Em.Ui.Wpf.Documents.Editing")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/printing", "Em.Ui.Wpf.Documents.Themes")]
