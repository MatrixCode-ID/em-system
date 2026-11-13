using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Em.Controls.Grid;
using Em.Controls.Grid.Data;
using Em.Documents.Xlsx;
using Em.Documents.Fonts;
using Em.Documents.Formats;
using Em.Documents.Model;
using Em.Documents.Pdf;
using Em.Documents.Docx;
using Em.Editors;
using Em.Controls.Grid.Query;

namespace Em.Controls.Grid.Export;

/// <summary>How cell values are written to an export.</summary>
public enum ExportValueMode
{
    /// <summary>Typed values carrying the column display and spreadsheet formats.</summary>
    FormattedTyped,
    /// <summary>Typed values without any column format.</summary>
    Raw,
    /// <summary>The formatted display text of each cell.</summary>
    DisplayText
}

/// <summary>Options for exporting or printing a grid view.</summary>
public sealed record GridExportOptions
{
    /// <summary>Gets the document title written to headers and file metadata.</summary>
    public string Title { get; init; } = "Grid";
    /// <summary>Gets the page size, orientation and margins.</summary>
    public PageSetup Page { get; init; } = new();
    /// <summary>Gets whether columns are scaled down to fit the printable width. The default is true.</summary>
    public bool FitToWidth { get; init; } = true;
    /// <summary>Gets whether only the selected rows are exported.</summary>
    public bool SelectedOnly { get; init; }
    /// <summary>Gets whether group header and footer rows are exported. The default is true.</summary>
    public bool IncludeGroups { get; init; } = true;
    /// <summary>Gets whether total and group summaries are exported. The default is true.</summary>
    public bool IncludeSummaries { get; init; } = true;
    /// <summary>Gets how cell values are written.</summary>
    public ExportValueMode Values { get; init; }
    /// <summary>Gets the culture used to format values. The default is the current culture.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;
}

/// <summary>The view state captured when an export starts, so the export does not change if the grid changes meanwhile.</summary>
/// <param name="Columns">The grid columns in display order; hidden columns are skipped.</param>
/// <param name="Query">The query to export.</param>
/// <param name="SelectedRows">The keys of the selected rows.</param>
/// <param name="DataCount">The number of matching data rows, when known, for progress reporting.</param>
public sealed record GridExportSnapshot(ImmutableArray<GridColumn> Columns, GridQuery Query,
    ImmutableHashSet<RowKey> SelectedRows, int? DataCount = null);

/// <summary>Export progress.</summary>
/// <param name="RowsWritten">The number of rows written so far.</param>
/// <param name="TotalRows">The total number of rows, or null when unknown.</param>
public readonly record struct ExportProgress(long RowsWritten, long? TotalRows);

/// <summary>Builds renderer-neutral documents from grid views and creates the standard export writers.</summary>
public static class GridDocument
{
    /// <summary>Creates a registry with the PDF, XLSX, DOCX and CSV writers.</summary>
    /// <param name="csvDelimiter">The CSV field delimiter.</param>
    /// <param name="csvEncoding">The CSV text encoding, or null for UTF-8.</param>
    /// <param name="pdfFont">The font embedded in PDF files, or null for the bundled font.</param>
    /// <returns>The writer registry.</returns>
    public static DocumentFormatRegistry CreateFormats(char csvDelimiter = ',', System.Text.Encoding? csvEncoding = null,
        TrueTypeFace? pdfFont = null)
    {
        var registry = new DocumentFormatRegistry();
        registry.Register(new PdfDocumentWriter(pdfFont));
        registry.Register(new XlsxDocumentWriter());
        registry.Register(new DocxDocumentWriter());
        registry.Register(new CsvDocumentWriter(csvDelimiter, csvEncoding));
        return registry;
    }

