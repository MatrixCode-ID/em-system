using Em.Editing;
using Em.Ui.Wpf.Documents.Editing;
using Em.Ui.Wpf.Documents.Writer;
using Em.Write.Editing;
using Em.Write.Model;
using Xunit;

namespace Em.Ui.Wpf.Documents.Tests.Wpf;

public sealed class WriteToolbarTests
{
    [Fact]
    public Task Switching_toolbar_keeps_document_selection_scroll_and_composition() => SheetToolbarTests.OnSta(() =>
    {
        using var editor = new MatrixWriter { Source = "deferred.docx" };
        Assert.Equal(0, editor.OpenCount);
        Assert.Equal(0, editor.LayoutCount);
        editor.InitializeSurface();
        editor.LoadDocument(WriteDocument.Create("Text"));
        editor.ScrollTo(144);
        var start = new WritePosition(0, 0, 1);
        var selection = new WriteSelection(start, start with { Offset = 3 });
        Assert.True(editor.Session.Select(selection));
        var document = editor.Session.Document;
        Assert.True(editor.BeginComposition("é"));
        var commands = editor.AvailableCommands.Select(command => command.Id).ToArray();

        editor.ToolbarStyle = ToolbarStyle.Ribbon;
        Assert.Same(document, editor.Session.Document);
        Assert.Equal(selection, editor.Session.Selection);
        Assert.Equal(144, editor.VerticalOffset);
        Assert.Equal("é", editor.CompositionText);
        Assert.Equal(commands, editor.AvailableCommands.Select(command => command.Id));
        editor.ToolbarStyle = ToolbarStyle.Compact;
        Assert.Equal("é", editor.CompositionText);

        editor.Mode = EditorMode.Viewer;
        Assert.Null(editor.CompositionText);
        Assert.Equal("Tét", editor.Session.GetParagraph(start)!.Text);
        Assert.False(editor.CanExecuteCommand("paste"));
        Assert.All(editor.AvailableCommands, command => Assert.False(command.MutatesDocument));
        editor.Mode = EditorMode.Full;
        Assert.True(editor.ExecuteCommand("undo"));
        Assert.Same(document, editor.Session.Document);
    });
}
