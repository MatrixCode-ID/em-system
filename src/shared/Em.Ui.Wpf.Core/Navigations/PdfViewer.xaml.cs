using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Shared.Pdf;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   /// <summary>
   /// The application's built-in PDF viewer, opened through <see cref="EmApp.ViewPdf"/> or
   /// <see cref="NavigationEntry.ViewPdf"/> with a <see cref="PdfViewerNavigationPayload"/>. It shows,
   /// searches, prints, and saves any PDF its caller provides; it is not guarded by a claim, because the
   /// rights to its content are checked where that content is fetched.
   /// </summary>
   public partial class PdfViewer : UserControl, INavigationBody
   {
      /// <summary>
      /// Creates a PDF viewer for application <paramref name="app"/>.
      /// </summary>
      public PdfViewer(EmApp app) {
         InitializeComponent();
         Vm.EmApp = app;
         Vm.ScrollRequested += offset => PageScroller.ScrollToVerticalOffset(offset);
         Loaded += (_, _) => Vm.DeviceScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
      }

      /// <summary>The view model of this screen.</summary>
      public PdfViewerVm Vm => (PdfViewerVm)DataContext;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) {
         if (args.Data is not PdfViewerNavigationPayload payload) {
            args.Cancel = true;
            args.Message = "The PDF viewer can only be opened with a PDF viewer payload.";
            return Task.CompletedTask;
         }

         Vm.Payload = payload;
         return Task.CompletedTask;
      }

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.LoadAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) {
         Vm.Release();
         return Task.CompletedTask;
      }

      /// <inheritdoc />
      protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) {
         base.OnDpiChanged(oldDpi, newDpi);
         Vm.DeviceScale = newDpi.DpiScaleX;
      }

      // The viewport is not a bindable property of a ScrollViewer, and it is what decides which pages get a
      // bitmap, so the view model is told about every change of it here.
      private void PageScroller_ScrollChanged(object sender, ScrollChangedEventArgs e) =>
         Vm.UpdateViewport(e.VerticalOffset, e.ViewportHeight, e.ViewportWidth);

      // Ctrl + wheel zooms, like every other document viewer. A wheel turn has no direction a MouseBinding
      // can tell apart, so it is forwarded by hand.
      private void PageScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
         if (Keyboard.Modifiers != ModifierKeys.Control) return;
         Vm.ZoomStep(e.Delta > 0);
         e.Handled = true;
      }
   }

   /// <summary>
   /// View model for <see cref="PdfViewer"/>.
   /// </summary>
   public class PdfViewerVm : MvvmModelBase
   {
      private const double PointToDip = 96.0 / 72.0;
      private const double PageGap = 16;
      private const double SidePadding = 24;
      private const double MinZoom = 0.1;
      private const double MaxZoom = 6;

      private static readonly double[] ZoomSteps =
         [0.25, 0.33, 0.5, 0.67, 0.75, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5, 6];

      private readonly CancellationTokenSource _lifetime = new();
      private CancellationTokenSource? _loadCancel;
      private CancellationTokenSource? _searchCancel;
      private PdfiumDocument? _document;
      private byte[]? _data;
      private int _generation;
      private bool _fitOnLoad = true;

      private double _viewportOffset;
      private double _viewportHeight;
      private double _viewportWidth;

      private IReadOnlyList<PdfSearchHit>? _hits;
      private string? _hitsQuery;
      private int _hitIndex = -1;

      /// <summary>
      /// Creates a new view model and registers all commands of the screen.
      /// </summary>
      public PdfViewerVm() {
         RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
         RegisterCommand(nameof(PreviousPageCommand), PreviousPageCommand, PreviousPageCommandAllowed);
         RegisterCommand(nameof(NextPageCommand), NextPageCommand, NextPageCommandAllowed);
         RegisterCommand(nameof(GoToPageCommand), GoToPageCommand, GoToPageCommandAllowed);
         RegisterCommand(nameof(ZoomOutCommand), ZoomOutCommand, ZoomOutCommandAllowed);
         RegisterCommand(nameof(ZoomInCommand), ZoomInCommand, ZoomInCommandAllowed);
         RegisterCommand(nameof(FitWidthCommand), FitWidthCommand, FitWidthCommandAllowed);
         RegisterCommand(nameof(FindNextCommand), FindNextCommand, FindNextCommandAllowed);
         RegisterCommand(nameof(FindPreviousCommand), FindPreviousCommand, FindPreviousCommandAllowed);
         RegisterCommand(nameof(PrintCommand), PrintCommand, PrintCommandAllowed);
         RegisterCommand(nameof(SaveAsCommand), SaveAsCommand, SaveAsCommandAllowed);
      }

      /// <summary>
      /// Requested by the view model when the display must scroll to a given position (in DIPs from the top
      /// of the page list) - changing page, a search result, or keeping the position when the zoom changes.
      /// </summary>
      public event Action<double>? ScrollRequested;

      /// <summary>The parameter that opened this screen: the title, the PDF fetcher, and its default file name.</summary>
      public PdfViewerNavigationPayload? Payload { get; internal set; }
      /// <summary>Indicates allow save as.</summary>
      public bool AllowSaveAs { get => Get(true); set => Set(value, _ => RaiseCommandsChanged()); }
      /// <summary>Indicates the error details is shown.</summary>
      public bool ShowErrorDetails { get; set; } = true;

      #region Pages

      /// <summary>All pages of the document that is open, in their order.</summary>
      public ObservableCollection<PdfPageVm> Pages { get; } = [];

      /// <summary>Distance of the page list from the edge of the card.</summary>
      public Thickness PagesMargin { get; } = new(SidePadding, PageGap, SidePadding, 0);

      /// <summary>Distance below each page.</summary>
      public Thickness PageMargin { get; } = new(0, 0, 0, PageGap);

      /// <summary>
      /// Screen scale where the viewer is shown (1 = 96 DPI). Used so pages are rendered as sharp as the
      /// real screen pixels, not just their DIP size.
      /// </summary>
      public double DeviceScale {
         get => Get(1.0);
         set => Set(value, _ => RefreshVisiblePages());
      }

      /// <summary><c>true</c> when a document is open.</summary>
      public bool HasDocument => _document != null;

      /// <summary>Error message of loading the document, shown while no document is open.</summary>
      public string? ErrorMessage {
         get => Get<string?>();
         private set => Set(value, _ => NotifyChanged(nameof(HasError)));
      }

      /// <summary><c>true</c> when the document failed to load and there is no other document to show.</summary>
      public bool HasError => !string.IsNullOrEmpty(ErrorMessage) && !HasDocument;

      /// <summary>Number of the page (starting from 1) that is most visible right now.</summary>
      public int CurrentPage {
         get => Get(0);
         private set => Set(value, r => {
            PageNumberText = r > 0 ? $"{r}" : "";
            RaiseCommandsChanged();
         });
      }

      /// <summary>Content of the page number box; Enter moves the view to that page.</summary>
      public string PageNumberText {
         get => Get("");
         set => Set(value);
      }

      /// <summary>Caption of the page count beside the page number box, e.g. <c>"/ 12"</c>.</summary>
      public string PageCountCaption => $"/ {Pages.Count:N0}";

      #endregion

      #region Zoom

      /// <summary>Zoom of the view: 1 means the original page size.</summary>
      public double Zoom {
         get => Get(1.0);
         private set => Set(value, _ => {
            NotifyChanged(nameof(ZoomCaption));
            RaiseCommandsChanged();
         });
      }

      /// <summary>Zoom in percent, e.g. <c>"100%"</c>.</summary>
      public string ZoomCaption => $"{Math.Round(Zoom * 100):0}%";

      /// <summary>Zooms in or out one step - used by Ctrl + mouse wheel.</summary>
      /// <param name="zoomIn"><c>true</c> to zoom in.</param>
      public void ZoomStep(bool zoomIn) {
         if (!HasDocument) return;
         if (zoomIn) ZoomInCommand();
         else ZoomOutCommand();
      }

      #endregion

      #region Search

      /// <summary>Text searched for in the document.</summary>
      public string SearchText {
         get => Get("");
         set => Set(value, _ => RaiseCommandsChanged());
      }

      /// <summary>Position of the search result currently highlighted, e.g. <c>"3 of 12"</c>.</summary>
      public string SearchCaption {
         get => Get("");
         private set => Set(value);
      }

      #endregion

      #region Viewport

      /// <summary>
      /// Tells the position and size of the page display area. Decides which pages are rendered, which have
      /// their bitmaps discarded, and the number of the page currently visible.
      /// </summary>
      /// <param name="offset">The vertical scroll position, in DIPs.</param>
      /// <param name="height">Height of the display area, in DIPs.</param>
      /// <param name="width">Width of the display area, in DIPs.</param>
      public void UpdateViewport(double offset, double height, double width) {
         _viewportOffset = offset;
         _viewportHeight = height;
         var widthChanged = Math.Abs(_viewportWidth - width) > 0.5;
         _viewportWidth = width;

         if (_fitOnLoad && widthChanged && HasDocument) {
            _fitOnLoad = false;
            ApplyZoom(FitWidthZoom(), keepPosition: false);
            return;
         }

         RefreshVisiblePages();
      }

      // Pages are laid out one below the other with a fixed gap, so where each one starts is known without
      // asking the layout - which is what lets the bitmaps follow the scroll position exactly.
      private void RefreshVisiblePages() {
         if (_document is not { } document || Pages.Count == 0 || _viewportHeight <= 0) return;

         var top = _viewportOffset;
         var bottom = _viewportOffset + _viewportHeight;
         var keepTop = top - 2 * _viewportHeight;
         var keepBottom = bottom + 2 * _viewportHeight;
         var scale = RenderScale;
         var current = 0;

         foreach (var page in Pages) {
            var pageBottom = page.Top + page.Height;
            var visible = pageBottom >= top && page.Top <= bottom;
            // The neighbours of what is on screen are rendered too, so a normal scroll finds them ready.
            var wanted = pageBottom >= top - _viewportHeight && page.Top <= bottom + _viewportHeight;
            var keep = pageBottom >= keepTop && page.Top <= keepBottom;

            if (current == 0 && pageBottom + PageGap > top + _viewportHeight * 0.3) current = page.Index + 1;

            page.IsWanted = wanted;
            if (!keep) page.Drop();
            else if (wanted && page.NeedsRender(scale)) _ = RenderAsync(document, page, scale, _generation, visible);
         }

         CurrentPage = current == 0 ? Pages.Count : current;
      }

      private double RenderScale => Zoom * PointToDip * Math.Max(1, DeviceScale);

      private async Task RenderAsync(PdfiumDocument document, PdfPageVm page, double scale, int generation,
         bool visible) {
         page.PendingScale = scale;
         try {
            // Pages not on screen yet wait a moment, so the ones that are go to PDFium first.
            if (!visible) await Task.Delay(60, _lifetime.Token);

            var bitmap = await Task.Run(() => page.IsWanted ? document.RenderPage(page.Index, scale) : null,
               _lifetime.Token);
            if (bitmap is null || generation != _generation || page.PendingScale != scale) return;

            page.SetBitmap(bitmap, scale);
         }
         catch (OperationCanceledException) {
            // The viewer is closing.
         }
         catch (ObjectDisposedException) {
            // The document was replaced or closed while this page was waiting for its turn.
         }
         catch (Exception) {
            // One page that cannot be drawn leaves a blank sheet; the others still show.
         }
         finally {
            if (page.PendingScale == scale) page.PendingScale = 0;
         }
      }

      #endregion

      #region Loading

      /// <summary>
      /// Fetches the PDF content through <see cref="PdfViewerNavigationPayload.Loader"/> and then shows it.
      /// Called when the viewer opens and by the Refresh button. If it fails, the document that is already
      /// shown (if any) stays shown.
      /// </summary>
      public async Task LoadAsync() {
         if (Payload is not { } payload || _lifetime.IsCancellationRequested) return;

         _loadCancel?.Cancel();
         var cancel = _loadCancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
         InWaiting = true;
         WaiterText = "Loading document";
         RaiseCommandsChanged();
         try {
            byte[] data;
            await using (var stream = await payload.Loader(cancel.Token)) {
               using var buffer = new MemoryStream();
               await stream.CopyToAsync(buffer, cancel.Token);
               data = buffer.ToArray();
            }

            var document = await Task.Run(() => PdfiumDocument.Open(data), cancel.Token);
            if (cancel.IsCancellationRequested) {
               document.Dispose();
               return;
            }

            ShowDocument(document, data);
            ErrorMessage = null;
         }
         catch (OperationCanceledException) when (cancel.IsCancellationRequested) {
            // A newer load, or closing the viewer, took over.
         }
         catch (Exception x) {
            ErrorMessage = x.Message;
            NotifyChanged(nameof(HasError));
            if (ShowErrorDetails) DiaplayException(x);
         }
         finally {
            if (_loadCancel == cancel) {
               InWaiting = false;
               WaiterText = "";
               RaiseCommandsChanged();
            }
         }
      }

      private void ShowDocument(PdfiumDocument document, byte[] data) {
         var previous = _document;
         var anchor = CurrentPage;
         _document = document;
         _data = data;
         _generation++;
         ClearSearch();

         Pages.Clear();
         for (var i = 0; i < document.PageCount; i++) Pages.Add(new PdfPageVm(i, document.GetPageSize(i)));
         NotifyChanged(nameof(PageCountCaption));
         NotifyChanged(nameof(HasDocument));
         NotifyChanged(nameof(HasError));

         if (previous is null && _viewportWidth > 0) {
            _fitOnLoad = false;
            ApplyZoom(FitWidthZoom(), keepPosition: false);
         }
         else {
            LayoutPages();
            // A reload keeps the reader on the page they were on, as far as the new document reaches.
            if (previous != null && anchor > 0) ScrollToPage(Math.Min(anchor, Pages.Count));
            RefreshVisiblePages();
         }

         // Closing waits for a render of the old document that may still be running, so not on this thread.
         if (previous != null) _ = Task.Run(previous.Dispose);
      }

      /// <summary>
      /// Releases the document and stops all work still running. Called when this viewer's entry is closed.
      /// </summary>
      public void Release() {
         _lifetime.Cancel();
         _searchCancel?.Cancel();
         var document = _document;
         _document = null;
         _data = null;
         foreach (var page in Pages) page.Drop();
         Pages.Clear();
         if (document != null) _ = Task.Run(document.Dispose);
      }

      #endregion

      #region Layout

      private void LayoutPages() {
         var top = PageGap;
         foreach (var page in Pages) {
            page.Layout(Zoom * PointToDip, top);
            top += page.Height + PageGap;
         }

         ApplyHighlights();
      }

      // The zoom at which the widest page fills the card, leaving the side padding and room for a scroll bar.
      private double FitWidthZoom() {
         if (Pages.Count == 0 || _viewportWidth <= 0) return 1;

         var widest = Pages.Max(r => r.PageSize.Width) * PointToDip;
         var zoom = (_viewportWidth - 2 * SidePadding - 2) / widest;
         return Math.Clamp(zoom, MinZoom, MaxZoom);
      }

      private void ApplyZoom(double zoom, bool keepPosition = true) {
         zoom = Math.Clamp(zoom, MinZoom, MaxZoom);

         // The point in the middle of the viewport stays where it is: remember it relative to its page.
         var anchorPage = keepPosition ? PageAt(_viewportOffset + _viewportHeight / 2) : null;
         var anchorRatio = anchorPage is null
            ? 0
            : (_viewportOffset + _viewportHeight / 2 - anchorPage.Top) / anchorPage.Height;

         Zoom = zoom;
         LayoutPages();

         var offset = anchorPage is null
            ? 0
            : anchorPage.Top + anchorRatio * anchorPage.Height - _viewportHeight / 2;
         _viewportOffset = Math.Max(0, offset);
         ScrollRequested?.Invoke(_viewportOffset);
         RefreshVisiblePages();
      }

      private PdfPageVm? PageAt(double offset) =>
         Pages.FirstOrDefault(r => r.Top + r.Height + PageGap > offset) ?? Pages.LastOrDefault();

      private void ScrollToPage(int pageNumber) {
         if (pageNumber < 1 || pageNumber > Pages.Count) return;
         ScrollTo(Pages[pageNumber - 1].Top - PageGap);
      }

      private void ScrollTo(double offset) {
         _viewportOffset = Math.Max(0, offset);
         ScrollRequested?.Invoke(_viewportOffset);
         RefreshVisiblePages();
      }

      #endregion

      #region Commands

      private void RaiseCommandsChanged() {
         foreach (var command in Commands) command.RaiseCanExecuteChanged();
      }

      // Almost every command applies only while a document is open and nothing is being waited for.
      private bool IsDocumentReady => HasDocument && !InWaiting;

      /// <summary>Fetches the PDF content again through its caller.</summary>
      public Task RefreshCommand() => LoadAsync();

      /// <summary>Refresh may run as long as it is not loading.</summary>
      public bool RefreshCommandAllowed() => Payload != null && !InWaiting;

      /// <summary>Goes to the previous page.</summary>
      public void PreviousPageCommand() => ScrollToPage(CurrentPage - 1);

      /// <summary>There is a page before the page currently visible.</summary>
      public bool PreviousPageCommandAllowed() => IsDocumentReady && CurrentPage > 1;

      /// <summary>Goes to the next page.</summary>
      public void NextPageCommand() => ScrollToPage(CurrentPage + 1);

      /// <summary>There is a page after the page currently visible.</summary>
      public bool NextPageCommandAllowed() => IsDocumentReady && CurrentPage < Pages.Count;

      /// <summary>Goes to the page typed in the page number box.</summary>
      public void GoToPageCommand() {
         if (int.TryParse(PageNumberText.Trim(), out var number))
            ScrollToPage(Math.Clamp(number, 1, Pages.Count));
         else
            PageNumberText = CurrentPage > 0 ? $"{CurrentPage}" : "";
      }

      /// <summary>There is a document whose pages can be navigated.</summary>
      public bool GoToPageCommandAllowed() => IsDocumentReady;

      /// <summary>Zooms out one step.</summary>
      public void ZoomOutCommand() {
         var next = ZoomSteps.LastOrDefault(r => r < Zoom - 0.001);
         ApplyZoom(next > 0 ? next : MinZoom);
      }

      /// <summary>Can still be zoomed out.</summary>
      public bool ZoomOutCommandAllowed() => IsDocumentReady && Zoom > MinZoom + 0.001;

      /// <summary>Zooms in one step.</summary>
      public void ZoomInCommand() {
         var next = ZoomSteps.FirstOrDefault(r => r > Zoom + 0.001);
         ApplyZoom(next > 0 ? next : MaxZoom);
      }

      /// <summary>Can still be zoomed in.</summary>
      public bool ZoomInCommandAllowed() => IsDocumentReady && Zoom < MaxZoom - 0.001;

      /// <summary>Adjusts the zoom so the widest page fills the width of the display.</summary>
      public void FitWidthCommand() => ApplyZoom(FitWidthZoom());

      /// <summary>There is a document whose width can be adjusted.</summary>
      public bool FitWidthCommandAllowed() => IsDocumentReady;

      /// <summary>Highlights the next search result; the first search builds the list of results.</summary>
      public Task FindNextCommand() => FindAsync(forward: true);

      /// <summary>There is text that can be searched.</summary>
      public bool FindNextCommandAllowed() => IsDocumentReady && !string.IsNullOrWhiteSpace(SearchText);

      /// <summary>Highlights the previous search result.</summary>
      public Task FindPreviousCommand() => FindAsync(forward: false);

      /// <summary>There is text that can be searched.</summary>
      public bool FindPreviousCommandAllowed() => FindNextCommandAllowed();

      /// <summary>Prints the document through the Windows print dialog.</summary>
      public async Task PrintCommand() {
         if (_document is not { } document) return;

         var dialog = new PrintDialog {
            UserPageRangeEnabled = true,
            MinPage = 1,
            MaxPage = (uint)document.PageCount,
            PageRange = new PageRange(1, document.PageCount)
         };
         if (dialog.ShowDialog() != true) return;

         var (first, last) = dialog.PageRangeSelection == PageRangeSelection.UserPages
            ? (dialog.PageRange.PageFrom, dialog.PageRange.PageTo)
            : (1, document.PageCount);
         first = Math.Clamp(first, 1, document.PageCount);
         last = Math.Clamp(last, first, document.PageCount);

         InWaiting = true;
         WaiterText = "Sending the document to the printer";
         RaiseCommandsChanged();
         try {
            await PdfPrinting.PrintAsync(document, first - 1, last - 1, dialog, Payload?.Title ?? "PDF document");
         }
         catch (Exception x) {
            DiaplayException(x);
         }
         finally {
            InWaiting = false;
            WaiterText = "";
            RaiseCommandsChanged();
         }
      }

      /// <summary>There is a document that can be printed.</summary>
      public bool PrintCommandAllowed() => IsDocumentReady;

      /// <summary>
      /// Saves the PDF to a file. What is written is the original bytes from the caller, without going through
      /// PDFium, so any digital signature in it stays valid.
      /// </summary>
      public async Task SaveAsCommand() {
         if (!AllowSaveAs || _data is not { } data) return;

         var dialog = new SaveFileDialog {
            Title = "Save PDF",
            FileName = SuggestedFileName(),
            DefaultExt = ".pdf",
            Filter = "PDF document|*.pdf|All files|*.*"
         };
         if (dialog.ShowDialog(DialogOwner) != true) return;

         try {
            await File.WriteAllBytesAsync(dialog.FileName, data);
         }
         catch (Exception x) {
            DiaplayException(x);
         }
      }

      /// <summary>There is a document that can be saved.</summary>
      public bool SaveAsCommandAllowed() => AllowSaveAs && IsDocumentReady && _data != null;

      private string SuggestedFileName() {
         var name = Payload?.FileName;
         if (string.IsNullOrWhiteSpace(name)) name = $"{Payload?.Title ?? "document"}.pdf";

         var invalid = Path.GetInvalidFileNameChars();
         return new string(name.Select(r => invalid.Contains(r) ? '_' : r).ToArray());
      }

      #endregion

      #region Search

      private async Task FindAsync(bool forward) {
         if (_document is not { } document) return;

         var query = SearchText.Trim();
         if (query.Length == 0) return;

         if (_hits is null || !string.Equals(_hitsQuery, query, StringComparison.Ordinal)) {
            _searchCancel?.Cancel();
            var cancel = _searchCancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            InWaiting = true;
            WaiterText = "Searching";
            try {
               var hits = await Task.Run(() => document.Search(query, matchCase: false, cancel.Token), cancel.Token);
               if (document != _document) return;

               _hits = hits;
               _hitsQuery = query;
               // The first match shown is the first one from the page being read, not from page one.
               var fromPage = Math.Max(0, CurrentPage - 1);
               var first = hits.Select((r, i) => (r, i)).FirstOrDefault(r => r.r.PageIndex >= fromPage);
               _hitIndex = hits.Count == 0 ? -1 : first.r is null ? 0 : first.i;
               if (!forward && _hitIndex >= 0) _hitIndex = (_hitIndex - 1 + hits.Count) % hits.Count;
            }
            catch (OperationCanceledException) {
               return;
            }
            catch (ObjectDisposedException) {
               return;
            }
            catch (Exception x) {
               DiaplayException(x);
               return;
            }
            finally {
               InWaiting = false;
               WaiterText = "";
               RaiseCommandsChanged();
            }
         }
         else if (_hits.Count > 0) {
            _hitIndex = (_hitIndex + (forward ? 1 : -1) + _hits.Count) % _hits.Count;
         }

         SearchCaption = _hits.Count == 0 ? "No match" : $"{_hitIndex + 1:N0} of {_hits.Count:N0}";
         ApplyHighlights();
         RevealCurrentHit();
      }

      private void ClearSearch() {
         _searchCancel?.Cancel();
         _hits = null;
         _hitsQuery = null;
         _hitIndex = -1;
         SearchCaption = "";
      }

      private void ApplyHighlights() {
         var scale = Zoom * PointToDip;
         foreach (var page in Pages) page.Highlights.Clear();
         if (_hits is null) return;

         for (var i = 0; i < _hits.Count; i++) {
            var hit = _hits[i];
            if (hit.PageIndex >= Pages.Count) continue;

            foreach (var box in hit.Boxes)
               Pages[hit.PageIndex].Highlights.Add(new PdfHighlightVm(box, scale, i == _hitIndex));
         }
      }

      private void RevealCurrentHit() {
         if (_hits is null || _hitIndex < 0 || _hitIndex >= _hits.Count) return;

         var hit = _hits[_hitIndex];
         var page = Pages[hit.PageIndex];
         var scale = Zoom * PointToDip;
         var boxTop = page.Top + hit.Boxes.Min(r => r.Top) * scale;
         var boxBottom = page.Top + hit.Boxes.Max(r => r.Bottom) * scale;
         if (boxTop >= _viewportOffset && boxBottom <= _viewportOffset + _viewportHeight) return;

         ScrollTo(boxTop - _viewportHeight / 3);
      }

      #endregion
   }

   /// <summary>
   /// One page in <see cref="PdfViewer"/>: its size and place in the page list, the rendered bitmap when it
   /// is visible, and the search highlights over it.
   /// </summary>
   public class PdfPageVm : NotifyPropertyBase
   {
      private double _renderedScale;
      private volatile bool _isWanted;

      internal PdfPageVm(int index, Size pageSize) {
         Index = index;
         PageSize = pageSize;
      }

      /// <summary>Sequence number of the page, starting from 0.</summary>
      public int Index { get; }

      /// <summary>Size of the page in points (1/72 inch).</summary>
      public Size PageSize { get; }

      /// <summary>Width of the page on screen, in DIPs.</summary>
      public double Width {
         get => Get(0.0);
         private set => Set(value);
      }

      /// <summary>Height of the page on screen, in DIPs.</summary>
      public double Height {
         get => Get(0.0);
         private set => Set(value);
      }

      /// <summary>Position of the page's top edge from the start of the page list, in DIPs.</summary>
      public double Top { get; private set; }

      /// <summary>
      /// The render result of the page, or <c>null</c> while this page is far from the screen. Shown stretched
      /// until the render for the new zoom finishes.
      /// </summary>
      public BitmapSource? Bitmap {
         get => Get<BitmapSource?>();
         private set => Set(value);
      }

      /// <summary>Search result highlights on this page.</summary>
      public ObservableCollection<PdfHighlightVm> Highlights { get; } = [];

      internal bool IsWanted {
         get => _isWanted;
         set => _isWanted = value;
      }

      internal double PendingScale { get; set; }

      internal void Layout(double scale, double top) {
         Width = Math.Round(PageSize.Width * scale);
         Height = Math.Round(PageSize.Height * scale);
         Top = top;
      }

      internal bool NeedsRender(double scale) =>
         PendingScale != scale && (Bitmap is null || Math.Abs(_renderedScale - scale) > 0.0001);

      internal void SetBitmap(BitmapSource bitmap, double scale) {
         Bitmap = bitmap;
         _renderedScale = scale;
      }

      internal void Drop() {
         IsWanted = false;
         if (Bitmap is null) return;
         Bitmap = null;
         _renderedScale = 0;
      }
   }

   /// <summary>
   /// One search result highlight box over a page of <see cref="PdfViewer"/>, in DIPs from the top-left
   /// corner of its page.
   /// </summary>
   public class PdfHighlightVm
   {
      internal PdfHighlightVm(Rect box, double scale, bool isCurrent) {
         Left = box.Left * scale;
         Top = box.Top * scale;
         Width = Math.Max(1, box.Width * scale);
         Height = Math.Max(1, box.Height * scale);
         IsCurrent = isCurrent;
      }

      /// <summary>Distance from the left edge of the page.</summary>
      public double Left { get; }

      /// <summary>Distance from the top edge of the page.</summary>
      public double Top { get; }

      /// <summary>Width of the box.</summary>
      public double Width { get; }

      /// <summary>Height of the box.</summary>
      public double Height { get; }

      /// <summary><c>true</c> for the search result currently being targeted.</summary>
      public bool IsCurrent { get; }
   }
}
