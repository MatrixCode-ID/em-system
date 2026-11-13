using System.Runtime.CompilerServices;

// The document engine synced from MatrixGrid was written as one assembly. Its WPF controls and tests use internal
// members of the renderer-neutral part, which lives here. See doc/engine/engine-documents.md#layers.
[assembly: InternalsVisibleTo("Em.Ui.Wpf.Documents")]
[assembly: InternalsVisibleTo("Em.Ui.Wpf.Documents.Tests")]
[assembly: InternalsVisibleTo("Em.Libs.Tests")]