    /// <summary>Creates a document that streams the rows of a captured grid view.</summary>
    /// <param name="provider">The data provider to read rows from.</param>
    /// <param name="snapshot">The captured view state.</param>
    /// <param name="options">The export options, or null for the defaults.</param>
    /// <param name="progress">Receives progress while rows are read.</param>
    /// <returns>The document to pass to a writer.</returns>
    public static Document Create(IGridDataProvider provider, GridExportSnapshot snapshot, GridExportOptions? options = null,
        IProgress<ExportProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(snapshot);
        var settings = options ?? new GridExportOptions();
        var columns = snapshot.Columns.Where(column => column.IsVisible).ToImmutableArray();
        if (columns.Length == 0)
            throw new InvalidOperationException("The view has no visible columns to export.");
        settings.Page.Validate();

        var headings = columns.Select(column => new DocumentColumn(column.Header, column.Width * 0.75,
            column.EffectiveAlignment switch
            {
                GridColumnAlignment.Right => HorizontalAlignment.Right,
                GridColumnAlignment.Center => HorizontalAlignment.Center,
                _ => HorizontalAlignment.Left
            },
            settings.Values == ExportValueMode.Raw ? null : column.DisplayFormat,
            settings.Values == ExportValueMode.Raw ? null : SpreadsheetFormat(column))).ToImmutableArray();
        var table = new DocumentTable(headings, token => Rows(provider, snapshot, columns, settings, progress, 0, token),
            SheetName: settings.Title)
        {
            OpenRowsFrom = MapsDisplayRows(snapshot.Query, settings)
                ? (first, token) => Rows(provider, snapshot, columns, settings, null, first, token) : null
        };
        var details = snapshot.Query.FilterEnabled && snapshot.Query.Filter is { } filter ? FilterTree.Describe(filter) : null;
        return new Document(settings.Title, [new DocumentSection(settings.Page,
            details is null ? [table] : [new DocumentParagraph(details), table])])
        {
            FitToWidth = settings.FitToWidth,
            Culture = settings.Culture
        };
    }

    /// <summary>
    /// True when every display row of the provider becomes exactly one document row, so a document row position is
    /// also a provider row offset (the total summary row follows the last display row).
    /// </summary>
    private static bool MapsDisplayRows(GridQuery query, GridExportOptions options) => !options.SelectedOnly
        && (query.Groups.IsDefaultOrEmpty || options.IncludeGroups && options.IncludeSummaries);

    private const int MapBatchSize = 128;
    // Leaves cores for the row source, the prefetch consumer and the writer.
    private static readonly int MapWorkers = Math.Clamp(Environment.ProcessorCount / 4, 1, 4);

