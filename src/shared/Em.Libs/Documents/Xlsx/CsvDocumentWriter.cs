// Ported the CSV quoting contract from GridCraft (crates/xlsx/src/csv.rs
// @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0.
// Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Buffers;
using System.IO;
using System.Text;
using Em.Documents.Formats;
using Em.Documents.Model;

namespace Em.Documents.Xlsx;

/// <summary>Writes the tables of a document as comma-separated values.</summary>
public sealed class CsvDocumentWriter : IDocumentWriter
{
    private readonly char _delimiter;
    private readonly Encoding _encoding;

    /// <summary>Creates a CSV writer.</summary>
    /// <param name="delimiter">The field delimiter; it cannot be a quote, line break or null character.</param>
    /// <param name="encoding">The text encoding, or null for UTF-8 without a byte order mark.</param>
    public CsvDocumentWriter(char delimiter = ',', Encoding? encoding = null)
    {
        if (delimiter is '\r' or '\n' or '"' or '\0')
            throw new ArgumentException("The delimiter cannot be a quote, line break or null character.", nameof(delimiter));
        _delimiter = delimiter;
        _encoding = encoding ?? new UTF8Encoding(false);
    }

    /// <inheritdoc/>
    public DocumentFormat Format { get; } = new("csv", "CSV", ".csv", "text/csv", FormatCapabilities.None);

    /// <inheritdoc/>
    public async ValueTask WriteAsync(Document document, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var special = SearchValues.Create([_delimiter, '"', '\r', '\n']);
        await using var writer = new StreamWriter(destination, _encoding, 64 * 1024, leaveOpen: true) { NewLine = "\r\n" };
        foreach (var section in document.Sections)
            foreach (var block in section.Blocks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (block is DocumentParagraph paragraph)
                {
                    WriteField(paragraph.Text);
                    writer.WriteLine();
                }
                else if (block is DocumentTable table)
                {
                    for (int index = 0; index < table.Columns.Length; index++)
                    {
                        if (index > 0)
                            writer.Write(_delimiter);
                        WriteField(table.Columns[index].Title);
                    }
                    writer.WriteLine();
                    int rows = 0;
                    await foreach (var row in RowPrefetch.ReadAsync(table.OpenRows(cancellationToken), cancellationToken)
                        .ConfigureAwait(false))
                    {
                        // Rows are written synchronously into the buffered writer; flush asynchronously in batches.
                        for (int index = 0; index < table.Columns.Length; index++)
                        {
                            if (index > 0)
                                writer.Write(_delimiter);
                            if (index < row.Cells.Length)
                                WriteField(row.Cells[index].Format(document.Culture, table.Columns[index].DisplayFormat));
                        }
                        writer.WriteLine();
                        if ((++rows & 4095) == 0)
                            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        void WriteField(string value)
        {
            if (value.AsSpan().IndexOfAny(special) < 0)
            {
                writer.Write(value);
                return;
            }
            writer.Write('"');
            writer.Write(value.Replace("\"", "\"\"", StringComparison.Ordinal));
            writer.Write('"');
        }
    }
}
