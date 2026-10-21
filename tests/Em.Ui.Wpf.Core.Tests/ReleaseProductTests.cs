using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Em.Ui.Wpf.Core.Release;
using Em.Ui.Wpf.Windows;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Core.Tests;

public sealed class ReleaseProductTests : IDisposable {
   private readonly string _root = Path.Combine(Path.GetTempPath(), "EmReleaseTests-" + Guid.NewGuid().ToString("N"));
   public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
   private ReleaseProfile Profile() => new() { Name = "Contoso Inventory", PrivateKeyPem = ReleasePemKey.Create(), Product = new() { AppId = "Contoso.Inventory", AppName = "Inventory", Publisher = "Contoso", AppExe = "Inventory.exe" } };

   [Fact]
   public void SingleJsonRoundTripsPemAndIgnoresLegacyDirectories() {
      var store = new ReleaseProfileStore(_root); var p = Profile(); store.Save(p);
      Assert.Single(Directory.GetFiles(_root)); Assert.Empty(Directory.GetDirectories(_root));
      var export = Path.Combine(_root, "export.json"); store.Export(p.Id, export);
      var target = new ReleaseProfileStore(Path.Combine(_root, "other")); var imported = target.Import(export, false);
      Assert.Equal(p.PrivateKeyPem, imported.PrivateKeyPem); Assert.Equal(p.Product.AppId, imported.Product.AppId);
      using var doc = JsonDocument.Parse(File.ReadAllText(export)); Assert.False(doc.RootElement.TryGetProperty("signing", out _));
      Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N")));
      Assert.Single(store.List());
      Assert.Throws<InvalidDataException>(() => ReleaseProfile.FromJson("{\"formatVersion\":1}"));
   }

   [Fact]
   public void PemUsesP256AndRejectsPublicOrWrongCurveKeys() {
      var p = Profile(); using var key = ReleasePemKey.Open(p.PrivateKeyPem);
      Assert.Equal(256, key.KeySize); Assert.StartsWith("-----BEGIN PUBLIC KEY-----", ReleasePemKey.Public(p.PrivateKeyPem));
      Assert.Throws<CryptographicException>(() => ReleasePemKey.Open(ReleasePemKey.Public(p.PrivateKeyPem)));
      using var wrong = ECDsa.Create(ECCurve.NamedCurves.nistP384);
      Assert.Throws<CryptographicException>(() => ReleasePemKey.Open(wrong.ExportPkcs8PrivateKeyPem()));
   }

   [Theory]
   [InlineData("beta", "1.0.0-beta.8", "different", "1.0.0-beta.9")]
   [InlineData("rc", "1.0.0-beta.8", "different", "1.0.0-rc.1")]
   [InlineData("release", "0.9.0", "different", "1.0.0")]
   [InlineData("beta", "1.0.0-beta.8", "same", "1.0.0-beta.8")]
   public void LauncherVersionComesFromTarget(string channel, string version, string remoteHash, string expected) =>
      Assert.Equal(expected, LauncherBuild.SelectVersion(new ReleaseLauncher { BaseVersion = "1.0.0", Channel = channel }, "same", new() { Version = version, InputHash = remoteHash }));

   [Fact]
   public void StableVersionCannotBeReusedForChangedInputs() => Assert.Throws<InvalidDataException>(() =>
      LauncherBuild.SelectVersion(new ReleaseLauncher { BaseVersion = "1.0.0", Channel = "release" }, "new", new() { Version = "1.0.0", InputHash = "old" }));

   [Fact]
   public void InputHashUsesContentsAndSettingsNotTimestampsOrBuildOutputs() {
      Directory.CreateDirectory(Path.Combine(_root, "src")); Directory.CreateDirectory(Path.Combine(_root, ".cargo"));
      var files = new[] { "build.rs", "Cargo.toml", "Cargo.lock", "launcher.manifest", "build-dist.ps1", "rust-toolchain.toml", ".cargo/config.toml", "src/main.rs" };
      foreach (var file in files) File.WriteAllText(Path.Combine(_root, file), "same");
      var p = Profile(); var initial = LauncherBuild.InputHash(_root, p, "https://example.test/release");
      File.SetLastWriteTimeUtc(Path.Combine(_root, "src/main.rs"), DateTime.UtcNow.AddYears(-1));
      Directory.CreateDirectory(Path.Combine(_root, "target")); File.WriteAllText(Path.Combine(_root, "target/output"), "ignored");
      Assert.Equal(initial, LauncherBuild.InputHash(_root, p, "https://example.test/release"));
      File.WriteAllText(Path.Combine(_root, "src/main.rs"), "changed"); Assert.NotEqual(initial, LauncherBuild.InputHash(_root, p, "https://example.test/release"));
      File.WriteAllText(Path.Combine(_root, "src/main.rs"), "same"); p.Launcher.UpdateBehavior = "ask";
      Assert.NotEqual(initial, LauncherBuild.InputHash(_root, p, "https://example.test/release"));
   }

