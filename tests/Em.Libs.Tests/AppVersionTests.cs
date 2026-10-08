using Em.Shared;

namespace Em.Libs.Tests
{
   public class AppVersionTests
   {
      [Theory]
      [InlineData("1.3.0-alpha.1", "1.3.0-alpha.1")]
      [InlineData("v1.3.0-alpha.1", "1.3.0-alpha.1")]
      [InlineData("V2.0.0", "2.0.0")]
      [InlineData("0.1.0-prealpha.1", "0.1.0-prealpha.1")]
      [InlineData(" 1.0.0-rc.12 ", "1.0.0-rc.12")]
      public void TryFromTag_ReadsVersionTags(string tag, string expected) {
         Assert.True(AppVersion.TryFromTag(tag, out var version));
         Assert.Equal(expected, version);
      }

      [Theory]
      [InlineData(null)]
      [InlineData("")]
      [InlineData("latest")]
      [InlineData("beta")]
      [InlineData("1.3")]
      [InlineData("v1.3-alpha")]
      [InlineData("01.2.3")]
      [InlineData("1.2.3-")]
      public void TryFromTag_RejectsTagsThatAreNotVersions(string? tag) {
         Assert.False(AppVersion.TryFromTag(tag, out var version));
         Assert.Equal("", version);
      }

      [Theory]
      [InlineData(null, true)]
      [InlineData("", true)]
      [InlineData("0.0.0-dev", true)]
      [InlineData("0.0.0-dev+a1b2c3", true)]
      [InlineData("1.0.0", false)]
      [InlineData("1.3.0-alpha.1+a1b2c3", false)]
      public void IsDevVersion_OnlyForTheDevMarker(string? version, bool expected) =>
         Assert.Equal(expected, AppVersion.IsDevVersion(version));

      [Theory]
      [InlineData("0.0.0-dev+a1b2c3", "dev")]
      [InlineData("1.3.0-alpha.1", "v1.3.0-alpha.1")]
      [InlineData("1.3.0-alpha.1+a1b2c3", "v1.3.0-alpha.1")]
      public void ToDisplay_ShowsVersionOrDev(string version, string expected) =>
         Assert.Equal(expected, AppVersion.ToDisplay(version));
   }
}
