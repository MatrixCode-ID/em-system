using Em.Editing;
using Xunit;

namespace Em.Libs.Tests.Editing;

public sealed class EditorContractTests
{
    [Fact]
    public void Viewer_catalog_rejects_mutating_commands()
    {
        var copy = new EditorCommand("copy", "Copy", "Copy", "Clipboard", "View");
        var paste = new EditorCommand("paste", "Paste", "Paste", "Clipboard", "Home", "Ctrl+V", true);
        Assert.True(copy.IsAvailable(EditorMode.Viewer));
        Assert.False(paste.IsAvailable(EditorMode.Viewer));
        Assert.True(paste.IsAvailable(EditorMode.Full));
    }
}
