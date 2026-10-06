using System.IO;
using Em.Ui.Wpf.Publish;

namespace Em.Ui.Wpf.Core.Tests
{
   public sealed class ContainerVersionTests
   {
      [Theory]
      [InlineData("0.1.0", 0, 1, 0, "release")]
      [InlineData("1.2.3-alpha.4", 1, 2, 3, "alpha")]
      [InlineData("0.1.0-prealpha.1", 0, 1, 0, "prealpha")]
      [InlineData("2.0.0-rc.12", 2, 0, 0, "rc")]
      public void TryParse_ReadsTheConventionTags(string tag, int major, int minor, int patch, string channel) {
         Assert.True(ContainerVersion.TryParse(tag, out var v));
         Assert.Equal(new ContainerVersion(major, minor, patch, channel), v);
      }

      [Theory]
      [InlineData("")]
      [InlineData("dev")]
      [InlineData("latest")]
      [InlineData("0.1")]
      [InlineData("0.1.0-nightly.1")]
      [InlineData("0.1.0-alpha")]
      [InlineData("0.1.0-release.1")]
      public void TryParse_RejectsCustomTags(string tag) {
         Assert.False(ContainerVersion.TryParse(tag, out var v));
         Assert.Equal(ContainerVersion.First, v);
      }

      [Fact]
      public void Tag_ReleaseIsTheBareVersionAndPrereleaseCarriesTheNumber() {
         Assert.Equal("1.2.3", new ContainerVersion(1, 2, 3, "release").Tag(9));
         Assert.Equal("1.2.3-beta.9", new ContainerVersion(1, 2, 3, "beta").Tag(9));
      }

      [Fact]
      public void NextNumber_StartsAtOneWhenTheChannelHasNoTag() {
         var v = new ContainerVersion(0, 1, 0, "alpha");

         Assert.Equal(1, v.NextNumber([]));
         Assert.Equal(1, v.NextNumber(["0.1.0-prealpha.7", "0.1.0-beta.2", "0.2.0-alpha.5", "latest"]));
      }

      [Fact]
      public void NextNumber_IsOneAboveTheHighestOfTheSameVersionAndChannel() {
         var v = new ContainerVersion(0, 1, 0, "alpha");

         Assert.Equal(11, v.NextNumber(["0.1.0-alpha.2", "0.1.0-alpha.10", "0.1.0-alpha.9", "0.1.0-alpha.x"]));
      }

      [Fact]
      public void NextNumber_RestartsWhenTheTargetVersionChanges() {
         Assert.Equal(1, new ContainerVersion(0, 2, 0, "alpha").NextNumber(["0.1.0-alpha.4"]));
      }

      // container-naming.md: a prerelease moves only its channel tag; release moves release, latest, A.B and A.
      [Theory]
      [InlineData("0.2.0-beta.3", "beta")]
      [InlineData("1.2.3", "release,latest,1.2,1")]
      [InlineData("dev", "")]
      public void FloatingTagsOf_FollowsTheChannel(string tag, string expected) {
         Assert.Equal(expected.Length == 0 ? [] : expected.Split(','), ContainerVersion.FloatingTagsOf(tag));
      }

      [Theory]
      [InlineData("dev")]
      [InlineData("hotfix-login")]
      public void ValidateManual_AcceptsFreeTags(string tag) {
         ContainerVersion.ValidateManual(tag);
      }

      [Theory]
      [InlineData("")]
      [InlineData("1.0.0")]
      [InlineData("0.2.0-beta.1")]
      [InlineData("latest")]
      [InlineData("alpha")]
      [InlineData("0.1")]
      [InlineData("bad tag")]
      public void ValidateManual_RejectsVersionFloatingAndInvalidTags(string tag) {
         Assert.Throws<InvalidDataException>(() => ContainerVersion.ValidateManual(tag));
      }
   }
}
