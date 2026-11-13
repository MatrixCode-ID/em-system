using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Em.Grid.Menus;
using Em.Ui.Wpf.Documents.Menus;

namespace Em.Ui.Wpf.Documents.Editors;

internal static class EditorMenu
{
    public static ContextMenu Attach(FrameworkElement target, TextBox? input, Func<bool> canClear, Action clear,
        Action<MenuOpeningEventArgs> onOpening, Func<string, ICommand?> resolve)
    {
        return MenuRenderer.Attach(target, () =>
        {
            ImmutableArray<MenuEntry>.Builder items = ImmutableArray.CreateBuilder<MenuEntry>();
            if (input is not null)
            {
                items.Add(new MenuCommand("cut", "Cut", "✂", "Ctrl+X", !input.IsReadOnly));
                items.Add(new MenuCommand("copy", "Copy", "Copy", "Ctrl+C"));
                items.Add(new MenuCommand("paste", "Paste", "TableRows", "Ctrl+V", !input.IsReadOnly));
                items.Add(new MenuSeparator());
                items.Add(new MenuCommand("select-all", "Select all", "Check", "Ctrl+A"));
            }
            items.Add(new MenuCommand("clear", "Clear", "Close", IsEnabled: canClear()));
            var opening = new MenuOpeningEventArgs(new MenuHit(MenuRegion.Editor), items.ToImmutable());
            onOpening(opening);
            return opening;
        }, id => resolve(id) ?? id switch
        {
            "cut" => ApplicationCommands.Cut,
            "copy" => ApplicationCommands.Copy,
            "paste" => ApplicationCommands.Paste,
            "select-all" => ApplicationCommands.SelectAll,
            "clear" => new ClearCommand(clear, canClear),
            _ => null
        });
    }

    private sealed class ClearCommand(Action clear, Func<bool> canClear) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => canClear();
        public void Execute(object? parameter) => clear();
    }
}
