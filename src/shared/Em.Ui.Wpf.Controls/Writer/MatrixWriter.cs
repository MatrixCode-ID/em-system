using System.Collections.Immutable;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Em.Editing;
using Em.Ui.Wpf.Controls;
using Em.Controls.Write.Editing;
using Em.Controls.Write.IO;
using Em.Controls.Write.Layout;
using Em.Controls.Write.Model;

namespace Em.Ui.Wpf.Controls;

public sealed partial class MatrixWriter : EditorControl
{
    private WriteSession? session;
    private ScrollViewer? scroll;
    private WriteSurface? surface;
    private WriteRuler? ruler;
    private WriteGuide? guide;
    private TextBlock? status;
    private DockPanel? editor;
    private TextBox? query;
    private FrameworkElement? findReplacement;
    private FrameworkElement? replaceAction;
    private CancellationTokenSource? layoutRequest;
    private ImmutableArray<WritePage> pages = [];
    private WriteDocument? laidOut;
    private WriteDocument? partialDocument;
    private DispatcherTimer? caretTimer;
    private bool caretVisible = true;
    private bool showRuler = true;
    private bool disposed;
    private string? composition;
    private double zoom = 1;
    private double pendingOffset;
    private int page = 1;
    private WriteClipboard? copied;

    /// <summary>Gets the editing session of the displayed document; a new empty document is created on first use.</summary>
    public WriteSession Session => session ??= CreateSession(WriteDocument.Create());
    /// <summary>Gets the zoom factor, where 1 is 100 percent.</summary>
    public double Zoom => zoom;
    /// <summary>Gets the one-based number of the page at the top of the view.</summary>
    public int CurrentPage => page;
    /// <summary>Gets the number of pages laid out so far.</summary>
    public int PageCount => pages.Length;
    /// <summary>Gets the vertical scroll offset in device-independent pixels.</summary>
    public double VerticalOffset => IsLoaded ? scroll?.VerticalOffset ?? pendingOffset : pendingOffset;
    /// <summary>Gets the text of an input method composition that is not committed yet, or null.</summary>
    public string? CompositionText => composition;
    /// <summary>Gets the number of completed layout passes, for diagnostics and tests.</summary>
    public int LayoutCount { get; private set; }

    internal ImmutableArray<WritePage> Pages => pages;
    internal bool CaretVisible => caretVisible;
    /// <inheritdoc/>
    protected override IReadOnlyList<EditorCommand> Catalog => WriteCommands.Catalog;
    /// <inheritdoc/>
    protected override string? ContextualToolbarTab => Mode != EditorMode.Full || session is null ? null :
        session.Selection.Caret.Story != WriteStory.Body ? WriteCommands.HeaderFooterTab :
        session.Selection.Caret.Row >= 0 ? "Table Tools" : null;

    /// <summary>Shows a document in a new editing session; the previous undo history is discarded.</summary>
    /// <param name="document">The document.</param>
    public void LoadDocument(WriteDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Detach();
        session = CreateSession(document);
        composition = null;
        pendingOffset = 0;
        page = 1;
        pages = [];
        laidOut = null;
        partialDocument = null;
        StartLayout();
        Refresh();
    }

    /// <summary>Scrolls the view to a vertical offset.</summary>
    /// <param name="offset">The offset in device-independent pixels.</param>
    public void ScrollTo(double offset)
    {
        if (!double.IsFinite(offset))
            return;
        pendingOffset = Math.Max(0, offset);
        scroll?.ScrollToVerticalOffset(pendingOffset);
    }

    /// <summary>Shows uncommitted input method text at the caret.</summary>
    /// <param name="text">The composition text.</param>
    /// <returns>False in viewer mode.</returns>
    public bool BeginComposition(string text)
    {
        if (Mode != EditorMode.Full)
            return false;
        composition = text;
        surface?.InvalidateVisual();
        return true;
    }

