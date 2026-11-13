using Em.Controls.Write.Editing;
using Em.Controls.Write.Model;
using Xunit;

namespace Em.Libs.Tests.Controls.Write;

public sealed class WriteUpstreamTests
{
    [Fact]
    public void Internal_paste_preserves_inline_images_from_both_documents()
    {
        var first = new WriteSession();
        first.InsertRun(new("", Image: new([1, 2, 3], "image/png", 32, 64)));
        first.SelectAll();
        var fragment = first.Copy();

        var second = new WriteSession();
        second.InsertRun(new("", Image: new([4, 5, 6], "image/png", 60, 80)));
        Assert.True(second.Paste(fragment).Accepted);
        var runs = second.GetParagraph(second.Selection.Caret)!.Runs;
        Assert.Equal(2, runs.Length);
        Assert.Equal(new byte[] { 4, 5, 6 }, runs[0].Image!.Bytes.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, runs[1].Image!.Bytes.ToArray());
    }
}
