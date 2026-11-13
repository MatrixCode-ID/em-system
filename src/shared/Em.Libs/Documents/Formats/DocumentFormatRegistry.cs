using System.IO;
using Em.Documents.Model;

namespace Em.Documents.Formats;

/// <summary>What a document format can represent.</summary>
[Flags]
public enum FormatCapabilities
{
    /// <summary>Plain rows only.</summary>
    None = 0,
    /// <summary>The format has pages, headers and footers.</summary>
    Paged = 1,
    /// <summary>Cells keep their typed values and number formats.</summary>
    TypedCells = 2,
    /// <summary>Group rows can form collapsible outline levels.</summary>
    Outline = 4
}

/// <summary>Describes a document file format.</summary>
/// <param name="Id">The format identifier, such as <c>pdf</c>, compared without case.</param>
/// <param name="DisplayName">The name shown in save dialogs.</param>
/// <param name="Extension">The file extension, including the leading dot.</param>
/// <param name="MediaType">The MIME type.</param>
/// <param name="Capabilities">What the format can represent.</param>
public sealed record DocumentFormat(string Id, string DisplayName, string Extension,
    string MediaType, FormatCapabilities Capabilities);

/// <summary>Writes a <see cref="Document"/> in one file format.</summary>
public interface IDocumentWriter
{
    /// <summary>Gets the format this writer produces.</summary>
    DocumentFormat Format { get; }

    /// <summary>Writes the document to a stream.</summary>
    /// <param name="document">The document to write.</param>
    /// <param name="destination">The writable stream; it is not closed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the document is written.</returns>
    ValueTask WriteAsync(Document document, Stream destination, CancellationToken cancellationToken = default);
}

/// <summary>A set of document writers keyed by format identifier.</summary>
public sealed class DocumentFormatRegistry
{
    private readonly Dictionary<string, IDocumentWriter> _writers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the registered formats.</summary>
    public IReadOnlyList<DocumentFormat> Formats => _writers.Values.Select(writer => writer.Format).ToArray();

    /// <summary>Adds a writer; its format identifier must not be registered yet.</summary>
    /// <param name="writer">The writer to add.</param>
    public void Register(IDocumentWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (!_writers.TryAdd(writer.Format.Id, writer))
            throw new ArgumentException($"The format '{writer.Format.Id}' is already registered.", nameof(writer));
    }

    /// <summary>Returns the writer of a format; throws <see cref="NotSupportedException"/> when the format is not registered.</summary>
    /// <param name="id">The format identifier, compared without case.</param>
    /// <returns>The writer.</returns>
    public IDocumentWriter Get(string id) => _writers.TryGetValue(id, out var writer)
        ? writer : throw new NotSupportedException($"The format '{id}' is not registered.");
}
