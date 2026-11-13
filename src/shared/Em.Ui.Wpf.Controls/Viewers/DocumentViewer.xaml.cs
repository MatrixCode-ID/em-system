using System.IO;
using System.Windows;
using System.Windows.Controls;
using Em.Editing;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Win32;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Controls
{
   /// <summary>
   /// The built-in read-only document viewer, opened with a <see cref="DocumentViewerNavigationPayload"/>, usually through
   /// <see cref="DocumentViewers.ViewDocument(Em.Ui.Wpf.Core.NavigationEntry,string,Func{CancellationToken,Task{Stream}},string)"/>.
   /// It shows a workbook in <see cref="MatrixSheet"/> or a document in <see cref="MatrixWriter"/>, both in
   /// <see cref="EditorMode.Viewer"/> mode, and can save the original file. It is not guarded by a claim, because the
   /// rights to its content are checked where that content is fetched.
   /// </summary>
   public partial class DocumentViewer : UserControl, INavigationBody
   {
      private EditorControl? _editor;

      /// <summary>Creates the document viewer.</summary>
      public DocumentViewer() {
         InitializeComponent();
         Vm.ContentLoaded += ShowContentAsync;
      }

      /// <summary>The view model of this screen.</summary>
      public DocumentViewerVm Vm => (DocumentViewerVm)DataContext;

      /// <summary>The editor that shows the current content, or null before the first content arrives.</summary>
      public EditorControl? Editor => _editor;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) {
         if (args.Data is not DocumentViewerNavigationPayload payload) {
            args.Cancel = true;
            args.Message = "The document viewer can only be opened with a document viewer payload.";
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
         Vm.ContentLoaded -= ShowContentAsync;
         Vm.Release();
         _editor?.Dispose();
         _editor = null;
         EditorHost.Content = null;
         return Task.CompletedTask;
      }

      // The editor is a control, so it is created and fed here; the view model only fetches the bytes. A reload keeps the
      // same editor and replaces its document.
      private async Task ShowContentAsync(byte[] data, string fileName, DocumentViewKind kind, CancellationToken cancellation) {
         if (_editor is null || !Fits(_editor, kind)) {
            _editor?.Dispose();
            _editor = kind == DocumentViewKind.Sheet ? new MatrixSheet() : new MatrixWriter();
            _editor.Mode = EditorMode.Viewer;
            _editor.AllowOpen = false;
            EditorHost.Content = _editor;
         }

         var editor = _editor;
         using var stream = new MemoryStream(data, writable: false);
         var opened = await editor.OpenAsync(stream, fileName, cancellation);
         if (!ReferenceEquals(editor, _editor)) return;
         Vm.ReportOpened(opened, editor.OpenError, editor.OpenWarnings);
      }

      private static bool Fits(EditorControl editor, DocumentViewKind kind) =>
         kind == DocumentViewKind.Sheet ? editor is MatrixSheet : editor is MatrixWriter;
   }

   /// <summary>Hands content fetched by <see cref="DocumentViewerVm"/> to the view, which opens it in an editor.</summary>
   /// <param name="data">The original bytes of the file.</param>
   /// <param name="fileName">The file name whose extension selects the format.</param>
   /// <param name="kind">The editor that shows the file.</param>
   /// <param name="cancellation">Cancelled when a newer load starts or the viewer closes.</param>
   /// <returns>A task that completes when the editor has finished opening the content.</returns>
   public delegate Task DocumentContentHandler(byte[] data, string fileName, DocumentViewKind kind, CancellationToken cancellation);

   /// <summary>View model for <see cref="DocumentViewer"/>: fetches the content, tracks errors and warnings, and saves the original file.</summary>
   public class DocumentViewerVm : MvvmModelBase
   {
      private readonly CancellationTokenSource _lifetime = new();
      private CancellationTokenSource? _loadCancel;
      private byte[]? _data;

      /// <summary>Creates the view model and registers the commands of the screen.</summary>
      public DocumentViewerVm() {
         RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
         RegisterCommand(nameof(SaveAsCommand), SaveAsCommand, SaveAsCommandAllowed);
      }

      /// <summary>Raised when content has been fetched and must be shown; the view opens it in the matching editor.</summary>
      public event DocumentContentHandler? ContentLoaded;

      /// <summary>The parameter that opened this screen: the title, the content fetcher and the file name.</summary>
      public DocumentViewerNavigationPayload? Payload {
         get => Get<DocumentViewerNavigationPayload?>();
         internal set => Set(value, payload => {
            NotifyChanged(nameof(FileName));
            NotifyChanged(nameof(IsSheet));
            NotifyChanged(nameof(IsWriter));
            _ = LoadAsync();
         });
      }

      /// <summary>The file name shown in the tool strip.</summary>
      public string FileName => Payload is { } payload ? Path.GetFileName(payload.FileName) : "";

      /// <summary><c>true</c> when the content is shown in <see cref="MatrixSheet"/>.</summary>
      public bool IsSheet => Payload?.Kind == DocumentViewKind.Sheet;

      /// <summary><c>true</c> when the content is shown in <see cref="MatrixWriter"/>.</summary>
      public bool IsWriter => Payload?.Kind == DocumentViewKind.Writer;

      /// <summary>Indicates whether a failure to fetch the content also opens the exception details dialog; the message is always shown in the viewer.</summary>
      public bool ShowErrorDetails { get; set; } = true;

      /// <summary>Indicates whether Save as is offered.</summary>
      public bool AllowSaveAs { get => Get(true); set => Set(value, _ => RaiseCommandsChanged()); }

      /// <summary><c>true</c> when the content is open in the editor.</summary>
      public bool HasDocument { get => Get(false); private set => Set(value, _ => NotifyChanged(nameof(HasError))); }

      /// <summary>Error message of fetching or opening the content, shown while no document is open.</summary>
      public string? ErrorMessage { get => Get<string?>(); private set => Set(value, _ => NotifyChanged(nameof(HasError))); }

      /// <summary><c>true</c> when the content failed to load and nothing is shown.</summary>
      public bool HasError => !string.IsNullOrEmpty(ErrorMessage) && !HasDocument;

      /// <summary>Messages about content the editor could not read and skipped.</summary>
      public IReadOnlyList<string> Warnings {
         get => Get<IReadOnlyList<string>>([]);
         private set => Set(value, _ => {
            NotifyChanged(nameof(HasWarnings));
            NotifyChanged(nameof(WarningCaption));
            NotifyChanged(nameof(WarningDetails));
         });
      }

      /// <summary><c>true</c> when the editor skipped some content.</summary>
      public bool HasWarnings => Warnings.Count > 0;

      /// <summary>Short caption of the warnings, e.g. <c>"2 items not shown"</c>.</summary>
      public string WarningCaption => Warnings.Count == 1 ? "1 item not shown" : $"{Warnings.Count:N0} items not shown";

      /// <summary>All warnings, one per line, for the tooltip.</summary>
      public string WarningDetails => string.Join(Environment.NewLine, Warnings);

      /// <summary>Fetches the content again and shows it; a load that is still running is cancelled.</summary>
      /// <returns>A task that completes when the content is shown or the load failed.</returns>
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
            if (cancel.IsCancellationRequested) return;

            _data = data;
            ErrorMessage = null;
            if (ContentLoaded is { } handler) await handler(data, payload.FileName, payload.Kind, cancel.Token);
         }
         catch (OperationCanceledException) when (cancel.IsCancellationRequested) {
            // A newer load, or closing the viewer, took over.
         }
         catch (Exception x) {
            ErrorMessage = x.Message;
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

      /// <summary>Records the outcome of opening the content in the editor.</summary>
      /// <param name="opened">Whether the editor shows the content.</param>
      /// <param name="error">The error of the open, or null.</param>
      /// <param name="warnings">Messages about content that was skipped.</param>
      public void ReportOpened(bool opened, Exception? error, IReadOnlyList<string> warnings) {
         HasDocument = opened;
         Warnings = opened ? warnings : [];
         ErrorMessage = opened ? null : error?.Message ?? "The file could not be read.";
         RaiseCommandsChanged();
      }

      /// <summary>Releases the content and stops loading. Called when this viewer's entry is closed.</summary>
      public void Release() {
         _lifetime.Cancel();
         _data = null;
      }

      /// <summary>Fetches the content again.</summary>
      /// <returns>A task that completes when the content is shown.</returns>
      public Task RefreshCommand() => LoadAsync();

      /// <summary>There is content to fetch and no load is running.</summary>
      /// <returns>True when Reload can run.</returns>
      public bool RefreshCommandAllowed() => Payload != null && !InWaiting;

      /// <summary>Saves the original bytes of the file, unchanged, to a location the user picks.</summary>
      /// <returns>A task that completes when the file is written.</returns>
      public async Task SaveAsCommand() {
         if (!AllowSaveAs || _data is not { } data || Payload is not { } payload) return;
         var extension = Path.GetExtension(payload.FileName);
         var dialog = new SaveFileDialog {
            Title = "Save document",
            FileName = Path.GetFileName(payload.FileName),
            DefaultExt = extension,
            Filter = $"{extension.TrimStart('.').ToUpperInvariant()} file|*{extension}|All files|*.*"
         };
         if (dialog.ShowDialog(DialogOwner) != true) return;
         try {
            await File.WriteAllBytesAsync(dialog.FileName, data);
         }
         catch (Exception x) {
            DiaplayException(x);
         }
      }

      /// <summary>There is content that can be saved.</summary>
      /// <returns>True when Save as can run.</returns>
      public bool SaveAsCommandAllowed() => AllowSaveAs && !InWaiting && _data != null;

      private void RaiseCommandsChanged() {
         foreach (var command in Commands) command.RaiseCanExecuteChanged();
      }
   }
}
