# Em.Ui.Wpf.Controls

WPF controls for Em System desktop clients, all in the namespace `Em.Ui.Wpf.Controls`:

- `MatrixGrid` — a virtualized data grid with sorting, filtering, search and finder, grouping, summaries, in-place
  editing, layouts, context menus, print preview and export (PDF, XLSX, DOCX, CSV).
- `MatrixSheet` — a spreadsheet editor (formulas, formatting, merges, frozen panes, CSV and XLSX).
- `MatrixWriter` — a word-processing editor (paged layout, tables, headers and footers, DOCX and PDF).
- `DocumentViewer` — a read-only viewer screen registered by `UseDocuments()`; `NavigationEntry.ViewDocument(...)`
  shows a downloaded spreadsheet or document in MatrixSheet or MatrixWriter and sends PDF to the PDF viewer.
- Standalone and in-cell value editors (`TextEditor`, `SpinEditor`, `DateEditor`, `CheckEditor`, `ComboEditor`,
  `LookupEditor`) and `PrintPreviewWindow`.

The renderer-neutral engine (`Em.Controls.Grid`, `Em.Controls.Sheet`, `Em.Controls.Write`, documents and writers) is
in `EmSys.Libs`, so a server can build and write documents without WPF.

```csharp
// Program setup: the controls follow the active Em theme.
builder.UseDocuments();
```

```xml
<Window xmlns:emc="https://MatrixCode/wpf/controls" xmlns:grid="clr-namespace:Em.Controls.Grid;assembly=Em.Libs">
  <emc:MatrixGrid ItemsSource="{Binding Orders}" AutoGenerateColumns="False">
    <emc:MatrixGrid.Columns>
      <grid:GridColumn Key="Customer" Header="Customer" Kind="Text" />
    </emc:MatrixGrid.Columns>
  </emc:MatrixGrid>
</Window>
```

The XAML namespace `https://MatrixCode/wpf/controls` (prefix `emc`) covers the WPF controls; engine types such as
`GridColumn` come from `Em.Libs` through `clr-namespace`.

The code is synced from the MatrixGrid project; do not edit the synced folders here. Guide, XAML namespace, theme
mapping and the sync workflow: `doc/engine/engine-documents.md` in the Em System repository. Third-party notices:
`THIRD-PARTY-NOTICES.md` and `licenses/` in this package.
