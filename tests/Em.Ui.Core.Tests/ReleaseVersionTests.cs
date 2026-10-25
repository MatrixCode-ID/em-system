using System.Security.Cryptography;
using System.Text;
using Em.Ui.Core.Release.Format;

namespace Em.Ui.Core.Tests;

public sealed class ReleaseVersionTests {
   [Theory] [InlineData(null)] [InlineData("1.2.0")] [InlineData("1.2.0-beta.10")]
   public void OptionalVersionRoundTrips(string? version) {
      var manifest = new ReleaseManifest { PublishedAtUtc = DateTime.UtcNow, Files = [], Version = version };
      var bytes = ReleaseManifestSerializer.Serialize(manifest);
      Assert.Equal(version, ReleaseManifestSerializer.Deserialize(bytes).Version);
      Assert.Equal(version is not null, Encoding.UTF8.GetString(bytes).Contains("\"version\""));
   }
   [Theory] [InlineData("1.2")] [InlineData("1.2.0+build")] [InlineData("1.2.0-dev")] [InlineData("1.2.0-beta.0")] [InlineData("01.2.0")]
   public void InvalidVersionRejectedByReaderAndWriter(string version) {
      Assert.Throws<ReleaseFormatException>(() => ReleaseManifestSerializer.Serialize(new() { PublishedAtUtc = DateTime.UtcNow, Files = [], Version = version }));
      var bytes = Encoding.UTF8.GetBytes("{\"publishedAtUtc\":\"2026-10-26T00:00:00Z\",\"files\":[],\"version\":\"" + version + "\"}");
      Assert.Throws<ReleaseFormatException>(() => ReleaseManifestSerializer.Deserialize(bytes));
   }
   [Theory] [InlineData("version-valid", true)] [InlineData("version-bad-format", false)]
   public async Task VersionVectorsHaveValidSignatures(string name, bool valid) {
      var root = SampleRoot(); using var key = ECDsa.Create(); key.ImportFromPem(File.ReadAllText(Path.Combine(root, "version-public-key.pem")));
      var folder = Path.Combine(root, name); var bytes = File.ReadAllBytes(Path.Combine(folder, "release.json"));
      Assert.True(ReleaseSignature.Verify(bytes, ReleaseManifestSerializer.DeserializeSignature(File.ReadAllBytes(Path.Combine(folder, "release.json.sig"))), [key.ExportSubjectPublicKeyInfo()]));
      if (!valid) { Assert.Throws<ReleaseFormatException>(() => ReleaseManifestSerializer.Deserialize(bytes)); return; }
      var manifest = ReleaseManifestSerializer.Deserialize(bytes); Assert.Equal("1.2.0-beta.3", manifest.Version);
      foreach (var file in manifest.Files) { var payload = await File.ReadAllBytesAsync(Path.Combine(folder, "binaries", file.Path)); Assert.Equal(file.Size, payload.LongLength); Assert.Equal(file.Sha256, Convert.ToHexStringLower(SHA256.HashData(payload))); }
   }
   private static string SampleRoot() {
      for (var folder = new DirectoryInfo(Directory.GetCurrentDirectory()); folder is not null; folder = folder.Parent) {
         var path = Path.Combine(folder.FullName, "doc", "release-format-samples"); if (Directory.Exists(path)) return path;
      }
      throw new DirectoryNotFoundException("Run tests from the engine repository.");
   }
}
