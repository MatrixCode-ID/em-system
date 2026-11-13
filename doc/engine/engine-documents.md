# Document controls and engine

Em ships a data grid, a spreadsheet editor, a word-processing editor, value editors, a print preview and writers for
PDF, XLSX, DOCX and CSV. The code comes from the MatrixGrid project, which stays the master: Em holds a translated copy
made by a sync script, plus a few files of its own (see [Sync](#sync)).

## Layers {#layers}

| Layer | Project / package | Contents |
| --- | --- | --- |
| Renderer-neutral | `Em.Libs` / `EmSys.Libs` (`net10.0`) | Grid query, data providers, export, menus model, editor settings, document model, layout, fonts, PDF/XLSX/DOCX/CSV writers, `SheetSession`, `WriteSession` and their file formats |
| WPF | `Em.Ui.Wpf.Controls` / `EmSys.Ui.Wpf.Controls` (`net10.0-windows`) | `MatrixGrid`, `MatrixSheet`, `MatrixWriter`, value editors, menus, print preview, themes, `UseDocuments()` |

A server needs only `EmSys.Libs`: `SheetSession` and `WriteSession` are the API for building formatted documents
(styles, tables, headers and footers, fields), so they live in the neutral layer. `Em.Libs` references no UI
framework; `tests/Em.Libs.Tests/DocumentsBoundaryTests.cs` checks it. `Em.Ui.Wpf.Controls` references no commercial
control library; `tests/Em.Ui.Wpf.Controls.Tests/DocumentsBoundaryTests.cs` checks it.

The engine was written as one assembly, so `Em.Libs` grants `InternalsVisibleTo` to `Em.Ui.Wpf.Controls` and to the
two test projects (`src/shared/Em.Libs/Properties/DocumentsAccess.cs`).

### Namespace map

| MatrixGrid | Em folder | Em namespace |
| --- | --- | --- |
| `Core` | `Em.Libs/Controls/Grid` | `Em.Controls.Grid` |
| `Data` (without the sample `GeneratedOrderProvider`) | `Em.Libs/Controls/Grid/Data` | `Em.Controls.Grid.Data` |
| `Query`, `Export`, `Menus` | `Em.Libs/Controls/Grid/...` | `Em.Controls.Grid.Query`, `Em.Controls.Grid.Export`, `Em.Controls.Grid.Menus` |
| `Editors` (settings) | `Em.Libs/Editors` | `Em.Editors` |
| `Editing` | `Em.Libs/Editing` | `Em.Editing` |
| `Documents/Model`, `Layout`, `Fonts`, `Packaging`, `Formats`, `Assets` | `Em.Libs/Documents/...` | `Em.Documents.*` |
| `Documents/PdfEngine` | `Em.Libs/Documents/Pdf` | `Em.Documents.Pdf` |
| `Documents/CellsEngine` (XLSX and CSV writers) | `Em.Libs/Documents/Xlsx` | `Em.Documents.Xlsx` |
| `Documents/WriterEngine` | `Em.Libs/Documents/Docx` | `Em.Documents.Docx` |
| `Sheet/Core` | `Em.Libs/Controls/Sheet/Cells` | `Em.Controls.Sheet.Cells` |
| `Sheet/*` | `Em.Libs/Controls/Sheet/*` | `Em.Controls.Sheet.*` |
| `Write/*` | `Em.Libs/Controls/Write/*` | `Em.Controls.Write.*` (types keep their `Write*` names) |
| `Wpf/Grid`, `Editors`, `Editing`, `Menus`, `Printing`, `Sheet`, `Themes` | `Em.Ui.Wpf.Controls/...` (same folder names) | `Em.Ui.Wpf.Controls` (one namespace) |
| `Wpf/Write` | `Em.Ui.Wpf.Controls/Writer` | `Em.Ui.Wpf.Controls` |

Control names: MatrixGrid's `GridControl` is `MatrixGrid` here and `MatrixWrite` is `MatrixWriter`; `MatrixSheet`
keeps its name. Other type names are unchanged. Resource keys such as `MatrixGrid.Brush.Primary` and `Material.*` are
not renamed.

Namespace history (user decisions of 2026-11-14): the grid model was first `Em.Grid`, which hid the WPF and MAUI
`Grid` type for code inside the `Em` namespace; it became `Em.Controls`, then `Em.Controls.Grid` once the sheet and
write engines moved under `Em.Controls` too (`Em.Controls.Sheet.*`, `Em.Controls.Write.*`), because the grid
`CellAddress` at the root of `Em.Controls` hid the sheet `CellAddress`. The WPF project, first `Em.Ui.Wpf.Controls`
with one namespace per control group, became `Em.Ui.Wpf.Controls` with every WPF type in the single namespace
`Em.Ui.Wpf.Controls`, which it shares with the controls of `Em.Ui.Wpf.Core` (no type names overlap). `Em.Controls`,
`Em.Editors`, `Em.Editing` and `Em.Documents` are root namespaces: code inside the `Em` namespace resolves a simple name
equal to one of them to that namespace before any `using` directive at the top of the file.

## Server: building files {#server}

A spreadsheet with `SheetSession` and `SheetFiles` (namespaces `Em.Controls.Sheet.Commands`, `Em.Controls.Sheet.Cells`,
`Em.Controls.Sheet.IO`):

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

A letter with `WriteSession` and `WriteFiles` (namespaces `Em.Controls.Write.Editing`, `Em.Controls.Write.Model`,
`Em.Controls.Write.IO`):

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

A grid-shaped report from any `IGridDataProvider` with `GridDocument` (namespaces `Em.Controls.Grid`, `Em.Controls.Grid.Data`,
`Em.Controls.Grid.Export`, `Em.Controls.Grid.Query`):

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

Reference `EmSys.Ui.Wpf.Controls` and call `builder.UseDocuments()` in the host setup (see [Theme](#theme)).

### XAML namespaces {#xaml-namespaces}

| URI | Prefix | CLR namespace (assembly `Em.Ui.Wpf.Controls`) |
| --- | --- | --- |
| `https://MatrixCode/wpf/controls` | `emc` | `Em.Ui.Wpf.Controls` |

One URI covers every WPF control of the assembly (`src/shared/Em.Ui.Wpf.Controls/Properties/XmlnsDefinitions.cs`). The
URI is case-sensitive. It replaced the five URIs (`/grid`, `/editors`, `/sheet`, `/writer`, `/printing`) of the first
layout on 2026-11-14.

**Engine types from `Em.Libs` are not in the URI.** The plan also mapped the grid model and editor settings namespaces
with `XmlnsDefinition(..., AssemblyName = "Em.Libs")`. Verified on 2026-11-13: the XAML markup compiler rejects such a
type (MC3074, "The tag 'GridColumn' does not exist in XML namespace") and the runtime XAML reader does too, and
`Em.Libs` cannot declare the attribute itself because it lives in a WPF assembly. Name those types with a
`clr-namespace` prefix:

```xml
xmlns:grid="clr-namespace:Em.Controls.Grid;assembly=Em.Libs"
xmlns:query="clr-namespace:Em.Controls.Grid.Query;assembly=Em.Libs"
xmlns:menus="clr-namespace:Em.Controls.Grid.Menus;assembly=Em.Libs"
xmlns:settings="clr-namespace:Em.Editors;assembly=Em.Libs"
```

`tests/Em.Ui.Wpf.Controls.Tests/XamlNamespaceSample.xaml` is compiled with the URI and checks this.

### MatrixGrid

```xml
<Window xmlns:emc="https://MatrixCode/wpf/controls" xmlns:grid="clr-namespace:Em.Controls.Grid;assembly=Em.Libs">
  <emc:MatrixGrid ItemsSource="{Binding Orders}" AutoGenerateColumns="False" ShowGroupPanel="True">
    <emc:MatrixGrid.Columns>
      <grid:GridColumn Key="Customer" Header="Customer" Kind="Text" Width="180" />
      <grid:GridColumn Key="Units" Header="Units" Kind="Number" />
    </emc:MatrixGrid.Columns>
  </emc:MatrixGrid>
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
<emc:MatrixSheet xmlns:emc="https://MatrixCode/wpf/controls" ToolbarStyle="Ribbon" Source="{Binding WorkbookPath}" />
<emc:MatrixWriter xmlns:emc="https://MatrixCode/wpf/controls" Mode="Viewer" Source="{Binding DocumentPath}" />
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
dependency, so another renderer gets its own project, for example `Em.Ui.Avalonia.Controls`, with the same control
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
- `src/shared/Em.Ui.Wpf.Controls/Em.Ui.Wpf.Controls.csproj`, `README.md`, `DocumentsExtensions.cs`,
  `DocumentsThemeApplier.cs`, `Properties/XmlnsDefinitions.cs`
- `tests/Em.Libs.Tests/DocumentsBoundaryTests.cs`, `tests/Em.Ui.Wpf.Controls.Tests/Em.Ui.Wpf.Controls.Tests.csproj`,
  `DocumentsBoundaryTests.cs`, `DocumentsThemeApplierTests.cs`, `XamlNamespaceSample.xaml(.cs)`
- `THIRD-PARTY-NOTICES.md`

The sample `GeneratedOrderProvider` becomes the test fixture `tests/Em.Libs.Tests/Controls/Grid/Fixtures/`, which the WPF tests
link. Third-party licenses are in `THIRD-PARTY-NOTICES.md` and `licenses/`, packed with both packages.

## Limitations {#limitations}

The controls inherit MatrixGrid's known limitations; see `docs/KnownLimitations.md`, `docs/SheetEngine.md` and
`docs/WriteEngine.md` in the MatrixGrid repository (not copied here). In Em in particular:

- The bundled Noto Sans font (about 570 KB) is embedded in `Em.Libs`, so every consumer of `EmSys.Libs`, including MAUI
  apps, carries it. `Em.Ui.Wpf.Controls` embeds a second copy as a WPF resource for on-screen text.
- No GUI or visual check of the controls inside an Em host has been run yet.
- `Em.Controls` and the other new root namespaces hide same-named types for code inside the `Em` namespace (see
  [Layers](#layers)).
