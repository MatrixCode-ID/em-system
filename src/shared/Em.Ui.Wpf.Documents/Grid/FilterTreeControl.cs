using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Em.Ui.Wpf.Documents.Themes;
using Em.Grid;
using Em.Grid.Query;

namespace Em.Ui.Wpf.Documents.Grid;

internal sealed class FilterTreeControl : StackPanel
{
    private Func<FilterExpression?> _read = null!;
    private readonly IReadOnlyList<GridColumn> _columns;

    public FilterTreeControl(IReadOnlyList<GridColumn> columns, FilterExpression? initial)
    {
        _columns = columns;
        Rebuild(initial);
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => NotifyChanged()));
        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => NotifyChanged()));
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => NotifyChanged()));
    }

    public event EventHandler? Changed;

    public void Clear()
    {
        Rebuild(null);
        NotifyChanged();
    }

    private void Rebuild(FilterExpression? initial)
    {
        Children.Clear();
        var (element, read) = CreateGroup(initial, 0);
        _read = read;
        Children.Add(element);
    }

    public FilterExpression? Expression() => _read();

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private (FrameworkElement Element, Func<FilterExpression?> Read) CreateGroup(FilterExpression? initial, int depth)
    {
        var actual = initial is NegatedFilter negated ? negated.Term : initial;
        var mode = new ComboBox { ItemsSource = new[] { "And", "Or" }, Width = 76,
            SelectedIndex = actual is AnyFilter ? 1 : 0 };
        Material.Paint(mode, Control.BackgroundProperty, "PrimaryContainer");
        Material.Paint(mode, Control.ForegroundProperty, "OnPrimaryContainer");
        var not = new CheckBox { Content = "NOT", IsChecked = initial is NegatedFilter, Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center };
        var list = new StackPanel { Margin = new Thickness(depth == 0 ? 0 : 28, 4, 0, 0) };
        var readers = new List<Func<FilterExpression?>>();
        var actions = new DockPanel { LastChildFill = false };
        var add = new Button { Content = "+ Condition", Margin = new Thickness(0, 0, 8, 0) };
        var group = new Button { Content = "+ Group", IsEnabled = depth < 8 };
        var addActions = new StackPanel { Orientation = Orientation.Horizontal };
        addActions.Children.Add(add);
        addActions.Children.Add(group);
        DockPanel.SetDock(addActions, Dock.Right);
        actions.Children.Add(mode);
        actions.Children.Add(not);
        actions.Children.Add(addActions);
        var panel = new StackPanel { Margin = new Thickness(2, 4, 2, 4) };
        panel.Children.Add(actions);
        panel.Children.Add(list);

        void Insert((FrameworkElement Element, Func<FilterExpression?> Read) item)
        {
            var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var remove = Material.IconButton("Close", "Remove filter term");
            remove.VerticalAlignment = VerticalAlignment.Top;
            remove.Margin = new Thickness(4, 4, 0, 0);
            DockPanel.SetDock(remove, Dock.Right);
            line.Children.Add(remove);
            line.Children.Add(item.Element);
            list.Children.Add(line);
            readers.Add(item.Read);
            remove.Click += (_, _) => { readers.Remove(item.Read); list.Children.Remove(line); NotifyChanged(); };
        }

        add.Click += (_, _) => { Insert(CreateCondition(null)); NotifyChanged(); };
        group.Click += (_, _) => { Insert(CreateGroup(null, depth + 1)); NotifyChanged(); };
        var terms = actual switch
        {
            AllFilters all => all.Terms,
            AnyFilter any => any.Terms,
            null => [],
            _ => ImmutableArray.Create(actual)
        };
        foreach (var child in terms)
        {
            var unwrapped = child is ColumnFilter filter ? filter.Term : child;
            Insert(unwrapped is FilterCondition or DatePeriodFilter
                ? CreateCondition(unwrapped)
                : CreateGroup(unwrapped, depth + 1));
        }
        if (readers.Count == 0)
            Insert(CreateCondition(null));

        return (panel, () =>
        {
            var items = readers.Select(read => read()).OfType<FilterExpression>().ToImmutableArray();
            if (items.Length == 0)
                return null;
            FilterExpression expression = items.Length == 1 ? items[0]
                : mode.SelectedIndex == 1 ? new AnyFilter(items) : new AllFilters(items);
            return not.IsChecked == true ? new NegatedFilter(expression) : expression;
        });
    }

    private (FrameworkElement Element, Func<FilterExpression?> Read) CreateCondition(FilterExpression? initial)
    {
        var filter = initial as FilterCondition;
        var date = initial as DatePeriodFilter;
        var column = new ComboBox
        {
            Width = 132, ItemsSource = _columns, DisplayMemberPath = nameof(GridColumn.Header),
            SelectedValuePath = nameof(GridColumn.Key), SelectedValue = filter?.ColumnKey ?? date?.ColumnKey ?? _columns[0].Key
        };
        var operation = new ComboBox { Width = 180, Margin = new Thickness(4, 0, 4, 0), DisplayMemberPath = nameof(ConditionOperator.Label),
            SelectedValuePath = nameof(ConditionOperator.Value) };
        var value = new TextBox { MinWidth = 86, VerticalContentAlignment = VerticalAlignment.Center };
        var upper = new TextBox { Width = 72, Margin = new Thickness(4, 0, 0, 0) };
        var formatted = new CheckBox { Content = "Display", Margin = new Thickness(6, 3, 0, 0), IsChecked = filter?.UseDisplayText == true };
        value.Text = filter?.Operator == FilterOperator.In
            ? string.Join(";", filter.Operands.Select(operand => operand.ToString()))
            : filter?.Operand.ToString() ?? (date?.Period == RelativeDatePeriod.LastDays ? date.Days.ToString(CultureInfo.InvariantCulture) : "");
        if (filter?.Operator == FilterOperator.Between && filter.Operands.Length > 0)
            upper.Text = filter.Operands[0].ToString();

        void ChooseOperator(object? requested)
        {
            var kind = column.SelectedItem is GridColumn spec ? spec.Kind : GridValueKind.Text;
            var ops = MatrixGrid.AvailableOperators(kind).Cast<object>();
            if (kind == GridValueKind.Date)
                ops = ops.Concat(Enum.GetValues<RelativeDatePeriod>().Cast<object>());
            operation.ItemsSource = ops.Select(op => new ConditionOperator(op,
                op is FilterOperator condition ? MatrixGrid.OperatorName(condition) : op.ToString() ?? string.Empty)).ToArray();
            operation.SelectedValue = requested ?? FilterOperator.Equal;
            if (operation.SelectedIndex < 0)
                operation.SelectedIndex = 0;
        }

        ChooseOperator(date is null ? filter?.Operator : date.Period);
        column.SelectionChanged += (_, _) => ChooseOperator(null);
        void SyncInputs()
        {
            upper.Visibility = operation.SelectedValue is FilterOperator.Between ? Visibility.Visible : Visibility.Collapsed;
            value.IsEnabled = operation.SelectedValue is not (FilterOperator.IsNull or FilterOperator.IsBlank)
                && (operation.SelectedValue is not RelativeDatePeriod period || period == RelativeDatePeriod.LastDays);
        }
        operation.SelectionChanged += (_, _) => SyncInputs();
        SyncInputs();
        var line = new DockPanel();
        foreach (var control in new FrameworkElement[] { column, operation, formatted, upper })
        {
            DockPanel.SetDock(control, HorizontalAlignmentFor(control));
            line.Children.Add(control);
        }
        line.Children.Add(value);

        return (line, () =>
        {
            if (column.SelectedItem is not GridColumn spec)
                return null;
            if (operation.SelectedValue is RelativeDatePeriod period)
            {
                int days = date?.Days ?? 7;
                if (period == RelativeDatePeriod.LastDays &&
                    (!int.TryParse(value.Text, out days) || days < 1))
                    throw new FormatException("Last days needs a positive number.");
                return new DatePeriodFilter(spec.Key, period, date?.ReferenceDate ?? DateOnly.FromDateTime(DateTime.Today),
                    date?.FirstDayOfWeek ?? CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek, days);
            }
            if (operation.SelectedValue is not FilterOperator op)
                return null;
            if (op is FilterOperator.IsNull or FilterOperator.IsBlank)
                return new FilterCondition(spec.Key, op);
            if (value.Text.Length == 0)
                return null;
            var kind = formatted.IsChecked == true ? GridValueKind.Text : spec.Kind;
            var operands = op switch
            {
                FilterOperator.Between => ImmutableArray.Create(Parse(upper.Text, kind, spec.Header)),
                FilterOperator.In => value.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(item => Parse(item, kind, spec.Header)).ToImmutableArray(),
                _ => []
            };
            return new FilterCondition(spec.Key, op, op == FilterOperator.In ? default : Parse(value.Text, kind, spec.Header))
            {
                Operands = operands,
                UseDisplayText = formatted.IsChecked == true,
                DisplayFormat = spec.Editor?.DisplayFormat, DisplayLabels = MatrixGrid.DisplayLabelsFor(spec),
                CultureName = CultureInfo.CurrentCulture.Name
            };
        });
    }

    private sealed record ConditionOperator(object Value, string Label);

    private static Dock HorizontalAlignmentFor(FrameworkElement control) => control is ComboBox ? Dock.Left : Dock.Right;

    private static GridValue Parse(string text, GridValueKind kind, string caption) => kind switch
    {
        GridValueKind.Number when decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var number) =>
            GridValue.From(number),
        GridValueKind.Date when DateOnly.TryParse(text, CultureInfo.CurrentCulture, out var date) => GridValue.From(date),
        GridValueKind.Boolean when bool.TryParse(text, out bool flag) => GridValue.From(flag),
        GridValueKind.Text or GridValueKind.Null => GridValue.From(text),
        _ => throw new FormatException($"Enter a valid {kind.ToString().ToLowerInvariant()} for {caption}.")
    };
}
