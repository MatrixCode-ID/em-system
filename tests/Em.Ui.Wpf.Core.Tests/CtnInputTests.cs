using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Core.Tests;

public sealed class CtnInputTests {
   [Theory]
   [InlineData("latest", true)]
   [InlineData("1.0.0", true)]
   [InlineData("1.0.0-beta.4", true)]
   [InlineData("stable_2026", true)]
   [InlineData("_x", true)]
   [InlineData("", false)]
   [InlineData("-rc", false)]
   [InlineData(".1", false)]
   [InlineData("1.0.0+build", false)]
   [InlineData("has space", false)]
   [InlineData("a/b", false)]
   [InlineData("sha256:abc", false)]
   public void IsValidTagFollowsOciTagRule(string tag, bool valid) => Assert.Equal(valid, CtnInput.IsValidTag(tag));

   [Fact]
   public void IsValidTagAllowsAtMost128Characters() {
      Assert.True(CtnInput.IsValidTag(new string('a', 128)));
      Assert.False(CtnInput.IsValidTag(new string('a', 129)));
   }
}