    /// <summary>Inserts the pending composition text into the document.</summary>
    /// <returns>True when text was inserted.</returns>
    public bool CommitComposition()
    {
        if (composition is not { } text)
            return false;
        composition = null;
        return Session.InsertText(text).Accepted;
    }

    /// <inheritdoc/>
    protected override void ApplyMode(EditorMode mode)
    {
        if (mode == EditorMode.Viewer && composition is not null)
            CommitComposition();
        if (session is not null)
            session.Mode = mode;
        if (findReplacement is not null)
            findReplacement.Visibility = mode == EditorMode.Full ? Visibility.Visible : Visibility.Collapsed;
        if (replaceAction is not null)
            replaceAction.Visibility = mode == EditorMode.Full ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    /// <inheritdoc/>
    protected override (object Document, IReadOnlyList<string> Warnings) ReadFile(string path, Stream stream, CancellationToken cancellation)
    {
        var opened = WriteFiles.OpenDocx(stream, cancellation);
        var first = new WritePaginator(WpfFontProvider.Instance).Pages(opened.Document, cancellation: cancellation).FirstOrDefault();
        return ((opened.Document, first), opened.Warnings);
    }

    /// <inheritdoc/>
    protected override void InstallDocument(object document)
    {
        var (model, first) = ((WriteDocument, WritePage?))document;
        InstallOffscreenPage(model, first);
    }

    internal void InstallOffscreenPage(WriteDocument model, WritePage? first)
    {
        LoadDocument(model);
        if (first is not null)
        {
            partialDocument = model;
            pages = [first];
            Refresh();
        }
    }

    /// <inheritdoc/>
    protected override UIElement BuildEditor()
    {
        editor = new DockPanel();
        Material.Paint(editor, Panel.BackgroundProperty, "SurfaceContainerHighest");
        ruler = new(this);
        DockPanel.SetDock(ruler, Dock.Top);
        editor.Children.Add(ruler);
        status = new TextBlock { Height = 32, Padding = new Thickness(8, 8, 0, 0), FontSize = 12 };
        Material.Paint(status, TextBlock.BackgroundProperty, "SurfaceContainer");
        DockPanel.SetDock(status, Dock.Bottom);
        editor.Children.Add(status);
        surface = new(this);
        surface.ContextMenuOpening += (_, _) => surface.ContextMenu = CreateCommandMenu("cut", "copy", "paste", "select-all", "paragraph");
        scroll = new ScrollViewer { Content = surface, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false };
        scroll.PreviewMouseWheel += OnWheelZoom;
        scroll.ScrollChanged += (_, _) =>
        {
            pendingOffset = scroll.VerticalOffset;
            page = surface.PageAt(scroll.VerticalOffset);
            surface.InvalidateVisual();
            ruler?.InvalidateVisual();
            RefreshStatus();
            RefreshToolbar();
        };
        guide = new WriteGuide();
        var pane = new System.Windows.Controls.Grid();
        pane.Children.Add(scroll);
        pane.Children.Add(guide);
        editor.Children.Add(pane);
        Loaded += (_, _) => StartLayout();
        IsVisibleChanged += (_, _) => { if (IsVisible) StartLayout(); else Suspend(); };
        Unloaded += (_, _) => Suspend();
        SizeChanged += (_, _) => Refresh();
        caretTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(530) };
        caretTimer.Tick += (_, _) => { caretVisible = !caretVisible; surface.InvalidateVisual(); };
        StartLayout();
        Refresh();
        return editor;
    }

    private WriteSession CreateSession(WriteDocument document)
    {
        var current = new WriteSession(document) { Mode = Mode };
        current.DocumentChanged += SessionChanged;
        current.SelectionChanged += SelectionChanged;
        return current;
    }

    private void Detach()
    {
        if (session is null)
            return;
        session.DocumentChanged -= SessionChanged;
        session.SelectionChanged -= SelectionChanged;
    }

    private void SessionChanged(object? sender, EventArgs args)
    {
        revealCaret = true;
        StartLayout();
        RaiseDocumentChanged();
    }

