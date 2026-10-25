using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Em.Ui.Core.Release.Format;
using Em.Ui.Wpf.Core.Release;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Core.Tests;

public sealed class AndroidReleaseTests : IDisposable {
   private readonly string _root=Path.Combine(Path.GetTempPath(),"AndroidReleaseTests-"+Guid.NewGuid().ToString("N"));
   public AndroidReleaseTests() => Directory.CreateDirectory(_root);
   public void Dispose() { Directory.Delete(_root,true); }
   private static ReleaseProfile Profile() => new() { Name="Mobile", Platform=ReleasePlatform.Android, PrivateKeyPem=ReleasePemKey.Create(),
      Product=new() { AppName="Inventory",Publisher="Contoso",AppId="Contoso.Inventory.Mobile",Version="1.2.0-alpha.1+build",AndroidTargetFramework="net10.0-android" },
      Android=new() { ApplicationId="com.contoso.inventory",KeystoreBase64="AA==",KeystoreAlias="release",KeystorePassword="store-secret",KeyPassword="key-secret",CertificateSha256=new string('a',64) } };
   private static ReleaseManifest Manifest(int code=12,string id="com.contoso.inventory") => new() { PublishedAtUtc=DateTime.UtcNow,
      Files=[new() { Path="inventory.apk",Size=1,Sha256=new string('a',64) }],
      Android=new() { FileName="inventory.apk",ApplicationId=id,VersionCode=code,VersionName="1.2.0" } };
   [Fact]
   public void OldProfileDefaultsToWindowsAndNewProfileRoundTrips() {
      var json=JsonNode.Parse(ReleaseProfile.ToJson(Profile()))!; json.AsObject().Remove("platform"); json.AsObject().Remove("android");
      var old=ReleaseProfile.FromJson(json.ToJsonString()); Assert.Equal(ReleasePlatform.Windows,old.Platform); Assert.NotNull(old.Android);
      var current=ReleaseProfile.FromJson(ReleaseProfile.ToJson(Profile())); Assert.Equal(ReleasePlatform.Android,current.Platform); Assert.Equal("store-secret",current.Android.KeystorePassword);
   }
   [Fact]
   public void PlatformChangeReplacesKeyAndInvalidatesPreparedState() {
      var profile=Profile(); var key=profile.PrivateKeyPem; profile.Android.PreparedVersionCode=12;
      profile.ChangePlatform(ReleasePlatform.Windows); Assert.NotEqual(key,profile.PrivateKeyPem); Assert.Equal(0,profile.Android.PreparedVersionCode);
      var changed=profile.PrivateKeyPem; profile.ChangePlatform(ReleasePlatform.Windows); Assert.Equal(changed,profile.PrivateKeyPem);
   }
   [Theory] [InlineData("",1)] [InlineData("7",7)] [InlineData("0",1)] [InlineData("bad",1)] [InlineData("2100000001",1)]
   public void FirstCodeUsesHostVersion(string initial,int expected) => Assert.Equal(expected,AndroidReleaseBuilder.NextVersionCode(null,initial,"com.contoso.inventory"));
   [Fact]
   public void ExistingCodeAndIdentityAreAuthoritative() {
      Assert.Equal(13,AndroidReleaseBuilder.NextVersionCode(Manifest(),"1","com.contoso.inventory"));
      Assert.Throws<InvalidDataException>(()=>AndroidReleaseBuilder.NextVersionCode(Manifest(),"1","com.contoso.other"));
      Assert.Throws<InvalidDataException>(()=>AndroidReleaseBuilder.NextVersionCode(new() { PublishedAtUtc=DateTime.UtcNow,Files=[] },"1","com.contoso.inventory"));
      Assert.Throws<InvalidDataException>(()=>AndroidReleaseBuilder.NextVersionCode(Manifest(2100000000),"1","com.contoso.inventory"));
   }
   [Fact]
   public void DraftIsSaveableButPrepareNeedsCompleteCredentials() {
      var p=Profile(); p.Android.KeystoreBase64=""; ReleaseProfile.ToJson(p); Assert.Throws<InvalidDataException>(p.ValidateAndroidRelease);
      p=Profile(); p.Android.ApplicationId="Com.Inventory"; Assert.Throws<InvalidDataException>(p.ValidateAndroidRelease);
      p=Profile(); p.Android.ApkFileName="../bad.apk"; Assert.Throws<InvalidDataException>(p.ValidateAndroidRelease);
      p=Profile(); p.ValidateAndroidRelease(); Assert.Equal("contoso-inventory-mobile.apk",p.AndroidApkFileName);
   }
   [Fact]
   public void TargetsScopeIdentityAndNeverWritePasswordsOrWindowsBranding() {
      var p=Profile(); var project=Path.Combine(_root,"Host.csproj");
      var file=AndroidReleaseBuilder.WriteIdentityTargets(p,project,_root,Path.Combine(_root,"release.p12"),12,"https://example.test/release/");
      var text=File.ReadAllText(file); var xml=XDocument.Load(file); XNamespace ns="http://schemas.microsoft.com/developer/msbuild/2003";
      Assert.Contains("MSBuildProjectFullPath",text); Assert.Contains(project,xml.Descendants(ns+"PropertyGroup").Single().Attribute("Condition")!.Value);
      Assert.Contains("env:EM_ANDROID_STORE_PASS",text); Assert.Contains("env:EM_ANDROID_KEY_PASS",text);
      Assert.DoesNotContain(p.Android.KeystorePassword,text); Assert.DoesNotContain(p.Android.KeyPassword,text);
      Assert.DoesNotContain("AssemblyName",text); Assert.DoesNotContain("ApplicationIcon",text); Assert.DoesNotContain("<Product>",text);
      Assert.Contains("EmReleaseAddress",text); Assert.Contains("EmReleasePublicKey",text); Assert.Contains("1.2.0-alpha.1</",text);
   }
   [Fact]
   public void CommandsContainExplicitPathsAndOnlyEnvironmentPasswordReferences() {
      var args=AndroidReleaseBuilder.PublishArguments("host.csproj","out","net10.0-android","sdk","jdk","targets");
      Assert.Contains("android-arm64",args); Assert.Contains("-f",args); Assert.DoesNotContain("--self-contained",args);
      var key=AndroidKeystore.CreateArguments("key.p12","release","Contoso"); Assert.Contains("4096",key); Assert.Contains("10000",key); Assert.Contains("-storepass:env",key); Assert.Contains("-keypass:env",key);
      var commands=AndroidEnvironment.PreparationCommands(["Missing: maui-android workload","Missing: JDK java/keytool","Missing: SDK platform-tools"],"host.csproj","net10.0-android",Path.Combine(_root,"sdk"),"jdk");
      Assert.Equal(3,commands.Count); Assert.Contains("maui-android",commands[0].Arguments); Assert.Contains("Microsoft.OpenJDK.21",commands[1].Arguments);
      Assert.Contains("-t:InstallAndroidDependencies",commands[2].Arguments); Assert.Empty(AndroidEnvironment.PreparationCommands([],"host","net10.0-android","sdk","jdk"));
   }
   [Fact]
   public void CandidatesPreservePreferenceAndDeduplicateExistingFolders() {
      var first=Path.Combine(_root,"sdk1"); var second=Path.Combine(_root,"sdk2"); Directory.CreateDirectory(first); Directory.CreateDirectory(second);
      Assert.Equal(new[] { first,second },AndroidEnvironment.Candidates([first,second,first,Path.Combine(_root,"missing")]));
   }
   [Theory] [InlineData("openjdk version \"21.0.8\" 2025-07-15",21)] [InlineData("java version \"17.0.1\"",17)] [InlineData("garbage",0)]
   public void JavaVersionParsesRealToolOutput(string output,int expected) => Assert.Equal(expected,AndroidEnvironment.JavaMajorVersion(output));
   [Fact]
   public void PreferencesUseOnlyInjectedBaseRegistry() {
      var path=@"Software\Em.Tests\"+Guid.NewGuid().ToString("N");
      try { var prefs=new ReleaseManagerPreferences(()=>Registry.CurrentUser.CreateSubKey(path)); prefs.AndroidSdkDirectory="sdk"; prefs.JavaSdkDirectory="java";
         Assert.Equal("sdk",prefs.AndroidSdkDirectory); Assert.Equal("java",prefs.JavaSdkDirectory);
      } finally { Registry.CurrentUser.DeleteSubKeyTree(path,false); }
   }
   [Fact]
   public async Task HostDiscoveryAndIdentityRecognizeAndroidAndMultitargeting() {
      var project=Path.Combine(_root,"Host.csproj");
      File.WriteAllText(project,"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFrameworks>net10.0;net10.0-android</TargetFrameworks><ApplicationTitle>Inventory Mobile</ApplicationTitle><Company>Contoso</Company><EmAppId>Contoso.Inventory.Mobile</EmAppId><ApplicationId>com.contoso.inventory</ApplicationId><Version>1.2.0</Version><ApplicationVersion>7</ApplicationVersion></PropertyGroup></Project>");
      var solution=Path.Combine(_root,"Host.slnx"); File.WriteAllText(solution,"<Solution><Project Path=\"Host.csproj\"/></Solution>");
      Assert.Contains(ReleasePlatform.Android,ReleaseBuilder.ReadHostPlatforms(project)); Assert.Single(ReleaseBuilder.ReadHostProjects(solution,null,default,ReleasePlatform.Android));
      var product=await ReleaseProduct.ReadAsync(project,default,ReleasePlatform.Android); Assert.Equal("Inventory Mobile",product.AppName); Assert.Equal("1.2.0",product.Version); Assert.Equal("7",product.ApplicationVersion);
   }
   [Fact]
   public void HostPlatformReadsImportedTargetFramework() {
      var project=Path.Combine(_root,"Imported.csproj");
      File.WriteAllText(Path.Combine(_root,"identity.props"),"<Project><PropertyGroup><TargetFramework>net10.0-android</TargetFramework></PropertyGroup></Project>");
      File.WriteAllText(project,"<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"identity.props\"/><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>");
      Assert.Contains(ReleasePlatform.Android,ReleaseBuilder.ReadHostPlatforms(project));
   }
   [Fact]
   public async Task FolderSyncVerifiesAndroidAndRejectsModifiedPreparedBytesOrIdentity() {
      var p=Profile(); p.Android.ApkFileName="inventory.apk"; p.PublishFolder=Path.Combine(_root,"publish"); Directory.CreateDirectory(p.PublishFolder);
      var data=new byte[] { 1,2,3 }; File.WriteAllBytes(Path.Combine(p.PublishFolder,"inventory.apk"),data);
      p.Android.PreparedVersionCode=1; p.Android.PreparedVersionName="1.2.0"; p.Android.PreparedApkSha256=Convert.ToHexStringLower(SHA256.HashData(data));
      var target=new FolderReleaseTarget(Path.Combine(_root,"target"),"mobile");
      var local=await LocalPublish.ScanAsync(p.PublishFolder,null,default); var comparison=await ReleaseComparer.CompareAsync(target,local,default);
      using var key=ReleasePemKey.Open(p.PrivateKeyPem);
      var manifest=await ReleaseSync.SyncAsync(target,comparison,key,null,default,profile:p);
      Assert.NotNull(manifest.Android); Assert.Null(manifest.Launcher);
      Assert.True((await ReleaseVerifier.VerifyAsync(target,[key.ExportSubjectPublicKeyInfo()],null,default,p)).IsSuccess);
      p.Android.ApplicationId="com.contoso.other";
      Assert.False((await ReleaseVerifier.VerifyAsync(target,[key.ExportSubjectPublicKeyInfo()],null,default,p)).IsSuccess);
      comparison=await ReleaseComparer.CompareAsync(target,local,default);
      await Assert.ThrowsAsync<ReleaseConflictException>(()=>ReleaseSync.SyncAsync(target,comparison,key,null,default,profile:p));
      p.Android.ApplicationId="com.contoso.inventory";
      File.WriteAllBytes(Path.Combine(p.PublishFolder,"inventory.apk"),[4,5,6]);
      local=await LocalPublish.ScanAsync(p.PublishFolder,null,default); comparison=await ReleaseComparer.CompareAsync(target,local,default);
      await Assert.ThrowsAsync<InvalidDataException>(()=>ReleaseSync.SyncAsync(target,comparison,key,null,default,profile:p));
   }
}
