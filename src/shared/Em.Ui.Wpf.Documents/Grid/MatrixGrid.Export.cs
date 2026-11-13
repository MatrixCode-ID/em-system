using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Em.Documents.Formats;
using Em.Documents.Model;
using Em.Grid.Export;
using Em.Grid.Menus;
using Em.Ui.Wpf.Documents.Printing;
using Em.Ui.Wpf.Documents.Themes;

namespace Em.Ui.Wpf.Documents.Grid;

public sealed partial class MatrixGrid
{
    private DocumentFormatRegistry? _exportFormats;
    private ICommand? _exportCommand;
    private ICommand? _printPreviewCommand;
    private bool _exportSelectedRowsOnly;

    /// <summary>Gets or sets the title used for exports and the print preview.</summary>
    public string DocumentTitle { get; set; } = "MatrixGrid";
    /// <summary>Gets or sets the export writers. Defaults to PDF, XLSX, DOCX and CSV.</summary>
    public DocumentFormatRegistry ExportFormats
    {
        get => _exportFormats ??= GridDocument.CreateFormats();
        set => _exportFormats = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets a command that asks for a file name and exports the view.</summary>
    public ICommand ExportCommand => _exportCommand ??= new DocumentCommand(
        () => _ = ExportViewAsync(), CanExport);

    /// <summary>Gets a command that opens the print preview of the view.</summary>
    public ICommand PrintPreviewCommand => _printPreviewCommand ??= new DocumentCommand(
        () => ShowPrintPreview(), CanExport);

    /// <summary>Creates a document of the current view for a writer or a custom renderer.</summary>
    /// <param name="options">The export options, or null for the defaults.</param>
    /// <param name="progress">Receives progress while rows are read.</param>
    /// <returns>The document.</returns>
    public Document CreateDocument(GridExportOptions? options = null, IProgress<ExportProgress>? progress = null)
    {
        var source = Provider ?? _local ?? throw new InvalidOperationException("The grid is not bound to a data source.");
        return GridDocument.Create(source, ExportSnapshot(), options ?? new GridExportOptions { Title = DocumentTitle }, progress);
    }

    /// <summary>Opens the print preview of the current view as a dialog.</summary>
    /// <param name="options">The export options, or null for the defaults.</param>
    public void ShowPrintPreview(GridExportOptions? options = null)
    {
        if ((Provider ?? _local) is not { } source || !CanExport())
            return;
        var snapshot = ExportSnapshot();
        var settings = options ?? new GridExportOptions { Title = DocumentTitle };
        var owner = Window.GetWindow(this);
        var preview = new PrintPreviewWindow((page, fit, progress) =>
            GridDocument.Create(source, snapshot, settings with { Page = page, FitToWidth = fit }, progress),
            ExportFormats, settings.Page, settings.FitToWidth,
            GridTheme.GetMode(this) == GridThemeMode.Dark);
        if (owner is not null)
        {
            preview.Owner = owner;
            preview.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        preview.ShowDialog();
    }

    /// <summary>Asks for a file name and exports the current view.</summary>
    /// <param name="formatId">The format to preselect, or null for the first registered format.</param>
    /// <param name="options">The export options, or null for the defaults.</param>
    /// <returns>A task that completes when the export finishes or is canceled.</returns>
    public async Task ExportViewAsync(string? formatId = null, GridExportOptions? options = null)
    {
        if (!CanExport())
            return;

        try
        {
            var settings = options ?? ChooseExportOptions();
            if (settings is null)
                return;
            var owner = Window.GetWindow(this);
            var file = DocumentFileExport.ChooseFile(owner, ExportFormats, settings.Title, formatId);
            if (file is null)
                return;
            using var cancellation = new CancellationTokenSource();
            var status = new TextBlock { Text = "Preparing export…", Margin = new Thickness(0, 0, 0, 10) };
            var cancel = Material.Button("Cancel export", "OutlinedButton");
            int pagesWritten = 0;
            ExportProgress rows = default;
            void ShowProgress() => status.Text = (rows.TotalRows is long total
                    ? $"Exporting {rows.RowsWritten:N0} of {total:N0} rows"
                    : $"Exporting {rows.RowsWritten:N0} rows")
                + (pagesWritten > 0 ? $" · {pagesWritten:N0} pages written…" : "…");
            var progress = new Progress<ExportProgress>(update => { rows = update; ShowProgress(); });
            var pageProgress = new Progress<int>(pages => { pagesWritten = pages; ShowProgress(); });
            var panel = new StackPanel();
            panel.Children.Add(status);
            panel.Children.Add(cancel);
            var dialog = CreateDialog("Exporting document", panel, 360);
            bool finished = false;
            cancel.Click += (_, _) => cancellation.Cancel();
            dialog.Closing += (_, args) =>
            {
                if (!finished)
                {
                    args.Cancel = true;
                    cancellation.Cancel();
                }
            };
            var document = CreateDocument(settings, progress) with { PageProgress = pageProgress };
            dialog.Loaded += async (_, _) =>
            {
                try
                {
                    await DocumentFileExport.WriteFileAsync(ExportFormats.Get(file.Value.FormatId), document,
                        file.Value.Path, cancellation.Token);
                    ShowMessage("Export complete");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { ShowMessage("Export canceled"); }
                catch (Exception ex) { ShowMessage(ex.GetBaseException().Message); }
                finally
                {
                    finished = true;
                    dialog.Close();
                }
            };
            dialog.ShowDialog();
        }
        catch (Exception ex) { ShowMessage(ex.GetBaseException().Message); }
        await Task.CompletedTask;
    }

    private bool CanExport() => (Provider ?? _local) is not null && _displayColumns.Count > 0;

    private GridExportSnapshot ExportSnapshot() => new(
        _displayColumns.Select(column => column with { Width = WidthFor(column) }).ToImmutableArray(),
        EffectiveQuery, State.SelectedRows, ResultRowCount);

    private GridExportOptions? ChooseExportOptions()
    {
        var panel = new StackPanel();
        var title = new TextBox { Text = DocumentTitle, Margin = new Thickness(0, 3, 0, 8) };
        var rows = new ComboBox { ItemsSource = new[] { "All rows in view", "Selected rows" },
            SelectedIndex = _exportSelectedRowsOnly && State.SelectedRows.Count > 0 ? 1 : 0 };
        var values = new ComboBox { ItemsSource = new[] { "Typed and formatted", "Raw values", "Display text" }, SelectedIndex = 0 };
        var groups = new CheckBox { Content = "Include group rows", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
        var summaries = new CheckBox { Content = "Include summaries", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "Document title" });
        panel.Children.Add(title);
        panel.Children.Add(rows);
        panel.Children.Add(values);
        panel.Children.Add(groups);
        panel.Children.Add(summaries);
        var next = Material.Button("Choose file…", "FilledButton");
        next.IsDefault = true;
        var cancel = Material.Button("Cancel");
        cancel.IsCancel = true;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(cancel);
        actions.Children.Add(next);
        panel.Children.Add(actions);
        var dialog = CreateDialog("Export options", panel, 400);
        next.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() != true)
            return null;
        return new GridExportOptions
        {
            Title = title.Text,
            SelectedOnly = rows.SelectedIndex == 1,
            IncludeGroups = groups.IsChecked == true,
            IncludeSummaries = summaries.IsChecked == true,
            Values = (ExportValueMode)values.SelectedIndex
        };
    }

    private MenuSubmenu ExportMenu() => new("Export", ExportFormats.Formats.Select<Em.Documents.Formats.DocumentFormat, MenuEntry>(
        format => new MenuCommand("export:" + format.Id, format.DisplayName, Material.FileIcon(format.Id),
            format.Extension, IsEnabled: CanExport()))
        .Prepend(new MenuSection("Export grid as"))
        .Append(new MenuSeparator())
        .Append(new MenuCommand("export-selected", "Selected rows only", "Check", IsEnabled: State.SelectedRows.Count > 0,
            IsChecked: _exportSelectedRowsOnly && State.SelectedRows.Count > 0))
        .Append(new MenuCommand("export", "Export options…", "Export", IsEnabled: CanExport()))
        .ToImmutableArray(), "Export", "export-menu");

    private sealed class DocumentCommand(Action execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
        public bool CanExecute(object? parameter) => canExecute();
        public void Execute(object? parameter) => execute();
    }
}
