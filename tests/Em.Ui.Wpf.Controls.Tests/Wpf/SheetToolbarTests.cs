using System.Windows;
using System.Windows.Threading;
using Em.Editing;
using Em.Controls.Sheet.Cells;
using Em.Ui.Wpf.Controls;
using Xunit;

namespace Em.Ui.Wpf.Controls.Tests.Wpf;

public sealed class SheetToolbarTests
{
    [Fact]
    public Task Switching_toolbar_keeps_workbook_selection_scroll_and_open_edit() => OnSta(() =>
    {
        using var control = new MatrixSheet();
        control.InitializeSurface();
        var workbook = control.Session.Workbook;
        var cell = new CellAddress(25, 4);
        control.Session.SetCell(cell, "5");
        control.ScrollTo(24, 3);
        Assert.True(control.BeginEdit(cell, "=2+3"));
        var commands = control.AvailableCommands.Select(command => command.Id).ToArray();

        control.ToolbarStyle = ToolbarStyle.Ribbon;
        Assert.Equal(ToolbarStyle.Ribbon, control.EffectiveToolbarStyle);
        Assert.Same(workbook, control.Session.Workbook);
        Assert.Equal(cell, control.Session.Selection.Start);
        Assert.Equal((24, 3), (control.FirstRow, control.FirstColumn));
        Assert.Equal(cell, control.EditCell);
        Assert.Equal("=2+3", control.EditText);
        Assert.Equal(commands, control.AvailableCommands.Select(command => command.Id));

        control.ToolbarStyle = ToolbarStyle.Compact;
        Assert.Equal("=2+3", control.EditText);
        control.Mode = EditorMode.Viewer;
        Assert.Null(control.EditCell);
        Assert.Equal("2+3", control.Session.ActiveSheet[cell].Formula);
        Assert.False(control.CanExecuteCommand("paste"));
        Assert.True(control.CanExecuteCommand("copy"));
        Assert.All(control.AvailableCommands, command => Assert.False(command.MutatesDocument));
    });

    [Fact]
    public Task Setting_source_on_an_unloaded_control_defers_open() => OnSta(() =>
    {
        using var control = new MatrixSheet { Source = "workbook.xlsx" };
        Assert.Equal(0, control.OpenCount);
        Assert.Equal(ToolbarStyle.Compact, control.ToolbarStyle);
    });

    internal static Task OnSta(Action check)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(() =>
            {
                try { check(); completion.SetResult(); }
                catch (Exception error) { completion.SetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
