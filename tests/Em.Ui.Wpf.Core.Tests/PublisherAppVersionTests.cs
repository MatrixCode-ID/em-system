using Em.Ui.Wpf.Publish;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Core.Tests
{
   public sealed class PublisherAppVersionTests
   {
      [Fact]
      public void WithAppVersion_AddsTheVersionTag() {
         var args = Publisher.WithAppVersion([new() { Key = "BUILD_CONFIGURATION", Value = "Release" }], "1.3.0-alpha.1");
         Assert.Equal(2, args.Count);
         Assert.Equal(Publisher.AppVersionBuildArg, args[1].Key);
         Assert.Equal("1.3.0-alpha.1", args[1].Value);
      }

      [Theory]
      [InlineData("latest")]
      [InlineData("")]
      public void WithAppVersion_SkipsTagsThatAreNotVersions(string tag) =>
         Assert.Empty(Publisher.WithAppVersion([], tag));

      [Fact]
      public void WithAppVersion_KeepsTheProfileValue() {
         var args = Publisher.WithAppVersion([new() { Key = Publisher.AppVersionBuildArg, Value = "9.9.9" }], "1.0.0");
         Assert.Equal("9.9.9", Assert.Single(args).Value);
      }

      [Fact]
      public void ComposeBuilds_GroupsServicesByVersion() {
         var builds = Publisher.ComposeBuilds([
            new() { Service = "api", VersionTag = "1.3.0" },
            new() { Service = "worker", VersionTag = "1.3.0" },
            new() { Service = "proxy", VersionTag = "latest" },
         ]);
         Assert.Equal(2, builds.Count);
         Assert.Equal(["--build-arg", "APP_VERSION=1.3.0", "api", "worker"], builds[0]);
         Assert.Equal(["proxy"], builds[1]);
      }

      [Theory]
      [InlineData("0.0.0-dev+a1b2c3", "dev", true)]
      [InlineData("1.3.0-alpha.1+a1b2c3", "v1.3.0-alpha.1", false)]
      public void StatusVersionItem_ShowsVersionOrDev(string informational, string text, bool isDev) {
         var item = new StatusVersionItem(informational);
         Assert.Equal(text, item.Text);
         Assert.Equal(isDev, item.IsDev);
         Assert.Contains(informational, item.ToolTip);
      }
   }
}
