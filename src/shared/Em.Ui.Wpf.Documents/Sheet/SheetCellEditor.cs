// Point mode and formula autocomplete follow GridCraft (crates/ui-egui/src/editor.rs, formula_bar.rs, grid/interact.rs @ c0ee120ac2d9b59630f5abf1151dd9b471fe2926), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Em.Sheet.Commands;
using Em.Sheet.Cells;
using Em.Sheet.Formulas;
using Em.Sheet.Functions;
using Em.Sheet.Model;
using Em.Ui.Wpf.Documents.Themes;

namespace Em.Ui.Wpf.Documents.Sheet;

/// <summary>
/// The editor over a cell, as in Excel. It takes the cell's font at the current zoom and grows to the right with its
/// text. Typing starts it in Enter mode, where an arrow commits and moves; F2 and a double-click start it in Edit mode,
/// where arrows move the caret, and F2 switches between the two. In a formula whose caret follows an operator, a
/// parenthesis, a separator or the equals sign, an arrow or a click on the sheet inserts a cell reference (point mode)
/// and Shift extends it. Typing a function name lists matching functions: Up and Down pick one and Tab inserts it with
/// its parenthesis. Inside a call, the function's arguments show with the current one in bold.
/// </summary>
internal sealed class SheetCellEditor : Border
{
    private const double Inset = 4;
    private readonly MatrixSheet owner;
    private readonly CellAddress cell;
    private readonly Popup popup;
    private readonly StackPanel choices;
    private readonly TextBlock detail;
    private readonly TextBlock hint;
    private IReadOnlyList<SheetFunctionInfo> matches = [];
    private int choice;
    private bool dismissed;
    private bool setting;
    private TextSpan? point;
    private CellAddress? pointCell, pointAnchor;