    private void SelectionChanged(object? sender, EventArgs args)
    {
        caretVisible = true;
        revealCaret = true;
        Refresh();
    }

    private async void StartLayout()
    {
        if (disposed || !IsLoaded || !IsVisible || surface is null)
            return;

        caretTimer?.Start();
        var document = Session.Document;
        if (ReferenceEquals(laidOut, document) || layoutRequest is not null && ReferenceEquals(partialDocument, document))
            return;
        layoutRequest?.Cancel();
        var request = new CancellationTokenSource();
        var token = request.Token;
        layoutRequest = request;
        partialDocument = document;
        LayoutCount++;
        try
        {
            var completed = await Task.Run(async () =>
            {
                var builder = ImmutableArray.CreateBuilder<WritePage>();
                var paginator = new WritePaginator(WpfFontProvider.Instance);
                foreach (var item in paginator.Pages(document, cancellation: token))
                {
                    token.ThrowIfCancellationRequested();
                    builder.Add(item);
                    if (builder.Count == 1)
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (ReferenceEquals(layoutRequest, request) && !token.IsCancellationRequested)
                            {
                                pages = [item];
                                Refresh();
                            }
                        }, DispatcherPriority.Background, token);
                }
                if (document.Sections.Any(section => section.Stories.Values.Any(story => story.OfType<WriteParagraph>()
                    .Any(paragraph => paragraph.Runs.Any(run => run.Field == WriteField.PageCount)))))
                    return paginator.Pages(document, builder.Count, token).ToImmutableArray();
                return builder.ToImmutable();
            }, token);
            if (!request.IsCancellationRequested && ReferenceEquals(layoutRequest, request))
            {
                pages = completed;
                laidOut = document;
                Refresh();
                scroll?.ScrollToVerticalOffset(pendingOffset);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        {
            if (!request.IsCancellationRequested && status is not null)
                status.Text = error.Message;
        }
        finally
        {
            if (ReferenceEquals(layoutRequest, request))
                layoutRequest = null;
            request.Dispose();
        }
    }

    private void Suspend()
    {
        if (zoomTarget is { } pending)
            ZoomAround(pending, zoomAnchor);
        zoomTarget = null;
        StopAnimation();
        caretTimer?.Stop();
        layoutRequest?.Cancel();
        layoutRequest = null;
    }

    private void Refresh()
    {
        if (ruler is not null)
        {
            ruler.Visibility = showRuler && Mode == EditorMode.Full && (ActualWidth == 0 || ActualWidth >= 400) ? Visibility.Visible : Visibility.Collapsed;
            ruler.InvalidateVisual();
        }
        surface?.InvalidateMeasure();
        surface?.InvalidateVisual();
        RefreshStatus();
        RefreshToolbar();
        RevealCaret();
    }

    private void RefreshStatus()
    {
        if (status is not null)
            status.Text = $"Page {page} of {Math.Max(1, PageCount)}    {zoom:P0}" +
                (Session.Selection.Caret.Story == WriteStory.Body ? "" : $"    {Session.Selection.Caret.Story}");
    }

    /// <summary>Shows the guide line down the pages at <paramref name="rulerX"/> on the ruler, or hides it with null.</summary>
    internal void ShowRulerGuide(double? rulerX)
    {
        if (guide is null || ruler is null)
            return;
        guide.Show(rulerX is { } x ? ruler.TranslatePoint(new Point(x, 0), guide).X : null);
    }

    internal void Navigate(int target)
    {
        target = Math.Clamp(target, 1, Math.Max(1, pages.Length));
        if (surface is not null)
            ScrollTo(surface.PageTop(target));
        page = target;
        RefreshStatus();
        RefreshToolbar();
    }

    /// <summary>Stops layout work and releases the session events.</summary>
    public override void Dispose()
    {
        disposed = true;
        Detach();
        Suspend();
        base.Dispose();
    }
}
