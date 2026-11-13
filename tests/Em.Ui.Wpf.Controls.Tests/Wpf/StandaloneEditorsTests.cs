using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Em.Editors;
using Em.Controls.Grid.Menus;
using Em.Ui.Wpf.Controls;
using Xunit;

namespace Em.Ui.Wpf.Controls.Tests.Wpf;

[Collection("WPF desktop")]
public sealed class StandaloneEditorsTests
{
    [Fact]
    public async Task Factory_editors_render_in_a_form_and_use_the_shared_validation_pipeline()
    {
        await OnDesktop(async () =>
        {
            var editors = new IGridEditor[]
            {
                EditorFactory.Create(new NumericSettings { Minimum = 1, Maximum = 10 }),
                EditorFactory.Create(new CheckSettings { AllowNull = true }),
                EditorFactory.Create(new DateSettings { EditFormat = "yyyy-MM-dd", Minimum = new(2026, 1, 1) }),
                EditorFactory.Create(new ComboSettings { Options = [new(7, "Express"), new(2, "Regular")] }),
                EditorFactory.Create(new LookupSettings
                {
                    AsyncSource = (query, _, _, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        IReadOnlyList<EditorOption> items = query == "west" ? [new(9, "Warehouse", ["West"])] : [];
                        return ValueTask.FromResult(items);
                    }
                })
            };

            var panel = new StackPanel();
            foreach (var editor in editors)
                panel.Children.Add(editor.Element);
            var window = new Window { Content = panel, Width = 480, Height = 320, ShowInTaskbar = false };
            GridTheme.SetMode(window, GridThemeMode.Dark);
            window.Show();
            window.Activate();

            try
            {
                foreach (var editor in editors)
                    Assert.NotNull(editor.Element.Template);

                var host = new Host();
                var spin = editors[0];
                spin.Host = host;
                var numericInput = Descendant<TextBox>(spin.Element);
                numericInput.Text = "11";
                Assert.False(spin.Commit());
                Assert.Empty(host.Commits);
                Assert.NotNull(spin.Error);
                numericInput.Text = "5";
                Assert.True(spin.Commit());
                Assert.Equal(5m, Assert.Single(host.Commits));
                Assert.True(spin.FocusEditor());
                Press(spin.Element, Key.Up);
                Assert.Equal(6m, spin.EditValue);
                Press(spin.Element, Key.Escape);
                Assert.True(host.Cancelled);

                var check = editors[1];
                check.EditValue = null;
                Assert.Null(Descendant<CheckBox>(check.Element).IsChecked);
                check.EditValue = true;
                Assert.True(Descendant<CheckBox>(check.Element).IsChecked);
                Assert.True(check.Commit());

                var date = editors[2];
                var dateInput = Descendant<TextBox>(date.Element);
                dateInput.Text = "2025-12-31";
                Assert.False(date.Commit());
                dateInput.Text = "2026-11-12";
                Assert.True(date.Commit());
                Assert.Equal(new DateOnly(2026, 11, 12), date.EditValue);

                var combo = editors[3];
                var choices = Descendant<ComboBox>(combo.Element);
                choices.SelectedIndex = 0;
                Assert.Equal(7, combo.EditValue);
                Assert.True(combo.Commit());

                var lookup = editors[4];
                Press(lookup.Element, Key.F4);
                var popup = (Popup)((DockPanel)((ContentPresenter)lookup.Element.Template.FindName("PART_Host", lookup.Element)).Content).Children[0];
                Assert.True(popup.IsOpen);
                var search = Descendant<TextBox>(popup.Child);
                search.Text = "west";
                var list = Descendant<ListView>(popup.Child);
                for (int attempt = 0; attempt < 40 && list.Items.Count == 0; attempt++)
                    await Task.Delay(25);
                Assert.Single(list.Items);
                Assert.Equal(2, Assert.IsType<GridView>(list.View).Columns.Count);
                list.SelectedIndex = 0;
                RaiseClick(Descendant<Button>(popup.Child, b => AutomationProperties.GetName(b) == "Select choice"));
                Assert.Equal(9, lookup.EditValue);
                Assert.False(popup.IsOpen);

                var menu = Assert.IsType<ContextMenu>(numericInput.ContextMenu);
                var spinControl = Assert.IsType<SpinEditor>(spin.Element);
                spinControl.EditorMenuOpening += (_, opening) =>
                {
                    Assert.Equal(MenuRegion.Editor, opening.Hit.Region);
                    Assert.Contains(opening.Items, item => item is MenuCommand { CommandId: "copy" });
                    opening.Items = [new MenuCommand("custom", "Custom")];
                };
                menu.IsOpen = true;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal("Custom", Assert.IsType<MenuItem>(Assert.Single(menu.Items)).Header);
                menu.IsOpen = false;
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void Press(UIElement element, Key key)
    {
        var source = PresentationSource.FromVisual(element) ?? throw new InvalidOperationException("No presentation source.");
        element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        });
    }

    private static void RaiseClick(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static T Descendant<T>(DependencyObject root, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        if (Find(root) is { } match)
            return match;
        throw new InvalidOperationException($"No {typeof(T).Name} was found.");

        T? Find(DependencyObject current)
        {
            if (current is T item && (predicate is null || predicate(item)))
                return item;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
                if (Find(VisualTreeHelper.GetChild(current, index)) is { } child)
                    return child;
            return null;
        }
    }

    internal static async Task OnDesktop(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await action();
                    completion.SetResult();
                }
                catch (Exception error) { completion.SetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private sealed class Host : IInPlaceEditorHost
    {
        public List<object?> Commits { get; } = [];
        public bool Cancelled { get; private set; }

        public void Commit(object? value) => Commits.Add(value);
        public void Cancel() => Cancelled = true;
        public bool Navigate(Key key, ModifierKeys modifiers) => true;
    }
}
