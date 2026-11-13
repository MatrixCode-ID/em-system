using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Em.Editing;
using Em.Ui.Wpf.Documents.Themes;
using WpfGrid = System.Windows.Controls.Grid;

namespace Em.Ui.Wpf.Documents.Editing;

/// <summary>Base of the MatrixSheet and MatrixWriter editors: a command toolbar or ribbon over an editor surface, file opening with a waiter, viewer mode and keyboard shortcuts.</summary>
public abstract class EditorControl : ContentControl, IDisposable, IToolbarState
{
    /// <summary>Identifies the <see cref="Mode"/> dependency property.</summary>
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(nameof(Mode), typeof(EditorMode),
        typeof(EditorControl), new PropertyMetadata(EditorMode.Full, ModeUpdated), value => value is EditorMode.Full or EditorMode.Viewer);
    /// <summary>Identifies the <see cref="ToolbarStyle"/> dependency property.</summary>
    public static readonly DependencyProperty ToolbarStyleProperty = DependencyProperty.Register(nameof(ToolbarStyle), typeof(ToolbarStyle),
        typeof(EditorControl), new PropertyMetadata(ToolbarStyle.Compact, ToolbarUpdated), value => value is ToolbarStyle.Compact or ToolbarStyle.Ribbon);
    /// <summary>Identifies the <see cref="ShowToolbar"/> dependency property.</summary>
    public static readonly DependencyProperty ShowToolbarProperty = DependencyProperty.Register(nameof(ShowToolbar), typeof(bool),
        typeof(EditorControl), new PropertyMetadata(true, ToolbarUpdated));
    /// <summary>Identifies the <see cref="AllowOpen"/> dependency property.</summary>
    public static readonly DependencyProperty AllowOpenProperty = DependencyProperty.Register(nameof(AllowOpen), typeof(bool),
        typeof(EditorControl), new PropertyMetadata(true, ToolbarUpdated));
    /// <summary>Identifies the <see cref="Source"/> dependency property.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(string),
        typeof(EditorControl), new PropertyMetadata(null, SourceChanged));

    private EditorToolbar? toolbar;
    private CancellationTokenSource? openRequest;
    private Control? waiter;
    private DockPanel? editorLayout;
    private bool initialized;
    private bool disposed;
    private bool sourcePending;
    internal bool RenderOffscreen { get; set; }

    /// <summary>Initializes the editor; the surface is built when the control is first loaded and visible.</summary>
    protected EditorControl()
    {
        Focusable = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Loaded += (_, _) => Activate();
        IsVisibleChanged += (_, _) => Activate();
    }

    /// <summary>Gets or sets whether the document can be edited or only viewed.</summary>
    public EditorMode Mode { get => (EditorMode)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    /// <summary>Gets or sets whether commands are shown as a compact toolbar or a ribbon.</summary>
    public ToolbarStyle ToolbarStyle { get => (ToolbarStyle)GetValue(ToolbarStyleProperty); set => SetValue(ToolbarStyleProperty, value); }
    /// <summary>Gets or sets whether the toolbar or ribbon is shown.</summary>
    public bool ShowToolbar { get => (bool)GetValue(ShowToolbarProperty); set => SetValue(ShowToolbarProperty, value); }
    /// <summary>Gets or sets whether the Open command is offered.</summary>
    public bool AllowOpen { get => (bool)GetValue(AllowOpenProperty); set => SetValue(AllowOpenProperty, value); }
    /// <summary>Gets or sets the path of a file to open. The file is read in the background when the editor becomes visible.</summary>
    public string? Source { get => (string?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    /// <summary>Gets the number of times a file was opened through <see cref="Source"/>.</summary>
    public int OpenCount { get; private set; }
    /// <summary>Gets the error of the last open attempt, or null when it succeeded.</summary>
    public Exception? OpenError { get; private set; }
    /// <summary>Gets the warnings reported by the last successful open.</summary>
    public IReadOnlyList<string> OpenWarnings { get; private set; } = [];
    /// <summary>Gets the toolbar style in effect after the toolbar adapts to the available width.</summary>
    public ToolbarStyle EffectiveToolbarStyle => toolbar?.EffectiveStyle ?? ToolbarStyle;
    /// <summary>Gets the commands of the catalog that the current mode allows.</summary>
    public IReadOnlyList<EditorCommand> AvailableCommands => Catalog.Where(command =>
        command.IsAvailable(Mode) && (AllowOpen || command.Id != "open")).ToArray();

    /// <summary>Occurs when <see cref="Mode"/> changes.</summary>
    public event EventHandler? ModeChanged;
    /// <summary>Occurs after the document changes in full mode.</summary>
    public event EventHandler? DocumentChanged;
    /// <summary>Occurs when opening <see cref="Source"/> finishes, whether it succeeded or failed.</summary>
    public event EventHandler? OpenCompleted;

    /// <summary>Gets the command catalog of the editor.</summary>
    protected abstract IReadOnlyList<EditorCommand> Catalog { get; }
    /// <summary>Builds the editor surface placed below the toolbar.</summary>
    /// <returns>The editor surface.</returns>
    protected abstract UIElement BuildEditor();
    /// <summary>Applies a new editor mode to the underlying session.</summary>
    /// <param name="mode">The new mode.</param>
    protected abstract void ApplyMode(EditorMode mode);
    /// <summary>Checks whether a command can run in the current state.</summary>
    /// <param name="id">The command identifier.</param>
    /// <returns>True when the command is enabled.</returns>
    protected abstract bool IsCommandEnabled(string id);
    /// <summary>Runs a command.</summary>
    /// <param name="id">The command identifier.</param>
    protected abstract void RunCommand(string id);
    /// <summary>Reads a document file on a background thread.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="stream">The open file stream.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The document and any warnings.</returns>
    protected abstract (object Document, IReadOnlyList<string> Warnings) ReadFile(string path, Stream stream, CancellationToken cancellation);
    /// <summary>Installs a document read by <see cref="ReadFile"/> on the UI thread.</summary>
    /// <param name="document">The document.</param>
    protected abstract void InstallDocument(object document);
    /// <summary>Gets whether the editor surface handles text keys itself, so command shortcuts do not intercept them.</summary>
    protected virtual bool HandlesTextEditing => false;
    /// <summary>Gets the contextual toolbar tab to show for the current selection, or null for none.</summary>
    protected virtual string? ContextualToolbarTab => null;
    private protected virtual IReadOnlyList<RibbonTabLayout> RibbonTabs => [];
    private protected virtual bool IsCommandToggled(string id) => false;
    private protected virtual string? ToolbarValue(string id) => null;
    private protected virtual IReadOnlyList<(string Label, string Value)> ToolbarChoices(string id) => [];
    private protected virtual void ApplyToolbarValue(string id, string value) { }

    IReadOnlyList<RibbonTabLayout> IToolbarState.RibbonTabs => RibbonTabs;
    bool IToolbarState.IsToggled(string id) => !disposed && IsCommandToggled(id);
    string? IToolbarState.Value(string id) => disposed ? null : ToolbarValue(id);
    IReadOnlyList<(string Label, string Value)> IToolbarState.Choices(string id) => ToolbarChoices(id);
    void IToolbarState.Apply(string id, string value)
    {
        if (disposed || openRequest is not null)
            return;
        ApplyToolbarValue(id, value);
        toolbar?.Refresh();
    }

    internal void InitializeSurface()
    {
        if (initialized || disposed)
            return;
        initialized = true;
        Material.Scope(this);
        var layout = new DockPanel();
        editorLayout = layout;
        Material.Paint(layout, Panel.BackgroundProperty, "Surface");
        toolbar = new(Catalog, id => ExecuteCommand(id), CanExecuteCommand, this);
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        var content = BuildEditor();
        layout.Children.Add(content);
        waiter = new Control { Style = Material.Style("GridWaiter"), Visibility = Visibility.Collapsed };
        var layer = new WpfGrid();
        layer.Children.Add(layout);
        layer.Children.Add(waiter);
        Content = layer;
        UpdateToolbar();
    }

    /// <summary>Checks whether a command is available and enabled.</summary>
    /// <param name="id">The command identifier.</param>
    /// <returns>True when the command can run.</returns>
    public bool CanExecuteCommand(string id) => !disposed && openRequest is null &&
        AvailableCommands.Any(command => command.Id == id) && IsCommandEnabled(id);

    /// <summary>Runs a command when it is available and enabled.</summary>
    /// <param name="id">The command identifier.</param>
    /// <returns>True when the command ran.</returns>
    public bool ExecuteCommand(string id)
    {
        if (!CanExecuteCommand(id))
            return false;
        RunCommand(id);
        toolbar?.Refresh();
        return true;
    }

    /// <summary>Refreshes the toolbar and raises <see cref="DocumentChanged"/> in full mode.</summary>
    protected void RaiseDocumentChanged()
    {
        toolbar?.Refresh();
        if (Mode == EditorMode.Full)
            DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Updates the contextual tab and the enabled and toggled state of the toolbar.</summary>
    protected void RefreshToolbar()
    {
        toolbar?.SetContext(ContextualToolbarTab);
        toolbar?.Refresh();
    }
    /// <summary>Creates a context menu with toolbar commands.</summary>
    /// <param name="ids">The identifiers of the commands to include.</param>
    /// <returns>The menu, or null before the toolbar exists.</returns>
    protected ContextMenu? CreateCommandMenu(params string[] ids) => toolbar?.Menu(AvailableCommands.Where(command => ids.Contains(command.Id)));

    /// <summary>Runs the command whose shortcut matches the pressed keys.</summary>
    /// <param name="args">The key event data.</param>
    protected override void OnPreviewKeyDown(KeyEventArgs args)
    {
        base.OnPreviewKeyDown(args);
        if (toolbar?.HandleKey(args) == true)
        {
            args.Handled = true;
            return;
        }
        if (args.Handled || HandlesTextEditing || args.OriginalSource is TextBox)
            return;
        var pressed = args.Key == Key.System ? args.SystemKey : args.Key;
        foreach (var command in AvailableCommands)
        {
            if (command.Shortcut is null || ParseShortcut(command.Shortcut) is not { } gesture)
                continue;
            if (Keyboard.Modifiers == gesture.Modifiers && gesture.Key == pressed && ExecuteCommand(command.Id))
            {
                args.Handled = true;
                return;
            }
        }
    }

    /// <summary>Reads a shortcut as the catalog displays it, such as "Ctrl+Shift+5", "Alt+=" or "Shift+F11".</summary>
    internal static (ModifierKeys Modifiers, Key Key)? ParseShortcut(string text)
    {
        var parts = text.Split('+');
        // "Ctrl++" names the plus key itself.
        if (text.EndsWith("++", StringComparison.Ordinal) || text == "+")
            parts = [.. text[..^1].TrimEnd('+').Split('+', StringSplitOptions.RemoveEmptyEntries), "+"];
        var modifiers = ModifierKeys.None;
        foreach (var part in parts[..^1])
            modifiers |= part switch
            {
                "Ctrl" => ModifierKeys.Control,
                "Shift" => ModifierKeys.Shift,
                "Alt" => ModifierKeys.Alt,
                _ => ModifierKeys.None
            };
        string name = parts[^1];
        Key key;
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
            key = Key.D0 + (name[0] - '0');
        else if (name is "=" or "+")
            key = Key.OemPlus;
        else if (name == "-")
            key = Key.OemMinus;
        else if (name.Length == 0 || char.IsAsciiDigit(name[0]) || !Enum.TryParse(name, true, out key))
            return null;
        return (modifiers, key);
    }

    private async void Activate()
    {
        if (!IsLoaded || !IsVisible || disposed)
            return;
        InitializeSurface();
        if (!sourcePending || string.IsNullOrWhiteSpace(Source))
            return;
        sourcePending = false;
        var request = new CancellationTokenSource();
        openRequest = request;
        var token = request.Token;
        var path = Source;
        OpenError = null;
        OpenWarnings = [];
        OpenCount++;
        waiter!.Visibility = Visibility.Visible;
        editorLayout!.IsEnabled = false;
        RefreshToolbar();
        try
        {
            var loaded = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return ReadFile(path, stream, token);
            }, token);
            if (!request.IsCancellationRequested && !disposed)
            {
                InstallDocument(loaded.Document);
                OpenWarnings = loaded.Warnings;
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or
            ArgumentException or System.Xml.XmlException)
        {
            if (!request.IsCancellationRequested)
                OpenError = error;
        }
        finally
        {
            if (ReferenceEquals(openRequest, request))
            {
                openRequest = null;
                waiter.Visibility = Visibility.Collapsed;
                editorLayout!.IsEnabled = true;
                RefreshToolbar();
                if (!disposed)
                    OpenCompleted?.Invoke(this, EventArgs.Empty);
            }
            request.Dispose();
        }
    }

    private static void SourceChanged(DependencyObject source, DependencyPropertyChangedEventArgs _)
    {
        var control = (EditorControl)source;
        control.openRequest?.Cancel();
        control.sourcePending = !string.IsNullOrWhiteSpace(control.Source);
        control.Activate();
    }

    private static void ModeUpdated(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        var control = (EditorControl)source;
        control.ApplyMode((EditorMode)args.NewValue);
        control.UpdateToolbar();
        control.ModeChanged?.Invoke(control, EventArgs.Empty);
    }

    private static void ToolbarUpdated(DependencyObject source, DependencyPropertyChangedEventArgs _) =>
        ((EditorControl)source).UpdateToolbar();

    private void UpdateToolbar()
    {
        toolbar?.SetContext(ContextualToolbarTab);
        toolbar?.Configure(Mode, ToolbarStyle, ShowToolbar, AllowOpen);
    }

    /// <summary>Cancels a pending file open and stops command handling.</summary>
    public virtual void Dispose()
    {
        disposed = true;
        openRequest?.Cancel();
    }
}
