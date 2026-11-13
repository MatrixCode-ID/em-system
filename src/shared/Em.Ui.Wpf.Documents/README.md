# Em.Ui.Wpf.Documents

WPF document controls for Em System desktop clients:

- `MatrixGrid` — a virtualized data grid with sorting, filtering, search and finder, grouping, summaries, in-place
  editing, layouts, context menus, print preview and export (PDF, XLSX, DOCX, CSV).
- `MatrixSheet` — a spreadsheet editor (formulas, formatting, merges, frozen panes, CSV and XLSX).
- `MatrixWriter` — a word-processing editor (paged layout, tables, headers and footers, DOCX and PDF).
- Standalone and in-cell value editors (`TextEditor`, `SpinEditor`, `DateEditor`, `CheckEditor`, `ComboEditor`,
  `LookupEditor`) and `PrintPreviewWindow`.

The renderer-neutral engine (queries, documents, writers, `SheetSession`, `WriteSession`) is in `EmSys.Libs`, so a
server can build and write documents without WPF.

```csharp
// Program setup: the controls follow the active Em theme.
builder.UseDocuments();
```

```xml
<Window xmlns:emg="https://MatrixCode/wpf/grid">
  <emg:MatrixGrid ItemsSource="{Binding Orders}" />
</Window>
```

The code is synced from the MatrixGrid project; do not edit the synced folders here. Guide, XAML namespaces, theme
mapping and the sync workflow: `doc/engine/engine-documents.md` in the Em System repository. Third-party notices:
`THIRD-PARTY-NOTICES.md` and `licenses/` in this package.
