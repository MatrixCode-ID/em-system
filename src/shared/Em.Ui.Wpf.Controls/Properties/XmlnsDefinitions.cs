using System.Windows.Markup;

// One XAML namespace for every control in this assembly (all in the CLR namespace Em.Ui.Wpf.Controls). Renderer-neutral
// types in Em.Libs (GridColumn, GridQuery, editor settings, menu entries) are not mapped: neither the XAML compiler nor
// the runtime XAML reader honours XmlnsDefinition.AssemblyName, and Em.Libs cannot declare the attribute without WPF.
// XAML names them with clr-namespace:Em.Controls.Grid;assembly=Em.Libs and similar; see
// doc/engine/engine-documents.md#xaml-namespaces.

[assembly: XmlnsPrefix("https://MatrixCode/wpf/controls", "emc")]
[assembly: XmlnsDefinition("https://MatrixCode/wpf/controls", "Em.Ui.Wpf.Controls")]
