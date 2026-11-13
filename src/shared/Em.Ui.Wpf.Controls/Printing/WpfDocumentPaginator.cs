using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Em.Documents.Layout;
using Em.Documents.Model;

namespace Em.Ui.Wpf.Controls;

/// <summary>Feeds a range of document pages to WPF printing, laying them out one at a time.</summary>
public sealed class WpfDocumentPaginator : DocumentPaginator
{
    private readonly Document _document;
    private readonly Paginator _paginator;
    private readonly int _allPages;
    private readonly int _firstPage;
    private readonly bool _fit;
    private IAsyncEnumerator<Em.Documents.Layout.DocumentPage>? _pages;
    private int _cursor;

    /// <summary>Creates a paginator for a page range.</summary>
    /// <param name="document">The document.</param>
    /// <param name="allPages">The total page count printed in footers.</param>
    /// <param name="firstPage">The one-based first page to print.</param>
    /// <param name="lastPage">The one-based last page to print.</param>
    /// <param name="printableSize">The printable area of the printer page.</param>
    /// <param name="fit">Whether pages are scaled to the printable area.</param>
    public WpfDocumentPaginator(Document document, int allPages, int firstPage, int lastPage,
        Size printableSize, bool fit = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (allPages < 1 || firstPage < 1 || lastPage < firstPage || lastPage > allPages)
            throw new ArgumentOutOfRangeException(nameof(firstPage), "Select a valid page range.");
        _document = document;
        _paginator = new Paginator();
        _allPages = allPages;
        _firstPage = firstPage;
        _fit = fit;
        PageCount = lastPage - firstPage + 1;
        PageSize = printableSize;
    }

    /// <inheritdoc/>
    public override bool IsPageCountValid => true;
    /// <inheritdoc/>
    public override int PageCount { get; }
    /// <inheritdoc/>
    public override Size PageSize { get; set; }
    /// <inheritdoc/>
    public override IDocumentPaginatorSource? Source => null;

    /// <inheritdoc/>
    public override System.Windows.Documents.DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= PageCount)
            return System.Windows.Documents.DocumentPage.Missing;
        int index = _firstPage + pageNumber;
        var page = Task.Run(() => ReadPage(index)).GetAwaiter().GetResult();
        if (page is null)
            return System.Windows.Documents.DocumentPage.Missing;

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            double scale = PagePresenter.DipsPerPoint;
            if (_fit)
                scale = Math.Min(scale, Math.Min(PageSize.Width / page.Setup.PageWidth, PageSize.Height / page.Setup.PageHeight));
            drawing.PushClip(new RectangleGeometry(new Rect(new Point(), PageSize)));
            drawing.PushTransform(new ScaleTransform(scale, scale));
            drawing.DrawDrawing(PagePresenter.CreateDrawing(page));
            drawing.Pop();
            drawing.Pop();
        }
        return new System.Windows.Documents.DocumentPage(visual, PageSize, new Rect(new Point(), PageSize),
            new Rect(new Point(), PageSize));
    }

    /// <summary>Releases the page enumerator of a print job that ended early.</summary>
    public void Close()
    {
        if (_pages is { } pages)
            Task.Run(async () => await pages.DisposeAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
        _pages = null;
        _cursor = 0;
    }

    private async Task<Em.Documents.Layout.DocumentPage?> ReadPage(int index)
    {
        if (_pages is null || index < _cursor)
        {
            if (_pages is { } prior)
                await prior.DisposeAsync().ConfigureAwait(false);
            _pages = _paginator.PagesAsync(_document, _allPages).GetAsyncEnumerator();
            _cursor = 0;
        }
        while (_cursor < index)
        {
            if (!await _pages.MoveNextAsync().ConfigureAwait(false))
                return null;
            _cursor++;
        }
        return _pages.Current;
    }
}
