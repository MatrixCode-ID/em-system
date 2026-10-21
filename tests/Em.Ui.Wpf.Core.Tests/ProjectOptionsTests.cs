using Em.Ui.Wpf.Publish;

namespace Em.Ui.Wpf.Core.Tests
{
   public sealed class ProjectOptionsTests
   {
      [Fact]
      public void From_EmptyProject_OffersDefaultConfigurationsAndLinuxRuntimes() {
         var options = ProjectOptions.From("", ";", "");

         Assert.Equal(["Debug", "Release"], options.Configurations);
         Assert.Equal(ProjectOptions.LinuxRuntimes, options.Runtimes);
         Assert.Empty(options.Frameworks);
      }

      [Fact]
      public void From_DeclaredValues_ComeFirstWithoutDuplicates() {
         var options = ProjectOptions.From("Debug;Release;Staging", "win-x64;linux-x64;", "net10.0; net9.0");

         Assert.Equal(["Debug", "Release", "Staging"], options.Configurations);
         Assert.Equal(["win-x64", "linux-x64", "linux-arm64", "linux-musl-x64", "linux-musl-arm64"], options.Runtimes);
         Assert.Equal(["net10.0", "net9.0"], options.Frameworks);
      }
   }
}
