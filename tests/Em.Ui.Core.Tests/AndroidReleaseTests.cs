using System.Net;
using System.Security.Cryptography;
using System.Text;
using Em.Ui.Core.Release.Format;
using Em.Ui.Core.Release.Update;

namespace Em.Ui.Core.Tests;

public sealed class AndroidReleaseTests {
   private static readonly byte[] Apk = Encoding.UTF8.GetBytes("sample apk");
   private static ReleaseManifest Manifest(int code = 12, int? minimum = 10, string id = "com.contoso.inventory") => new() {
      PublishedAtUtc = DateTime.UtcNow, Files = [new() { Path = "inventory.apk", Size = Apk.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(Apk)) }],
      Android = new() { FileName = "inventory.apk", ApplicationId = id, VersionCode = code, VersionName = "1.2.0-alpha.1+build", MinVersionCode = minimum }
   };
   [Fact]
   public void AndroidRoundTrip() {
      var original = Manifest(); var read = ReleaseManifestSerializer.Deserialize(ReleaseManifestSerializer.Serialize(original));
      Assert.Equal(original.Android, read.Android); Assert.Equal(original.Files[0].Sha256, read.Files[0].Sha256);
   }
   [Theory]
   [InlineData("0.0.0", true)] [InlineData("1.2.3-alpha.1+abc", true)] [InlineData("1.0.0-01", false)]
   [InlineData("01.0.0", false)] [InlineData("1.2", false)] [InlineData("1.2.3-rc.0", true)]
   public void SemanticVersions(string version, bool valid) => Assert.Equal(valid, ReleaseManifestSerializer.IsSemanticVersion(version));
   [Theory]
   [InlineData("fileName", "\"missing.apk\"")] [InlineData("fileName", "\"nested/inventory.apk\"")]
   [InlineData("applicationId", "\"Com.Inventory\"")] [InlineData("applicationId", "\"inventory\"")]
   [InlineData("versionCode", "0")] [InlineData("versionCode", "2100000001")] [InlineData("versionCode", "1.5")]
   [InlineData("versionCode", "\"12\"")] [InlineData("minVersionCode", "null")]
   [InlineData("minVersionCode", "0")] [InlineData("minVersionCode", "13")] [InlineData("versionName", "\"1.0\"")]
   public void InvalidMetadataRejected(string field, string value) {
      var json = System.Text.Json.Nodes.JsonNode.Parse(ReleaseManifestSerializer.Serialize(Manifest()))!;
      json["android"]![field] = System.Text.Json.Nodes.JsonNode.Parse(value);
      Assert.Throws<ReleaseFormatException>(() => ReleaseManifestSerializer.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString())));
   }
   [Fact]
   public void LauncherAndAndroidAreExclusive() {
      var m = Manifest();
      Assert.Throws<ReleaseFormatException>(() => ReleaseManifestSerializer.Serialize(new() { PublishedAtUtc = m.PublishedAtUtc, Files = m.Files,
         Android = m.Android, Launcher = new() { FileName = "inventory.exe", Version = "1.0.0", InputHash = new string('a',64) } }));
   }
   [Theory]
   [InlineData(12, 10, 12, ReleaseUpdateStatus.UpToDate)] [InlineData(12, 10, 13, ReleaseUpdateStatus.UpToDate)]
   [InlineData(12, 10, 10, ReleaseUpdateStatus.Optional)] [InlineData(12, 10, 9, ReleaseUpdateStatus.Mandatory)]
   [InlineData(12, null, 1, ReleaseUpdateStatus.Optional)]
   public async Task UpdateStatusUsesSignedCodes(int remote, int? minimum, long installed, ReleaseUpdateStatus expected) {
      using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
      var bytes = ReleaseManifestSerializer.Serialize(Manifest(remote, minimum));
      using var client = Client(bytes, ReleaseManifestSerializer.SerializeSignature(ReleaseSignature.Sign(bytes,key)));
      var result = await new ReleaseUpdateChecker(client).CheckAsync(new(new("https://example.test/release/"),key.ExportSubjectPublicKeyInfo()),"com.contoso.inventory",installed);
      Assert.Equal(expected,result.Status);
   }
   [Theory]
   [InlineData("signature")] [InlineData("key")] [InlineData("missing")] [InlineData("identity")] [InlineData("malformed")]
   public async Task UntrustedOrIncompatibleReleaseFails(string mutation) {
      using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
      var m = Manifest(id: mutation == "identity" ? "com.contoso.other" : "com.contoso.inventory");
      var bytes = mutation == "missing" ? ReleaseManifestSerializer.Serialize(new() { PublishedAtUtc = m.PublishedAtUtc, Files=m.Files }) : ReleaseManifestSerializer.Serialize(m);
      if (mutation == "malformed") bytes = Encoding.UTF8.GetBytes("{}");
      var signature = ReleaseManifestSerializer.SerializeSignature(ReleaseSignature.Sign(bytes, mutation == "key" ? other : key));
      if (mutation == "signature") bytes[0] ^= 1;
      using var client = Client(bytes,signature);
      Assert.Equal(ReleaseUpdateStatus.Failed,(await new ReleaseUpdateChecker(client).CheckAsync(new(new("https://example.test/release/"),key.ExportSubjectPublicKeyInfo()),"com.contoso.inventory",1)).Status);
   }
   [Fact]
   public async Task NoSourceIsNotConfigured() { using var client = Client([],[]); Assert.Equal(ReleaseUpdateStatus.NotConfigured,(await new ReleaseUpdateChecker(client).CheckAsync(null,"x",1)).Status); }
   [Fact]
   public async Task TimeoutFailsWithinFiveSeconds() {
      using var client = new HttpClient(new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite,token); return new(HttpStatusCode.OK); }));
      Assert.Equal(ReleaseUpdateStatus.Failed,(await new ReleaseUpdateChecker(client).CheckAsync(new(new("https://example.test/"),[]),"com.contoso.inventory",1)).Status);
   }
   [Theory] [InlineData(false)] [InlineData(true)]
   public async Task DownloadChecksBytesAndDeletesCorruption(bool corrupt) {
      var file = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".apk");
      using var client = new HttpClient(new Handler((_,_) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new ByteArrayContent(corrupt ? Encoding.UTF8.GetBytes("corruption") : Apk) })));
      try {
         var checker = new ReleaseUpdateChecker(client);
         if (corrupt) { await Assert.ThrowsAsync<IOException>(() => checker.DownloadAsync(new(new("https://example.test/"),[]),Manifest(),file,null,default)); Assert.False(File.Exists(file)); }
         else { await checker.DownloadAsync(new(new("https://example.test/"),[]),Manifest(),file,null,default); Assert.Equal(Apk,File.ReadAllBytes(file)); }
      } finally { if (File.Exists(file)) File.Delete(file); }
   }
   [Theory] [InlineData("android-valid",true)] [InlineData("android-missing-object",true)] [InlineData("android-bad-file",false)] [InlineData("android-and-launcher",false)]
   public void AndroidSampleVectors(string name,bool valid) {
      var root = SampleRoot(); using var key = ECDsa.Create(); key.ImportFromPem(File.ReadAllText(Path.Combine(root,"android-public-key.pem")));
      var bytes = File.ReadAllBytes(Path.Combine(root,name,"release.json"));
      Assert.True(ReleaseSignature.Verify(bytes,ReleaseManifestSerializer.DeserializeSignature(File.ReadAllBytes(Path.Combine(root,name,"release.json.sig"))),[key.ExportSubjectPublicKeyInfo()]));
      if (valid) { var manifest=ReleaseManifestSerializer.Deserialize(bytes); Assert.Equal(name=="android-valid",manifest.Android is not null); }
      else Assert.Throws<ReleaseFormatException>(() => ReleaseManifestSerializer.Deserialize(bytes));
   }
   [Theory] [InlineData("valid", ReleaseSignatureStatus.Valid)] [InlineData("tampered-file",ReleaseSignatureStatus.Valid)]
   [InlineData("tampered-manifest",ReleaseSignatureStatus.Invalid)] [InlineData("unknown-key",ReleaseSignatureStatus.UnknownKey)]
   public void OriginalVectorsKeepTheirSignatures(string name,ReleaseSignatureStatus expected) {
      var root=SampleRoot(); using var key=ECDsa.Create(); key.ImportFromPem(File.ReadAllText(Path.Combine(root,"public-key.pem")));
      Assert.Equal(expected,ReleaseSignature.Check(File.ReadAllBytes(Path.Combine(root,name,"release.json")),ReleaseManifestSerializer.DeserializeSignature(File.ReadAllBytes(Path.Combine(root,name,"release.json.sig"))),[key.ExportSubjectPublicKeyInfo()]));
   }
   private static string SampleRoot() {
      for (var folder=new DirectoryInfo(Directory.GetCurrentDirectory()); folder is not null; folder=folder.Parent) {
         var path=Path.Combine(folder.FullName,"doc","release-format-samples"); if (Directory.Exists(path)) return path;
      }
      throw new DirectoryNotFoundException("Run tests from the engine repository.");
   }
   private static HttpClient Client(byte[] manifest,byte[] signature) => new(new Handler((request,_) => {
      Assert.True(request.Headers.CacheControl!.NoCache); Assert.True(request.Headers.CacheControl.NoStore);
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new ByteArrayContent(request.RequestUri!.AbsolutePath.EndsWith(".sig") ? signature : manifest) });
   }));
   private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler {
      protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) => send(request,cancellationToken);
   }
}
