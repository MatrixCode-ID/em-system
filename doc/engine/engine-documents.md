# Document controls and engine

Em ships a data grid, a spreadsheet editor, a word-processing editor, value editors, a print preview and writers for
PDF, XLSX, DOCX and CSV. The code comes from the MatrixGrid project, which stays the master: Em holds a translated copy
made by a sync script, plus a few files of its own (see [Sync](#sync)).

## Layers {#layers}

| Layer | Project / package | Contents |
| --- | --- | --- |
| Renderer-neutral | `Em.Libs` / `EmSys.Libs` (`net10.0`) | Grid query, data providers, export, menus model, editor settings, document model, layout, fonts, PDF/XLSX/DOCX/CSV writers, `SheetSession`, `WriteSession` and their file formats |
| WPF | `Em.Ui.Wpf.Documents` / `EmSys.Ui.Wpf.Documents` (`net10.0-windows`) | `MatrixGrid`, `MatrixSheet`, `MatrixWriter`, value editors, menus, print preview, themes, `UseDocuments()` |

A server needs only `EmSys.Libs`: `SheetSession` and `WriteSession` are the API for building formatted documents
(styles, tables, headers and footers, fields), so they live in the neutral layer. `Em.Libs` references no UI
framework; `tests/Em.Libs.Tests/DocumentsBoundaryTests.cs` checks it. `Em.Ui.Wpf.Documents` references no commercial
control library; `tests/Em.Ui.Wpf.Documents.Tests/DocumentsBoundaryTests.cs` checks it.

The engine was written as one assembly, so `Em.Libs` grants `InternalsVisibleTo` to `Em.Ui.Wpf.Documents` and to the
two test projects (`src/shared/Em.Libs/Properties/DocumentsAccess.cs`).

### Namespace map

| MatrixGrid | Em folder | Em namespace |
| --- | --- | --- |
| `Core` | `Em.Libs/Grid` | `Em.Grid` |
| `Data` (without the sample `GeneratedOrderProvider`) | `Em.Libs/Grid/Data` | `Em.Grid.Data` |
| `Query`, `Export`, `Menus` | `Em.Libs/Grid/...` | `Em.Grid.Query`, `Em.Grid.Export`, `Em.Grid.Menus` |
| `Editors` (settings) | `Em.Libs/Editors` | `Em.Editors` |
| `Editing` | `Em.Libs/Editing` | `Em.Editing` |
| `Documents/Model`, `Layout`, `Fonts`, `Packaging`, `Formats`, `Assets` | `Em.Libs/Documents/...` | `Em.Documents.*` |
| `Documents/PdfEngine` | `Em.Libs/Documents/Pdf` | `Em.Documents.Pdf` |
| `Documents/CellsEngine` (XLSX and CSV writers) | `Em.Libs/Documents/Xlsx` | `Em.Documents.Xlsx` |
| `Documents/WriterEngine` | `Em.Libs/Documents/Docx` | `Em.Documents.Docx` |
| `Sheet/Core` | `Em.Libs/Sheet/Cells` | `Em.Sheet.Cells` |
| `Sheet/*` | `Em.Libs/Sheet/*` | `Em.Sheet.*` |
| `Write/*` | `Em.Libs/Write/*` | `Em.Write.*` (types keep their `Write*` names) |
| `Wpf/Grid`, `Editors`, `Editing`, `Menus`, `Printing`, `Sheet`, `Themes` | `Em.Ui.Wpf.Documents/...` | `Em.Ui.Wpf.Documents.*` |
| `Wpf/Write` | `Em.Ui.Wpf.Documents/Writer` | `Em.Ui.Wpf.Documents.Writer` |

Control names: MatrixGrid's `GridControl` is `MatrixGrid` here and `MatrixWrite` is `MatrixWriter`; `MatrixSheet`
keeps its name. Other type names are unchanged. Resource keys such as `MatrixGrid.Brush.Primary` and `Material.*` are
not renamed.

Because `Em.Grid`, `Em.Editors`, `Em.Editing`, `Em.Documents`, `Em.Sheet` and `Em.Write` are namespaces, code that is
itself inside the `Em` namespace resolves a simple name such as `Grid` to `Em.Grid` before any `using` directive at the
top of the file. Engine code under `Em.*` that means the WPF or MAUI `Grid` control therefore names it with an alias or
a qualified name. Code in other root namespaces is not affected.

## Server: building files {#server}

A spreadsheet with `SheetSession` and `SheetFiles` (namespaces `Em.Sheet.Commands`, `Em.Sheet.Cells`, `Em.Sheet.IO`):

```csharp
var session = new SheetSession();
session.SetCell(new CellAddress(0, 0), "Customer");
session.SetCell(new CellAddress(0, 1), "Total");
session.SetCell(new CellAddress(1, 0), "Northwind");
session.SetCell(new CellAddress(1, 1), "1250.5");
session.SetCell(new CellAddress(2, 1), "=SUM(B2:B2)");
session.Format(new CellRange(new(0, 0), new(0, 1)), style => style with { Bold = true });
session.Format(new CellRange(new(1, 1), new(2, 1)), style => style with { NumberFormat = "#,##0.00" });

using var file = File.Create("report.xlsx");
SheetFiles.SaveXlsx(session.Workbook, file);
```

A letter with `WriteSession` and `WriteFiles` (namespaces `Em.Write.Editing`, `Em.Write.Model`, `Em.Write.IO`):

```csharp
var session = new WriteSession(WriteDocument.Create("Quarterly summary\nSales grew in every region."));
var title = session.Paragraphs().First();
session.Select(new WriteSelection(title, title));
session.ApplyStyle("Title", WriteStyleKind.Paragraph);

var last = session.Paragraphs().Last();
var end = last with { Offset = session.GetParagraph(last)!.Length };
session.Select(new WriteSelection(end, end));
session.InsertTable(rows: 2, columns: 3);   // inserted after the current body paragraph

using (var docx = File.Create("summary.docx"))
   WriteFiles.SaveDocx(session.Document, docx);
await using (var pdf = File.Create("summary.pdf"))
   await WriteFiles.SavePdfAsync(session.Document, pdf);
```

A grid-shaped report from any `IGridDataProvider` with `GridDocument` (namespaces `Em.Grid`, `Em.Grid.Data`,
`Em.Grid.Export`, `Em.Grid.Query`):

```csharp
var columns = ImmutableArray.Create(
   new GridColumn("Customer", "Customer", GridValueKind.Text),
   new GridColumn("Total", "Total", GridValueKind.Number));
var provider = new InMemoryGridDataProvider(rows);   // or your own provider over a database query
var snapshot = new GridExportSnapshot(columns, GridQuery.Empty, []);
var document = GridDocument.Create(provider, snapshot, new GridExportOptions { Title = "Customers" });

var writer = GridDocument.CreateFormats().Get("xlsx");   // "pdf", "docx" or "csv"
await using var output = File.Create("customers.xlsx");
await writer.WriteAsync(document, output);
```

Rows are streamed from the provider while the file is written, so large views need not fit in memory.

## WPF: controls {#wpf}

Reference `EmSys.Ui.Wpf.Documents` and call `builder.UseDocuments()` in the host setup (see [Theme](#theme)).

### XAML namespaces {#xaml-namespaces}

| URI | Prefix | CLR namespaces |
| --- | --- | --- |
| `https://MatrixCode/wpf/grid` | `emg` | `Em.Ui.Wpf.Documents.Grid`, `Em.Grid`, `Em.Grid.Query`, `Em.Grid.Menus` |
| `https://MatrixCode/wpf/editors` | `eme` | `Em.Ui.Wpf.Documents.Editors`, `Em.Editors` |
| `https://MatrixCode/wpf/sheet` | `ems` | `Em.Ui.Wpf.Documents.Sheet` |
| `https://MatrixCode/wpf/writer` | `emw` | `Em.Ui.Wpf.Documents.Writer` |
| `https://MatrixCode/wpf/printing` | `emp` | `Em.Ui.Wpf.Documents.Printing` |

`Em.Ui.Wpf.Documents.Menus`, `.Editing` and `.Themes` are mapped to all five URIs. The URIs are case-sensitive. The
`Em.Libs` namespaces are mapped from `Em.Ui.Wpf.Documents` with `XmlnsDefinition(..., AssemblyName = "Em.Libs")`
(`src/shared/Em.Ui.Wpf.Documents/Properties/XmlnsDefinitions.cs`). The runtime XAML reader resolves them
(`Xaml_namespaces_resolve_controls_and_engine_types`). The compile-time XAML compiler has not been verified with a
consumer project yet; if it rejects a mapped engine type, declare it with
`xmlns:grid="clr-namespace:Em.Grid;assembly=Em.Libs"`.

### MatrixGrid

```xml
<Window xmlns:emg="https://MatrixCode/wpf/grid" xmlns:eme="https://MatrixCode/wpf/editors">
  <emg:MatrixGrid ItemsSource="{Binding Orders}" AutoGenerateColumns="False" ShowGroupPanel="True">
    <emg:MatrixGrid.Columns>
      <emg:GridColumn Key="Customer" Header="Customer" Kind="Text" Width="180" />
      <emg:GridColumn Key="Units" Header="Units" Kind="Number" />
    </emg:MatrixGrid.Columns>
  </emg:MatrixGrid>
</Window>
```

- `ItemsSource` takes in-memory objects; public properties become columns when `AutoGenerateColumns` is true.
- `Provider` takes an `IGridDataProvider` that filters, sorts, groups and pages the data itself (for large or remote
  data). Implement `IDistinctValueCountsProvider` for filter value counts and `IStreamingGridDataProvider` for fast
  exports.
- `Query` (two-way) holds sorting, filters, search, groups and summaries. `CaptureLayout()` / `RestoreLayout()` save
  and restore the layout as JSON.
- `ShowPrintPreview()`, `ExportViewAsync()`, `ExportCommand` and `PrintPreviewCommand` print and export the current
  view; `ExportFormats` holds the writers.
- `MenuDefinitions`, `MenuOpening` and `MenuOpeningCommand` customize the context menus.

### MatrixSheet and MatrixWriter

```xml
<ems:MatrixSheet xmlns:ems="https://MatrixCode/wpf/sheet" ToolbarStyle="Ribbon" Source="{Binding WorkbookPath}" />
<emw:MatrixWriter xmlns:emw="https://MatrixCode/wpf/writer" Mode="Viewer" Source="{Binding DocumentPath}" />
```

Both derive from `EditorControl`: `Mode` (`Full` or `Viewer`), `ToolbarStyle` (`Compact` or `Ribbon`),
`ShowToolbar`, `AllowOpen`, `Source` (a file opened in the background), `ExecuteCommand(id)` and the `DocumentChanged`
event. `Session` gives the underlying `SheetSession` or `WriteSession`; `LoadWorkbook` and `LoadDocument` show a
workbook or document built in code.

### Print preview

`PrintPreviewWindow` takes a function that builds a `Document` for a page setup, plus the export formats. The grid and
both editors open it themselves; open it directly for documents built with `GridDocument` or your own blocks.

## Theme {#theme}

The controls draw with `MatrixGrid.Brush.*` resources. On their own they use the built-in Light palette (or Dark
through `GridTheme.Mode`). `builder.UseDocuments()` registers `DocumentsThemeApplier`, which sets
`GridTheme.UseHostPalette` and writes every palette brush into the application resources each time the Em theme is
applied, so switching the Em theme switches the controls, their popups, menus and dialogs.

| Palette role | Source |
| --- | --- |
| `Primary`, `OnPrimary`, `PrimaryContainer`, `OnPrimaryContainer` | Em `Primary`, `OnPrimary`, `PrimaryContainer`, `OnPrimaryContainer` |
| `SecondaryContainer`, `OnSecondaryContainer` | Em `SecondaryContainer`, `OnSecondaryContainer` |
| `Success`, `Error`, `ErrorContainer`, `Scrim` | Em `Success`, `Error`, `ErrorContainer`, `Scrim` |
| `Surface`, `SurfaceContainerLow`, `SurfaceContainer`, `SurfaceContainerHigh` | Em tokens of the same name |
| `OnSurface`, `OnSurfaceVariant`, `Outline`, `OutlineVariant` | Em tokens of the same name |
| `StateHover` | Em `OnSurface` at 8% opacity |
| `StateSelected` | Em `Primary` at 12% (Light) or 16% (Dark) opacity |
| `StateDisabled` | Em `OnSurface` at 38% opacity |
| `TertiaryContainer`, `OnTertiaryContainer`, `SurfaceContainerLowest`, `SurfaceContainerHighest`, `InverseSurface`, `InverseOnSurface` | Built-in Light or Dark palette, chosen by `ThemeBase.Variant` (Em has no such tokens) |

Without `UseDocuments()` the controls keep their built-in palette, which is also how they behave outside an Em host.

## Renderers {#renderers}

Only WPF is implemented. The neutral layer (queries, documents, layout as drawing commands, sessions, writers) has no UI
dependency, so another renderer gets its own project, for example `Em.Ui.Avalonia.Documents`, with the same control
class names, without changing `Em.Libs`. MAUI has no document controls yet; a MAUI app can still use the neutral layer.

## Sync {#sync}

MatrixGrid is developed in the sibling checkout `..\MatrixGrid` and stays the master. Features and fixes are made
there, then copied:

1. Commit the change in MatrixGrid (its public members need English XML comments; CS1591 is an error there).
2. Run `scripts\sync-matrixgrid.cmd` or the RunBay action **Sync MatrixGrid**. `-Source <path>` points to another
   checkout and `-WhatIf` only reports.
3. Build and test (`Build Documents`, `Test Documents`), then commit with the MatrixGrid commit in the message.
   `scripts/sync-matrixgrid/last-sync.json` records the source commit and time.

`scripts/sync-matrixgrid/map.json` drives the copy: folder maps (code, tests, `licenses/`), full-name namespace maps,
the two control renames (also applied to file names), string rewrites (pack URIs, `InternalsVisibleTo`, the old xmlns
URI), exclusions (demo, benchmarks, the FlaUI demo smoke test, MatrixGrid's own architecture tests and xmlns file, project
files) and the files Em owns. The script copies only files tracked by Git in MatrixGrid, converts the tests from xUnit
v2 to v3, and deletes a copied file whose source is gone, but only inside mapped folders and never an owned file.

**Do not edit synced files in Em.** They are overwritten by the next sync. Fix the map or the script when a translation
is wrong, or fix the source in MatrixGrid and sync again. Files Em owns:

- `src/shared/Em.Libs/Em.Libs.csproj`, `src/shared/Em.Libs/Properties/DocumentsAccess.cs`
- `src/shared/Em.Ui.Wpf.Documents/Em.Ui.Wpf.Documents.csproj`, `README.md`, `DocumentsExtensions.cs`,
  `DocumentsThemeApplier.cs`, `Properties/XmlnsDefinitions.cs`
- `tests/Em.Libs.Tests/DocumentsBoundaryTests.cs`, `tests/Em.Ui.Wpf.Documents.Tests/Em.Ui.Wpf.Documents.Tests.csproj`,
  `DocumentsBoundaryTests.cs`, `DocumentsThemeApplierTests.cs`
- `THIRD-PARTY-NOTICES.md`

The sample `GeneratedOrderProvider` becomes the test fixture `tests/Em.Libs.Tests/Grid/Fixtures/`, which the WPF tests
link. Third-party licenses are in `THIRD-PARTY-NOTICES.md` and `licenses/`, packed with both packages.

## Limitations {#limitations}

The controls inherit MatrixGrid's known limitations; see `docs/KnownLimitations.md`, `docs/SheetEngine.md` and
`docs/WriteEngine.md` in the MatrixGrid repository (not copied here). In Em in particular:

- The bundled Noto Sans font (about 570 KB) is embedded in `Em.Libs`, so every consumer of `EmSys.Libs`, including MAUI
  apps, carries it. `Em.Ui.Wpf.Documents` embeds a second copy as a WPF resource for on-screen text.
- No GUI or visual check of the controls inside an Em host has been run yet.