    /// <summary>
    /// Reads the display rows in batches and maps each batch's data rows to document rows in parallel while the next
    /// batch is read. Group rows, footers, selected-row handling, progress and the total summary are then applied in
    /// source order, so the output is the same as mapping one row at a time.
    /// </summary>
    private static async IAsyncEnumerable<DocumentRow> Rows(IGridDataProvider provider, GridExportSnapshot snapshot,
        ImmutableArray<GridColumn> columns, GridExportOptions options, IProgress<ExportProgress>? progress, long start,
        [EnumeratorCancellation] CancellationToken token)
    {
        long written = 0;
        // Each formatter memo is a plain dictionary; a worker takes a whole set from the pool, so the memos stay warm
        // across batches without being shared between threads.
        var formatterPool = new ConcurrentBag<CellFormatter[]>();
        var openGroups = new List<DocumentRow>();
        var emitted = new HashSet<int>();
        var totalItems = snapshot.Query.Summaries.Where(summary => summary.Scope == SummaryScope.Total).ToArray();
        var query = options.IncludeSummaries && !options.SelectedOnly
            ? snapshot.Query : snapshot.Query with { Summaries = [] };
        int outline = options.IncludeGroups ? query.Groups.Length : 0;
        long? expected = options.SelectedOnly ? snapshot.SelectedRows.Count : snapshot.DataCount;

        // Each column resolves its ordinal once per row schema instead of looking up its key in every row.
        var slots = columns.Select(column => new GridColumnSlot(column.Key)).ToArray();

        DocumentRow?[] Map(GridRow[] rows, CancellationToken cancellation)
        {
            var mapped = new DocumentRow?[rows.Length];
            Parallel.For(0, rows.Length, new ParallelOptions { CancellationToken = cancellation, MaxDegreeOfParallelism = MapWorkers },
                () => formatterPool.TryTake(out var set) ? set
                    : columns.Select(column => new CellFormatter(column, options.Culture)).ToArray(),
                (index, _, formatters) =>
                {
                    var row = rows[index];
                    // Group rows and footers are built in order later; unselected rows are skipped without mapping.
                    if (row.Group is not null || options.SelectedOnly && !snapshot.SelectedRows.Contains(row.Key))
                        return formatters;
                    var cells = new DocumentCell[columns.Length];
                    for (int column = 0; column < cells.Length; column++)
                        cells[column] = Cell(slots[column].Get(row.Values), columns[column],
                            formatters[column], options);
                    mapped[index] = new DocumentRow(ImmutableCollectionsMarshal.AsImmutableArray(cells), OutlineLevel: outline);
                    return formatters;
                },
                formatterPool.Add);
            return mapped;
        }

        IEnumerable<DocumentRow> Emit(GridRow[] rows, DocumentRow?[] mapped)
        {
            for (int index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                if (row.Group is { } group)
                {
                    if (group.IsFooter)
                    {
                        if (options.IncludeGroups && options.IncludeSummaries && !options.SelectedOnly)
                            yield return SummaryRow(row.Summaries, columns, query, SummaryScope.Group, group.Level, options.Culture);
                        continue;
                    }
                    if (!options.IncludeGroups)
                        continue;
                    var label = $"{group.ColumnKey}: {group.Value.Format(options.Culture)} ({group.Count:N0})";
                    var item = new DocumentRow([DocumentCell.Text(label)], DocumentRowKind.Group, group.Level);
                    if (!options.SelectedOnly)
                        yield return item;
                    else
                    {
                        while (openGroups.Count > group.Level)
                            openGroups.RemoveAt(openGroups.Count - 1);
                        emitted.RemoveWhere(level => level >= group.Level);
                        openGroups.Add(item);
                    }
                    continue;
                }

                if (mapped[index] is not { } data)
                    continue;
                if (options.IncludeGroups && options.SelectedOnly)
                    for (int level = 0; level < openGroups.Count; level++)
                        if (emitted.Add(level))
                            yield return openGroups[level];
                yield return data;
                written++;
                if ((written & 1023) == 0)
                    progress?.Report(new ExportProgress(written, expected));
            }
        }

        var source = start <= 0 ? GridRowStreaming.ReadAsync(provider, query, token)
            : GridRowStreaming.ReadPagesAsync(provider, query, (int)Math.Min(start, int.MaxValue), token);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<DocumentRow?[]>? pending = null;
        GridRow[]? pendingRows = null;
        var batch = new List<GridRow>(MapBatchSize);
        try
        {
            await foreach (var row in source.ConfigureAwait(false))
            {
                batch.Add(row);
                if (batch.Count < MapBatchSize)
                    continue;
                // Start mapping this batch before emitting the previous one, so mapping overlaps with reading.
                var rows = batch.ToArray();
                batch.Clear();
                var started = Task.Run(() => Map(rows, stop.Token), stop.Token);
                if (pending is not null)
                    foreach (var item in Emit(pendingRows!, await pending.ConfigureAwait(false)))
                        yield return item;
                (pending, pendingRows) = (started, rows);
            }
            if (pending is not null)
            {
                var finished = await pending.ConfigureAwait(false);
                pending = null;
                foreach (var item in Emit(pendingRows!, finished))
                    yield return item;
            }
            if (batch.Count > 0)
            {
                var rows = batch.ToArray();
                foreach (var item in Emit(rows, await Task.Run(() => Map(rows, stop.Token), stop.Token).ConfigureAwait(false)))
                    yield return item;
            }
        }
        finally
        {
            // A consumer that stops early must not leave a mapping task running against the snapshot.
            stop.Cancel();
            if (pending is not null)
                try { await pending.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
        }

        if (options.IncludeSummaries && !options.SelectedOnly && totalItems.Length > 0)
        {
            var totals = await provider.FetchAsync(query, new RowRange(0, 0), token).ConfigureAwait(false);
            yield return SummaryRow(totals.Totals, columns, query, SummaryScope.Total, 0, options.Culture);
        }
        progress?.Report(new ExportProgress(written, expected));
    }

    private static DocumentRow SummaryRow(IReadOnlyDictionary<string, GridValue> values, ImmutableArray<GridColumn> columns,
        GridQuery query, SummaryScope scope, int level, CultureInfo culture)
    {
        var cells = columns.Select(column => DocumentCell.Text(string.Join(" · ", query.Summaries
            .Where(summary => summary.Scope == scope && summary.ColumnKey == column.Key && values.ContainsKey(summary.Key))
            .Select(summary => $"{summary.Operation} = {values[summary.Key].Format(culture,
                summary.Operation == SummaryOperation.Count ? null : column.DisplayFormat)}")))).ToImmutableArray();
        return new DocumentRow(cells, DocumentRowKind.Summary, level);
    }

    private static DocumentCell Cell(GridValue value, GridColumn column, CellFormatter formatter, GridExportOptions options)
    {
        bool? check = value.Kind == GridValueKind.Boolean && (column.Editor is null or CheckSettings)
            && options.Values != ExportValueMode.Raw ? value.Boolean : null;
        if (options.Values == ExportValueMode.DisplayText)
            return DocumentCell.Text(formatter.Format(value)) with { CheckMark = check };
        string? display = options.Values == ExportValueMode.FormattedTyped ? formatter.Format(value) : null;
        return value.Kind switch
        {
            GridValueKind.Text => DocumentCell.Text(display ?? value.Text),
            GridValueKind.Number => DocumentCell.Number(value.Number!.Value, display),
            GridValueKind.Date => DocumentCell.Date(value.Date!.Value, display),
            GridValueKind.Boolean => DocumentCell.Boolean(value.Boolean!.Value, display) with { CheckMark = check },
            _ => display is { Length: > 0 } ? DocumentCell.Text(display) : default
        };
    }

    /// <summary>
    /// Formats one column's cells with a bounded memo, so repeated values (codes, dates, quantities) are formatted
    /// once per export. Numbers are keyed with their scale because 1.0 and 1.00 format differently.
    /// </summary>
    private sealed class CellFormatter(GridColumn column, CultureInfo culture)
    {
        private const int Capacity = 4096;
        private readonly Dictionary<(GridValue Value, int Scale), string> _cache = new();

        public string Format(GridValue value)
        {
            if (value.Kind is GridValueKind.Null or GridValueKind.Text && column.Editor is null)
                return value.Kind == GridValueKind.Text ? value.Text! : column.Format(value, culture);
            var key = (value, value.Number is { } number ? number.Scale : 0);
            if (_cache.TryGetValue(key, out var text))
                return text;
            text = column.Format(value, culture);
            if (_cache.Count < Capacity)
                _cache[key] = text;
            return text;
        }
    }

    private static string? SpreadsheetFormat(GridColumn column)
    {
        if (column.SpreadsheetFormat is { } format)
            return format;
        if (column.Kind == GridValueKind.Date)
            return "dd mmm yyyy";
        if (column.Kind != GridValueKind.Number || column.DisplayFormat is not { Length: 2 } display ||
            !char.IsAsciiDigit(display[1]))
            return null;
        int decimals = display[1] - '0';
        string suffix = decimals == 0 ? "" : "." + new string('0', decimals);
        return char.ToUpperInvariant(display[0]) switch
        {
            'N' => "#,##0" + suffix, 'F' => "0" + suffix, 'P' => "0" + suffix + "%", _ => null
        };
    }
}
