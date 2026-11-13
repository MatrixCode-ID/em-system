using System.IO;
using System.Windows;
using Em.Documents.Formats;
using Em.Documents.Model;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Documents.Printing;

/// <summary>Asks for an export file and writes documents to it off the UI thread.</summary>
public static class DocumentFileExport
{
    /// <summary>Shows a save dialog for the registered formats.</summary>
    /// <param name="owner">The owner window, or null.</param>
    /// <param name="registry">The available writers.</param>
    /// <param name="title">The suggested file name.</param>
    /// <param name="formatId">A format to offer alone, or null to offer every format.</param>
    /// <returns>The chosen path and format, or null when the dialog was canceled.</returns>
    public static (string Path, string FormatId)? ChooseFile(Window? owner, DocumentFormatRegistry registry,
        string title, string? formatId = null)
    {
        var formats = formatId is null ? registry.Formats : new[] { registry.Get(formatId).Format };
        if (formats.Count == 0)
            throw new InvalidOperationException("Register at least one export format.");
        var dialog = new SaveFileDialog
        {
            Title = "Export document",
            FileName = SafeName(title),
            AddExtension = true,
            OverwritePrompt = true,
            Filter = string.Join("|", formats.Select(format =>
                $"{format.DisplayName} (*{format.Extension})|*{format.Extension}"))
        };
        bool? chosen = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return chosen == true ? (dialog.FileName, formats[dialog.FilterIndex - 1].Id) : null;
    }

    /// <summary>Writes a document to a temporary file on a background thread and moves it over the target when complete, so a failed or canceled write leaves the target untouched.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="document">The document.</param>
    /// <param name="path">The target path.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is written.</returns>
    public static Task WriteFileAsync(IDocumentWriter writer, Document document, string path,
        CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full) ?? throw new IOException("The export has no destination directory.");
        string temporary = Path.Combine(directory, $".matrixgrid-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous))
                await writer.WriteAsync(document, output, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }, cancellationToken);

    private static string SafeName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var value = new string(title.Where(character => !invalid.Contains(character)).ToArray()).Trim(' ', '.');
        return string.IsNullOrWhiteSpace(value) ? "Grid" : value;
    }
}