   [Fact]
   public async Task MsbuildIdentityReadsEvaluatedImportsAndAcceptsBasicHost() {
      Directory.CreateDirectory(_root);
      File.WriteAllText(Path.Combine(_root, "identity.props"), "<Project><PropertyGroup><Product>Contoso Inventory</Product><Company>Contoso</Company><EmAppId>Contoso.Inventory</EmAppId></PropertyGroup></Project>");
      var project = Path.Combine(_root, "Inventory.csproj");
      File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"identity.props\"/><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>");
      var product = await ReleaseProduct.ReadAsync(project, TestContext.Current.CancellationToken);
      Assert.Equal("Contoso.Inventory", product.AppId); Assert.Equal("Contoso Inventory", product.AppName); Assert.Equal("Inventory.exe", product.AppExe); Assert.False(product.IsEmHost);
   }

   [Fact]
   public void HostMetadataAndRegistryFactoryUseAppIdWithLegacyFallback() {
      var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("IdentityFixture"), AssemblyBuilderAccess.Run);
      assembly.SetCustomAttribute(new CustomAttributeBuilder(typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!, ["EmAppId", "Contoso.Inventory"]));
      Assert.Equal("Contoso.Inventory", HostIdentity.Metadata(assembly, "EmAppId"));
      Assert.Equal(@"Software\Contoso.Inventory", HostIdentity.RegistryPath("Contoso.Inventory", "Display"));
      Assert.Equal("Legacy Display", HostIdentity.RegistryPath(null, "Legacy Display"));
      var root = @"Software\EmReleaseFixture-" + Guid.NewGuid().ToString("N");
      try {
         var app = (EmApp)RuntimeHelpers.GetUninitializedObject(typeof(EmApp));
         app.BaseRegistryKeyFactory = () => Registry.CurrentUser.CreateSubKey(root);
         using var key = app.BaseRegKey; key.SetValue("fixture", "isolated");
         Assert.Equal("isolated", key.GetValue("fixture")); Assert.EndsWith(root, key.Name);
      }
      finally { Registry.CurrentUser.DeleteSubKeyTree(root, false); }
   }

   [Fact]
   public void SignedLauncherMetadataRoundTripsAndRejectsUnsafeOrUnlistedExe() {
      var metadata = new ReleaseLauncherMetadata { FileName = "inventory-installer.exe", Version = "1.0.1-beta.3", InputHash = new string('a', 64) };
      var manifest = new ReleaseManifest { PublishedAtUtc = DateTime.UtcNow, Launcher = metadata,
         Files = [new() { Path = metadata.FileName, Size = 0, Sha256 = new string('b', 64) }] };
      var bytes = ReleaseManifestSerializer.Serialize(manifest); Assert.Equal(metadata.FileName, ReleaseManifestSerializer.Deserialize(bytes).Launcher!.FileName);
      metadata.FileName = "../escape.exe"; Assert.Throws<ReleaseFormatException>(() => ReleaseManifestSerializer.Serialize(manifest));
   }

   [Fact]
   public async Task ReleaseDoesNotPublishInstallerSettingsAndNamesCannotOverwriteTheApplication() {
      Directory.CreateDirectory(_root); File.WriteAllText(Path.Combine(_root, "settings.ini"), "[Launcher]\nSource=local");
      File.WriteAllText(Path.Combine(_root, "Inventory.exe"), "app");
      var snapshot = await LocalPublish.ScanAsync(_root, null, TestContext.Current.CancellationToken);
      Assert.Equal("Inventory.exe", Assert.Single(snapshot.Files).Path);
      Assert.Throws<InvalidDataException>(() => LauncherBuild.ValidateSettings(new ReleaseLauncher { FileName = "Inventory.exe" }, "Inventory.exe"));
   }

   [Fact]
   public void UpdateScheduleDefaultsAndUnlaunchedStateNeedNoProcess() {
      var options = new LauncherUpdateOptions(); Assert.Equal(TimeSpan.FromMinutes(5), options.FirstCheckDelay); Assert.Equal(TimeSpan.FromMinutes(60), options.CheckInterval); Assert.Equal(TimeSpan.FromMinutes(10), options.Jitter);
      using var item = new StatusUpdateItem(); Assert.False(item.IsReady); Assert.Contains("Up to date", item.ToolTip);
   }
}