    internal SheetCellEditor(MatrixSheet owner, CellAddress cell, string text, bool enterMode)
    {
        this.owner = owner;
        this.cell = cell;
        EnterMode = enterMode;
        BorderThickness = new Thickness(2);
        Material.Paint(this, BorderBrushProperty, "Primary");
        Input = new TextBox
        {
            Style = Material.Style("BareInput"), BorderThickness = new Thickness(0), Text = text, AcceptsReturn = false,
            AcceptsTab = false, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden
        };
        Child = Input;
        Input.TextChanged += OnTextChanged;
        Input.SelectionChanged += (_, _) => { if (!setting) UpdateSuggestions(); };
        Input.PreviewKeyDown += OnKey;
        // A click in the editor places the caret in the text, as in Excel, where it also leaves Enter mode.
        Input.PreviewMouseLeftButtonDown += (_, _) => EnterMode = false;
        Input.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!Input.IsKeyboardFocusWithin)
                popup!.IsOpen = false;
        }));

        choices = new StackPanel();
        detail = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 280, FontSize = 12, Margin = new Thickness(8, 6, 8, 4) };
        Material.Paint(detail, TextBlock.ForegroundProperty, "OnSurfaceVariant");
        hint = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 360, FontSize = 12, Margin = new Thickness(8, 4, 8, 4) };
        var body = new StackPanel();
        body.Children.Add(choices);
        body.Children.Add(detail);
        body.Children.Add(hint);
        var card = Material.Panel(body, "SurfaceContainer", 8, new Thickness(4));
        card.Margin = new Thickness(0, 4, 10, 10);
        card.Focusable = false;
        popup = new Popup
        {
            PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = true, AllowsTransparency = true, Child = card,
            Focusable = false
        };
        popup.Opened += (_, _) => GridTheme.SetMode(card, GridTheme.GetMode(owner));
    }

    internal TextBox Input { get; }

    /// <summary>Enter mode: an arrow commits the edit and moves, or points at a cell in a formula.</summary>
    internal bool EnterMode { get; private set; }

    internal string Text => Input.Text;

    /// <summary>Whether an arrow or a click on the sheet inserts a reference at the caret.</summary>
    internal bool CanPoint => FormulaInput.CanPoint(Input.Text, Input.CaretIndex, point);

    /// <summary>The range the reference inserted last points at, while the caret is still after it.</summary>
    internal CellRange? PointedRange =>
        FormulaInput.PointAtCaret(Input.Text, Input.CaretIndex, point) && point is { } span &&
        CellRange.TryParse(Input.Text[span.Start..span.End], out var range) ? range : null;

    internal void Focus(Point? click)
    {
        Input.Focus();
        Input.CaretIndex = Input.Text.Length;
        if (click is not { } at)
            return;
        // The text is laid out once the editor is placed; then the caret goes where the double-click landed.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            Input.UpdateLayout();
            var local = owner.TranslateToInput(at, Input);
            int index = Input.GetCharacterIndexFromPoint(local, true);
            if (index < 0)
                return;
            var leading = Input.GetRectFromCharacterIndex(index);
            var trailing = Input.GetRectFromCharacterIndex(index, true);
            Input.CaretIndex = Math.Min(Input.Text.Length, local.X > (leading.Left + trailing.Left) / 2 ? index + 1 : index);
        }));
    }

    /// <summary>Takes the cell's font, colours and inset at the zoom, so the text does not move when editing starts.</summary>
    internal void ApplyStyle(SheetStyle style, double zoom, Brush? fill, Brush? ink)
    {
        Input.FontFamily = new FontFamily(style.FontFamily);
        Input.FontSize = Math.Clamp(style.FontSize * zoom, 1, 400);
        Input.FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal;
        Input.FontStyle = style.Italic ? FontStyles.Italic : FontStyles.Normal;
        Input.TextAlignment = style.Alignment switch
        {
            CellAlignment.Center => TextAlignment.Center, CellAlignment.End => TextAlignment.Right, _ => TextAlignment.Left
        };
        Input.VerticalContentAlignment = style.VerticalAlignment switch
        {
            CellVerticalAlignment.Top => VerticalAlignment.Top, CellVerticalAlignment.Bottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center
        };
        // The sheet draws text 8 units and the indent inside the cell; the border and the text box's own margin take Inset.
        double left = (8 + style.Indent * 4) * zoom;
        Input.Padding = new Thickness(Math.Max(0, left - Inset), 0, Math.Max(0, 8 * zoom - Inset), 0);
        if (fill is null)
            Material.Paint(this, BackgroundProperty, "SurfaceContainerLowest");
        else
            Background = fill;
        if (ink is null)
            Material.Paint(Input, Control.ForegroundProperty, "OnSurface");
        else
            Input.Foreground = ink;
    }

    /// <summary>The width the text needs, so the editor grows over the cells to its right as in Excel.</summary>
    internal double TextWidth()
    {
        var typeface = new Typeface(Input.FontFamily, Input.FontStyle, Input.FontWeight, Input.FontStretch);
        var formatted = new FormattedText(Input.Text + "W", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface,
            Input.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace + Input.Padding.Left + Input.Padding.Right + 2 * Inset;
    }

    /// <summary>Replaces the text from outside, the formula bar for one; the caret goes to the end.</summary>
    internal void SetText(string text)
    {
        if (Input.Text == text)
            return;
        Set(text, text.Length, null);
        pointCell = pointAnchor = null;
    }

    internal void Close() => popup.IsOpen = false;

    /// <summary>Points at a cell clicked on the sheet; with <paramref name="extend"/> the range grows from its anchor.</summary>
    internal void PointAt(CellAddress target, bool extend)
    {
        var anchor = extend && FormulaInput.PointAtCaret(Input.Text, Input.CaretIndex, point) ? pointAnchor ?? target : target;
        PointRange(anchor, target);
    }

    /// <summary>Moves the far end of the pointed range while the mouse drags over the sheet.</summary>
    internal void PointDrag(CellAddress target) => PointRange(pointAnchor ?? target, target);

    private void PointMove(int rows, int columns, bool extend)
    {
        var from = pointCell ?? cell;
        var to = SheetNavigation.Step(owner.Session.ActiveSheet, from, rows, columns);
        PointRange(extend ? pointAnchor ?? from : to, to);
        owner.Reveal(to);
    }

    private void PointRange(CellAddress anchor, CellAddress end)
    {
        var (text, caret, span) = FormulaInput.InsertReference(Input.Text, Input.CaretIndex, point, new CellRange(anchor, end).ToString());
        Set(text, caret, span);
        pointCell = end;
        pointAnchor = anchor;
        owner.EditTextChanged(text);
    }

    private void Set(string text, int caret, TextSpan? span)
    {
        setting = true;
        try
        {
            Input.Text = text;
            Input.CaretIndex = Math.Clamp(caret, 0, text.Length);
        }
        finally
        {
            setting = false;
        }
        point = span;
        UpdateSuggestions();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs args)
    {
        if (setting)
            return;
        // Typed text ends the pointed reference; typing an operator in Edit mode starts pointing, as in Excel.
        point = null;
        pointCell = pointAnchor = null;
        dismissed = false;
        if (args.Changes.Any(change => change.AddedLength > 0) && CanPoint)
            EnterMode = true;
        UpdateSuggestions();
        owner.EditTextChanged(Input.Text);
    }

    private void OnKey(object sender, KeyEventArgs args)
    {
        var key = args.Key == Key.System ? args.SystemKey : args.Key;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (matches.Count > 0 && popup.IsOpen)
        {
            switch (key)
            {
                case Key.Up or Key.Down:
                    choice = (choice + (key == Key.Down ? 1 : matches.Count - 1)) % matches.Count;
                    ShowChoices();
                    args.Handled = true;
                    return;
                case Key.Tab when !shift:
                    Accept(choice);
                    args.Handled = true;
                    return;
                case Key.Escape:
                    dismissed = true;
                    UpdateSuggestions();
                    args.Handled = true;
                    return;
            }
        }
        switch (key)
        {
            case Key.Enter:
                owner.CommitCellEdit(shift ? -1 : 1, 0);
                args.Handled = true;
                break;
            case Key.Tab:
                owner.CommitCellEdit(0, shift ? -1 : 1);
                args.Handled = true;
                break;
            case Key.Escape:
                owner.CancelCellEdit();
                args.Handled = true;
                break;
            case Key.F2:
                EnterMode = !EnterMode;
                owner.EditTextChanged(Input.Text);
                args.Handled = true;
                break;
            case Key.Up or Key.Down or Key.Left or Key.Right when EnterMode:
                var (rows, columns) = key switch { Key.Up => (-1, 0), Key.Down => (1, 0), Key.Left => (0, -1), _ => (0, 1) };
                if (CanPoint)
                    PointMove(rows, columns, shift);
                else
                    owner.CommitCellEdit(rows, columns);
                args.Handled = true;
                break;
        }
    }

    private void Accept(int index)
    {
        if (index < 0 || index >= matches.Count)
            return;
        var (text, caret) = FormulaInput.Accept(Input.Text, Input.CaretIndex, matches[index].Name);
        Set(text, caret, null);
        // The caret now follows the parenthesis, so arrows point at the first argument.
        EnterMode = true;
        Input.Focus();
        owner.EditTextChanged(text);
    }

    private void UpdateSuggestions()
    {
        if (!Input.IsKeyboardFocusWithin)
        {
            popup.IsOpen = false;
            return;
        }
        IReadOnlyList<SheetFunctionInfo> found = dismissed ? [] : FormulaInput.Complete(Input.Text, Input.CaretIndex);
        if (!found.Select(item => item.Name).SequenceEqual(matches.Select(item => item.Name)))
            choice = 0;
        matches = found;
        hint.Inlines.Clear();
        if (matches.Count == 0 && FormulaInput.CurrentFunction(Input.Text, Input.CaretIndex) is { } call &&
            SheetFunctionCatalog.Find(call.Name) is { } function)
            ShowHint(function.Signature, call.Argument);
        ShowChoices();
        popup.IsOpen = !dismissed && (matches.Count > 0 || hint.Inlines.Count > 0);
    }

    private void ShowChoices()
    {
        choices.Children.Clear();
        for (int i = 0; i < matches.Count; i++)
        {
            int index = i;
            var label = new TextBlock { Text = "ƒ  " + matches[i].Name, FontSize = 12.5, Margin = new Thickness(8, 3, 8, 3) };
            var row = new Border { Child = label, CornerRadius = new CornerRadius(4), MinWidth = 220, Cursor = Cursors.Hand };
            if (i == choice)
            {
                Material.Paint(row, BackgroundProperty, "SecondaryContainer");
                Material.Paint(label, TextBlock.ForegroundProperty, "OnSecondaryContainer");
            }
            else
                row.Background = Brushes.Transparent;
            row.MouseLeftButtonDown += (_, args) => { Accept(index); args.Handled = true; };
            choices.Children.Add(row);
        }
        detail.Text = choice < matches.Count ? matches[choice].Description : "";
        detail.Visibility = detail.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        choices.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        hint.Visibility = matches.Count == 0 && hint.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // NAME(argument1, [argument2], ...) with the argument the caret is on in bold; a repeating "..." stays current.
    private void ShowHint(string signature, int argument)
    {
        int open = signature.IndexOf('(');
        if (open < 0)
        {
            hint.Inlines.Add(new Run(signature));
            return;
        }
        hint.Inlines.Add(new Run(signature[..(open + 1)]));
        var parts = signature[(open + 1)..].TrimEnd(')').Split(", ");
        for (int i = 0; i < parts.Length; i++)
        {
            bool current = i == Math.Min(argument, parts.Length - 1) || parts[i].Contains("...", StringComparison.Ordinal) && argument >= i;
            hint.Inlines.Add(new Run(parts[i]) { FontWeight = current ? FontWeights.Bold : FontWeights.Normal });
            if (i + 1 < parts.Length)
                hint.Inlines.Add(new Run(", "));
        }
        hint.Inlines.Add(new Run(")"));
    }
}
