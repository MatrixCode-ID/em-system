using System.IO;
using System.Text.Json.Nodes;
using Em.Ui.Wpf.Publish;

namespace Em.Ui.Wpf.Core.Tests
{
   public sealed class DockerDaemonConfigTests : IDisposable
   {
      private readonly string folder = Path.Combine(Path.GetTempPath(), "em-daemon-" + Guid.NewGuid().ToString("N"));
      private string Path_ => Path.Combine(folder, ".docker", "daemon.json");

      public void Dispose() {
         try {
            Directory.Delete(folder, true);
         } catch (IOException) {
         } catch (UnauthorizedAccessException) {
         }
      }

      [Fact]
      public void Add_CreatesTheFileWhenMissing() {
         var backup = DockerDaemonConfig.AddInsecureRegistry(Path_, "192.0.2.11:5132");

         Assert.Equal("", backup);
         var list = JsonNode.Parse(File.ReadAllText(Path_))!["insecure-registries"]!.AsArray();
         Assert.Equal(["192.0.2.11:5132"], list.Select(x => x!.GetValue<string>()));
      }

      [Fact]
      public void Add_KeepsOtherSettings_AppendsOnce_AndBacksUpTheOriginal() {
         Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);
         var original = "{\n // proxy for the office\n \"builder\": { \"gc\": { \"enabled\": true } },\n \"insecure-registries\": [ \"192.0.2.2:5101\", ],\n}";
         File.WriteAllText(Path_, original);

         var backup = DockerDaemonConfig.AddInsecureRegistry(Path_, "192.0.2.11:5132");

         Assert.StartsWith("daemon.json.em-backup-", backup);
         Assert.Equal(original, File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path_)!, backup!)));
         var root = JsonNode.Parse(File.ReadAllText(Path_))!;
         Assert.True(root["builder"]!["gc"]!["enabled"]!.GetValue<bool>());
         Assert.Equal(["192.0.2.2:5101", "192.0.2.11:5132"], root["insecure-registries"]!.AsArray().Select(x => x!.GetValue<string>()));
         Assert.True(DockerDaemonConfig.IsListed(Path_, "192.0.2.11:5132"));
         Assert.True(DockerDaemonConfig.IsListed(Path_, "192.0.2.2:5101"));

         var before = File.ReadAllText(Path_);
         Assert.Null(DockerDaemonConfig.AddInsecureRegistry(Path_, "192.0.2.11:5132".ToUpperInvariant()));
         Assert.Equal(before, File.ReadAllText(Path_));
      }

      [Theory]
      [InlineData("")]
      [InlineData("http://192.0.2.11:5132")]
      [InlineData("host/path")]
      [InlineData("host name")]
      [InlineData("host:99999999")]
      public void Add_RejectsAnythingButAHost(string host) {
         Assert.Throws<InvalidDataException>(() => DockerDaemonConfig.AddInsecureRegistry(Path_, host));
         Assert.False(File.Exists(Path_));
      }

      [Fact]
      public void Add_RefusesAFileItCannotRead_AndLeavesItAlone() {
         Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);
         File.WriteAllText(Path_, "{ not json");

         Assert.Throws<InvalidDataException>(() => DockerDaemonConfig.AddInsecureRegistry(Path_, "host:1"));

         Assert.Equal("{ not json", File.ReadAllText(Path_));
         Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Path_)!));
      }
   }
}
