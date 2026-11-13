using System.Windows;
using System.Windows.Controls;
using Em.Sheet.Cells;
using Em.Ui.Wpf.Documents.Editing;
using Em.Ui.Wpf.Documents.Sheet;
using Em.Ui.Wpf.Documents.Writer;
using Em.Write.Model;
using Xunit;

namespace Em.Ui.Wpf.Documents.Tests.Wpf;

public sealed class EditorEmbeddingTests
{
    [Fact]
    public Task Unloaded_tabs_defer_sources_and_keep_session_state() => SheetToolbarTests.OnSta(() =>
    {
        using var sheet = new MatrixSheet { Source = "pending.xlsx" };
        using var write = new MatrixWriter { Source = "pending.docx" };
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Content = sheet });
        tabs.Items.Add(new TabItem { Content = write });
        tabs.Measure(new Size(1000, 700));
        tabs.Arrange(new Rect(0, 0, 1000, 700));
        Assert.Null(sheet.Content);
        Assert.Null(write.Content);
        Assert.Equal(0, sheet.OpenCount);
        Assert.Equal(0, write.OpenCount);
        Assert.Equal(0, write.LayoutCount);

        var book = sheet.Session.Workbook;
        var address = new CellAddress(60, 4);
        sheet.Session.Select(new(address, address));
        sheet.ScrollTo(60, 4);
        var document = WriteDocument.Create("Draft");
        write.LoadDocument(document);
        tabs.SelectedIndex = 1;
        tabs.SelectedIndex = 0;

        Assert.Same(book, sheet.Session.Workbook);
        Assert.Equal(address, sheet.Session.Selection.Start);
        Assert.Equal((60, 4), (sheet.FirstRow, sheet.FirstColumn));
        Assert.Same(document, write.Session.Document);
        Assert.Equal(0, sheet.OpenCount);
        Assert.Equal(0, write.OpenCount);
        Assert.Equal(0, write.LayoutCount);
    });

    [Fact]
    public Task Ribbon_falls_back_below_360_DIP_without_replacing_editor_content() => SheetToolbarTests.OnSta(() =>
    {
        using var sheet = new MatrixSheet { ToolbarStyle = ToolbarStyle.Ribbon };
        sheet.InitializeSurface();
        var editor = sheet.Content;
        var line = new Size(340, 500);
        sheet.Measure(line);
        sheet.Arrange(new Rect(line));
        sheet.UpdateLayout();
        Assert.Equal(ToolbarStyle.Compact, sheet.EffectiveToolbarStyle);
        Assert.Same(editor, sheet.Content);
    });
}
