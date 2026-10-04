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
   /// Viewer PDF bawaan aplikasi, dibuka lewat <see cref="EmApp.ViewPdf"/> atau
   /// <see cref="NavigationEntry.ViewPdf"/> dengan <see cref="PdfViewerNavigationPayload"/>. Menampilkan,
   /// mencari, mencetak, dan menyimpan PDF apa pun yang diberikan pemanggilnya; tidak dijaga claim, karena
   /// hak atas isinya diperiksa di tempat isi itu diambil.
   /// </summary>
   public partial class PdfViewer : UserControl, INavigationBody
   {
      /// <summary>
      /// Membuat viewer PDF untuk aplikasi <paramref name="app"/>.
      /// </summary>
      public PdfViewer(EmApp app) {
         InitializeComponent();
         Vm.EmApp = app;
         Vm.ScrollRequested += offset => PageScroller.ScrollToVerticalOffset(offset);
         Loaded += (_, _) => Vm.DeviceScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
      }

      /// <summary>ViewModel layar ini.</summary>
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
   /// ViewModel untuk <see cref="PdfViewer"/>.
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
      /// Membuat ViewModel baru dan mendaftarkan seluruh command layar.
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
      /// Diminta ViewModel saat tampilan harus digulir ke posisi tertentu (dalam DIP dari atas daftar
      /// halaman) - pindah halaman, hasil pencarian, atau menjaga posisi saat zoom berubah.
      /// </summary>
      public event Action<double>? ScrollRequested;

      /// <summary>Parameter yang membuka layar ini: judul, pengambil isi PDF, dan nama file bawaannya.</summary>
      public PdfViewerNavigationPayload? Payload { get; internal set; }
      public bool AllowSaveAs { get => Get(true); set => Set(value, _ => RaiseCommandsChanged()); }
      public bool ShowErrorDetails { get; set; } = true;

      #region Pages

      /// <summary>Seluruh halaman dokumen yang sedang dibuka, sesuai urutannya.</summary>
      public ObservableCollection<PdfPageVm> Pages { get; } = [];

      /// <summary>Jarak daftar halaman dari tepi kartu.</summary>
      public Thickness PagesMargin { get; } = new(SidePadding, PageGap, SidePadding, 0);

      /// <summary>Jarak di bawah setiap halaman.</summary>
      public Thickness PageMargin { get; } = new(0, 0, 0, PageGap);

      /// <summary>
      /// Skala layar tempat viewer tampil (1 = 96 DPI). Dipakai supaya halaman dirender setajam piksel
      /// layar yang sebenarnya, bukan sekadar ukuran DIP-nya.
      /// </summary>
      public double DeviceScale {
         get => Get(1.0);
         set => Set(value, _ => RefreshVisiblePages());
      }

      /// <summary><c>true</c> kalau ada dokumen yang terbuka.</summary>
      public bool HasDocument => _document != null;

      /// <summary>Pesan kegagalan memuat dokumen, ditampilkan selama belum ada dokumen yang terbuka.</summary>
      public string? ErrorMessage {
         get => Get<string?>();
         private set => Set(value, _ => NotifyChanged(nameof(HasError)));
      }

      /// <summary><c>true</c> kalau dokumen gagal dimuat dan tidak ada dokumen lain yang bisa ditampilkan.</summary>
      public bool HasError => !string.IsNullOrEmpty(ErrorMessage) && !HasDocument;

      /// <summary>Nomor halaman (mulai dari 1) yang sedang paling banyak terlihat.</summary>
      public int CurrentPage {
         get => Get(0);
         private set => Set(value, r => {
            PageNumberText = r > 0 ? $"{r}" : "";
            RaiseCommandsChanged();
         });
      }

      /// <summary>Isi kotak nomor halaman; Enter memindahkan tampilan ke halaman itu.</summary>
      public string PageNumberText {
         get => Get("");
         set => Set(value);
      }

      /// <summary>Keterangan jumlah halaman di samping kotak nomor halaman, mis. <c>"/ 12"</c>.</summary>
      public string PageCountCaption => $"/ {Pages.Count:N0}";

      #endregion

      #region Zoom

      /// <summary>Perbesaran tampilan: 1 berarti ukuran asli halaman.</summary>
      public double Zoom {
         get => Get(1.0);
         private set => Set(value, _ => {
            NotifyChanged(nameof(ZoomCaption));
            RaiseCommandsChanged();
         });
      }

      /// <summary>Perbesaran dalam persen, mis. <c>"100%"</c>.</summary>
      public string ZoomCaption => $"{Math.Round(Zoom * 100):0}%";

      /// <summary>Memperbesar atau memperkecil satu langkah - dipakai Ctrl + roda mouse.</summary>
      /// <param name="zoomIn"><c>true</c> untuk memperbesar.</param>
      public void ZoomStep(bool zoomIn) {
         if (!HasDocument) return;
         if (zoomIn) ZoomInCommand();
         else ZoomOutCommand();
      }

      #endregion

      #region Search

      /// <summary>Teks yang dicari di dokumen.</summary>
      public string SearchText {
         get => Get("");
         set => Set(value, _ => RaiseCommandsChanged());
      }

      /// <summary>Posisi hasil pencarian yang sedang disorot, mis. <c>"3 of 12"</c>.</summary>
      public string SearchCaption {
         get => Get("");
         private set => Set(value);
      }

      #endregion

      #region Viewport

      /// <summary>
      /// Memberi tahu posisi dan ukuran area tampilan halaman. Menentukan halaman mana yang dirender, mana
      /// yang bitmapnya dibuang, dan nomor halaman yang sedang terlihat.
      /// </summary>
      /// <param name="offset">Posisi gulir vertikal, dalam DIP.</param>
      /// <param name="height">Tinggi area tampilan, dalam DIP.</param>
      /// <param name="width">Lebar area tampilan, dalam DIP.</param>
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
      /// Mengambil isi PDF lewat <see cref="PdfViewerNavigationPayload.Loader"/> lalu menampilkannya.
      /// Dipanggil saat viewer dibuka dan oleh tombol Refresh. Kalau gagal, dokumen yang sudah tampil
      /// (kalau ada) tetap ditampilkan.
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
      /// Melepas dokumen dan menghentikan seluruh pekerjaan yang masih berjalan. Dipanggil saat entri
      /// viewer ini ditutup.
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

      /// <summary>Mengambil ulang isi PDF lewat pemanggilnya.</summary>
      public Task RefreshCommand() => LoadAsync();

      /// <summary>Refresh bisa dijalankan selama tidak sedang memuat.</summary>
      public bool RefreshCommandAllowed() => Payload != null && !InWaiting;

      /// <summary>Pindah ke halaman sebelumnya.</summary>
      public void PreviousPageCommand() => ScrollToPage(CurrentPage - 1);

      /// <summary>Ada halaman sebelum halaman yang sedang terlihat.</summary>
      public bool PreviousPageCommandAllowed() => IsDocumentReady && CurrentPage > 1;

      /// <summary>Pindah ke halaman berikutnya.</summary>
      public void NextPageCommand() => ScrollToPage(CurrentPage + 1);

      /// <summary>Ada halaman sesudah halaman yang sedang terlihat.</summary>
      public bool NextPageCommandAllowed() => IsDocumentReady && CurrentPage < Pages.Count;

      /// <summary>Pindah ke halaman yang diketik di kotak nomor halaman.</summary>
      public void GoToPageCommand() {
         if (int.TryParse(PageNumberText.Trim(), out var number))
            ScrollToPage(Math.Clamp(number, 1, Pages.Count));
         else
            PageNumberText = CurrentPage > 0 ? $"{CurrentPage}" : "";
      }

      /// <summary>Ada dokumen yang bisa dituju halamannya.</summary>
      public bool GoToPageCommandAllowed() => IsDocumentReady;

      /// <summary>Memperkecil satu langkah.</summary>
      public void ZoomOutCommand() {
         var next = ZoomSteps.LastOrDefault(r => r < Zoom - 0.001);
         ApplyZoom(next > 0 ? next : MinZoom);
      }

      /// <summary>Masih bisa diperkecil.</summary>
      public bool ZoomOutCommandAllowed() => IsDocumentReady && Zoom > MinZoom + 0.001;

      /// <summary>Memperbesar satu langkah.</summary>
      public void ZoomInCommand() {
         var next = ZoomSteps.FirstOrDefault(r => r > Zoom + 0.001);
         ApplyZoom(next > 0 ? next : MaxZoom);
      }

      /// <summary>Masih bisa diperbesar.</summary>
      public bool ZoomInCommandAllowed() => IsDocumentReady && Zoom < MaxZoom - 0.001;

      /// <summary>Menyesuaikan perbesaran supaya halaman terlebar memenuhi lebar tampilan.</summary>
      public void FitWidthCommand() => ApplyZoom(FitWidthZoom());

      /// <summary>Ada dokumen yang bisa disesuaikan lebarnya.</summary>
      public bool FitWidthCommandAllowed() => IsDocumentReady;

      /// <summary>Menyorot hasil pencarian berikutnya; pencarian pertama menyusun daftar hasilnya.</summary>
      public Task FindNextCommand() => FindAsync(forward: true);

      /// <summary>Ada teks yang bisa dicari.</summary>
      public bool FindNextCommandAllowed() => IsDocumentReady && !string.IsNullOrWhiteSpace(SearchText);

      /// <summary>Menyorot hasil pencarian sebelumnya.</summary>
      public Task FindPreviousCommand() => FindAsync(forward: false);

      /// <summary>Ada teks yang bisa dicari.</summary>
      public bool FindPreviousCommandAllowed() => FindNextCommandAllowed();

      /// <summary>Mencetak dokumen lewat dialog cetak Windows.</summary>
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

      /// <summary>Ada dokumen yang bisa dicetak.</summary>
      public bool PrintCommandAllowed() => IsDocumentReady;

      /// <summary>
      /// Menyimpan PDF ke file. Yang ditulis adalah byte asli dari pemanggilnya, tanpa lewat PDFium, supaya
      /// tanda tangan digital di dalamnya tetap sah.
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

      /// <summary>Ada dokumen yang bisa disimpan.</summary>
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
   /// Satu halaman di <see cref="PdfViewer"/>: ukuran dan letaknya di daftar halaman, bitmap hasil render
   /// kalau sedang terlihat, dan sorotan hasil pencarian di atasnya.
   /// </summary>
   public class PdfPageVm : NotifyPropertyBase
   {
      private double _renderedScale;
      private volatile bool _isWanted;

      internal PdfPageVm(int index, Size pageSize) {
         Index = index;
         PageSize = pageSize;
      }

      /// <summary>Nomor urut halaman, mulai dari 0.</summary>
      public int Index { get; }

      /// <summary>Ukuran halaman dalam point (1/72 inci).</summary>
      public Size PageSize { get; }

      /// <summary>Lebar halaman di layar, dalam DIP.</summary>
      public double Width {
         get => Get(0.0);
         private set => Set(value);
      }

      /// <summary>Tinggi halaman di layar, dalam DIP.</summary>
      public double Height {
         get => Get(0.0);
         private set => Set(value);
      }

      /// <summary>Posisi tepi atas halaman dari awal daftar halaman, dalam DIP.</summary>
      public double Top { get; private set; }

      /// <summary>
      /// Hasil render halaman, atau <c>null</c> selama halaman ini jauh dari layar. Ditampilkan
      /// direntangkan sampai render untuk perbesaran yang baru selesai.
      /// </summary>
      public BitmapSource? Bitmap {
         get => Get<BitmapSource?>();
         private set => Set(value);
      }

      /// <summary>Sorotan hasil pencarian di halaman ini.</summary>
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
   /// Satu kotak sorotan hasil pencarian di atas sebuah halaman <see cref="PdfViewer"/>, dalam DIP dari
   /// pojok kiri atas halamannya.
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

      /// <summary>Jarak dari tepi kiri halaman.</summary>
      public double Left { get; }

      /// <summary>Jarak dari tepi atas halaman.</summary>
      public double Top { get; }

      /// <summary>Lebar kotak.</summary>
      public double Width { get; }

      /// <summary>Tinggi kotak.</summary>
      public double Height { get; }

      /// <summary><c>true</c> untuk hasil pencarian yang sedang dituju.</summary>
      public bool IsCurrent { get; }
   }
}
