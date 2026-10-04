using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core.Release;
using Em.Ui.Wpf.Navigations;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Publish;
using Microsoft.Win32;

internal static class Program
{
   [STAThread]
   private static int Main() {
      var temporary = Path.Combine(Path.GetTempPath(), "em-release-profile-smoke-" + Guid.NewGuid().ToString("N"));
      var registry = "EmReleaseProfileSmoke-" + Guid.NewGuid().ToString("N");
      var failures = 0;
      void Check(string name, Action test) {
         try { test(); Console.WriteLine("PASS " + name); }
         catch (Exception x) { failures++; Console.WriteLine($"FAIL {name}: {x.Message}"); }
      }
      static void Require(bool value) { if (!value) throw new Exception("Assertion failed."); }
      static void Throws<T>(Action action) where T : Exception {
         try { action(); }
         catch (T) { return; }
         throw new Exception("Expected " + typeof(T).Name);
      }
      Directory.CreateDirectory(temporary);
      var store = new ReleaseProfileStore(Path.Combine(temporary, "profiles"));
      var preferences = new ReleaseManagerPreferences(() => Registry.CurrentUser.CreateSubKey(registry));
      var secrets = new ReleaseSigningSecrets(Path.Combine(temporary, "secrets"));
      ReleaseProfile a;
      ReleaseProfile b;
      try {
         a = store.Create("Alpha");
         b = store.Create("Bravo");
         Check("Create List Load Save Rename unique names", () => {
            Require(store.List()[0].Profile!.Name == "Alpha");
            a.SolutionPath = "example.slnx";
            store.Save(a);
            Require(store.Load(a.Id).SolutionPath == "example.slnx");
            store.Rename(b.Id, "Zulu");
            Throws<InvalidOperationException>(() => store.Rename(b.Id, " alpha "));
            Require(!Directory.EnumerateFiles(store.Root, "*.tmp", SearchOption.AllDirectories).Any());
         });
         Check("Broken JSON and mismatched id stay in list", () => {
            var bad = Path.Combine(store.Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(bad);
            File.WriteAllText(Path.Combine(bad, "profile.json"), "{");
            var mismatch = Path.Combine(store.Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(mismatch);
            File.WriteAllText(Path.Combine(mismatch, "profile.json"), ReleaseProfile.ToJson(a));
            Require(store.List().Count(entry => entry.Error is not null) == 2);
         });
         Check("External changes rejected", () => {
            var entry = store.Save(a);
            File.SetLastWriteTimeUtc(Path.Combine(entry.Directory, "profile.json"), entry.LastWriteUtc.AddSeconds(2));
            Throws<ReleaseProfileChangedException>(() => store.Save(a, entry.LastWriteUtc));
            store.Save(a);
         });
         Check("PFX public certificate validation signing and re-export", () => {
            using var publicCertificate = SigningCertificates.CreatePfx(store.KeyFilePath(a.Id), store.CertificateFilePath(a.Id), "test-password");
            Require(!publicCertificate.HasPrivateKey);
            using var cer = SigningCertificates.LoadCertificateFile(store.CertificateFilePath(a.Id));
            Require(cer is not null && !cer.HasPrivateKey);
            Throws<CryptographicException>(() => SigningCertificates.ValidatePfx(store.KeyFilePath(a.Id), "wrong").Dispose());
            using var cert = SigningCertificates.LoadPfxForSigning(store.KeyFilePath(a.Id), "test-password");
            using var key = cert.GetECDsaPrivateKey();
            using var publicKey = cer!.GetECDsaPublicKey();
            byte[] data = [1, 2, 3];
            Require(publicKey!.VerifyData(data, key!.SignData(data, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256));
            var exported = Path.Combine(temporary, "reexport.pfx");
            SigningCertificates.ExportPfxFile(store.KeyFilePath(a.Id), "test-password", exported, "new-password");
            using var validated = SigningCertificates.ValidatePfx(exported, "new-password");
            Require(validated.Thumbprint == cert.Thumbprint);
            a.Signing = new() { Source = ReleaseSigningSource.ProfileFile, Thumbprint = cert.Thumbprint, PasswordStorage = ReleasePasswordStorage.Plaintext, Password = "test-password" };
            store.Save(a);
         });
         Check("Duplicate copies files and gets new identity and name", () => {
            var copy = store.Duplicate(a.Id);
            Require(copy.Id != a.Id && copy.Name == "Alpha (copy)" && store.HasKeyFile(copy.Id));
            Require(File.ReadAllBytes(store.CertificateFilePath(copy.Id)).SequenceEqual(File.ReadAllBytes(store.CertificateFilePath(a.Id))));
            store.Delete(copy.Id);
         });
         Check("Delete confined to profile folder and GUID ids", () => {
            var unrelated = Path.Combine(store.Root, "unrelated");
            Directory.CreateDirectory(unrelated);
            var publish = Path.Combine(temporary, "publish");
            Directory.CreateDirectory(publish);
            var copy = store.Duplicate(a.Id);
            copy.PublishFolder = publish;
            store.Save(copy);
            store.Delete(copy.Id);
            Require(Directory.Exists(unrelated) && Directory.Exists(publish) && Directory.Exists(store.DirectoryOf(a.Id)));
            Throws<InvalidDataException>(() => store.Delete("../profiles"));
         });
         Check("Registry migration only when no valid profiles", () => {
            using (var root = Registry.CurrentUser.CreateSubKey(registry))
            using (var key = root.CreateSubKey("ReleaseManager")) {
               key.SetValue("SolutionPath", "legacy.slnx");
               key.SetValue("SigningThumbprint", "LEGACY");
            }
            Require(preferences.ReadLegacyProfile()!.Signing.Thumbprint == "LEGACY");
            var vm = new ReleaseManagerVm();
            vm.Initialize(preferences, store, secrets);
            Require(preferences.HasLegacySettings());
            var empty = new ReleaseProfileStore(Path.Combine(temporary, "migrated"));
            vm.Initialize(preferences, empty, secrets);
            Require(!preferences.HasLegacySettings() && empty.List()[0].Profile!.Name == "Default" && empty.Load(vm.Profile!.Id).SolutionPath == "legacy.slnx");
         });
         Check("ServerKey normalization and last profiles per server", () => {
            Require(ReleaseManagerPreferences.ServerKey(new ApiConnection { Host = "https://Server.Example:443/" }) == "server.example");
            Require(ReleaseManagerPreferences.ServerKey(new ApiConnection { Host = "http://host:5132" }) == "host:5132");
            Require(ReleaseManagerPreferences.ServerKey(null) == "(none)");
            preferences.SetLastProfile("one", a.Id);
            preferences.SetLastProfile("two", b.Id);
            Require(preferences.GetLastProfile("one") == a.Id && preferences.GetLastProfile("two") == b.Id);
            preferences.ProfilesFolder = store.Root;
            Require(preferences.ProfilesFolder == store.Root);
         });
         Check("Target and publish conflicts normalized; empty values ignored", () => {
            var other = a.Clone(); other.Id = b.Id;
            a.PublishFolder = Path.Combine(temporary, "publish");
            other.PublishFolder = a.PublishFolder.ToUpperInvariant() + "\\";
            Require(ReleaseProfileConflicts.Find(a, [other], true, true).Count == 2);
            a.TargetKind = other.TargetKind = ReleaseTargetKind.Folder;
            a.TargetFolder = temporary; other.TargetFolder = temporary.ToUpperInvariant() + "\\";
            Require(ReleaseProfileConflicts.Find(a, [other], true, true).Count == 2);
            a.PublishFolder = other.PublishFolder = a.TargetFolder = other.TargetFolder = "";
            Require(ReleaseProfileConflicts.Find(a, [other], true, true).Count == 0);
            store.Save(a);
         });
         Check("Session DPAPI thumbprint corruption and Forget", () => {
            secrets.Put(a.Id, "thumb", "secret", false);
            Require(secrets.Get(a.Id, "thumb") == "secret" && !secrets.IsRemembered(a.Id));
            secrets.Put(a.Id, "thumb", "secret", true);
            Require(secrets.IsRemembered(a.Id));
            typeof(ReleaseSigningSecrets).GetMethod("ClearSessionForTesting", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null);
            var fresh = new ReleaseSigningSecrets(Path.Combine(temporary, "secrets"));
            Require(fresh.Get(a.Id, "thumb") == "secret" && fresh.Get(a.Id, "other") is null);
            typeof(ReleaseSigningSecrets).GetMethod("ClearSessionForTesting", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null);
            File.WriteAllText(Path.Combine(temporary, "secrets", a.Id + ".bin"), "corrupt");
            Require(fresh.Get(a.Id, "thumb") is null && !fresh.IsRemembered(a.Id));
            fresh.Put(a.Id, "thumb", "secret", true); fresh.Forget(a.Id);
            Require(fresh.Get(a.Id, "thumb") is null && !fresh.IsRemembered(a.Id));
         });
         Check("Export sensitive selection Import conflict new id and loose JSON", () => {
            var safe = Path.Combine(temporary, "safe.zip");
            var full = Path.Combine(temporary, "full.zip");
            store.Export(a.Id, safe, false); store.Export(a.Id, full, true);
            using (var zip = ZipFile.OpenRead(safe)) {
               Require(zip.GetEntry("signing.pfx") is null && zip.GetEntry("signing.cer") is not null);
               using var reader = new StreamReader(zip.GetEntry("profile.json")!.Open());
               Require(ReleaseProfile.FromJson(reader.ReadToEnd()).Signing.Password is null);
            }
            using (var zip = ZipFile.OpenRead(full)) {
               Require(zip.GetEntry("signing.pfx") is not null);
               using var reader = new StreamReader(zip.GetEntry("profile.json")!.Open());
               Require(ReleaseProfile.FromJson(reader.ReadToEnd()).Signing.Password == "test-password");
            }
            var importedStore = new ReleaseProfileStore(Path.Combine(temporary, "imports"));
            Require(importedStore.Import(full, false).Id == a.Id);
            Throws<ReleaseProfileConflictException>(() => importedStore.Import(full, false));
            Require(importedStore.Import(full, true).Id != a.Id);
            Require(importedStore.Import(Path.Combine(store.DirectoryOf(a.Id), "profile.json"), true).Id != a.Id);
            foreach (var entry in new[] { "foreign", "..\\x", "profile.json/profile.json" }) {
               var malicious = Path.Combine(temporary, "malicious.zip");
               using (var zip = new ZipArchive(File.Create(malicious), ZipArchiveMode.Create)) zip.CreateEntry(entry);
               Throws<InvalidDataException>(() => importedStore.Import(malicious, true));
            }
            Require(!Directory.EnumerateDirectories(importedStore.Root, ".import-*").Any());
         });
         Check("Settings draft cancel and password mode roundtrip", () => {
            var entry = store.Save(a);
            var vm = new ReleaseSettingsDialogVm();
            vm.Load(store, a, entry.LastWriteUtc, [], secrets, "10.0.100", true, false);
            vm.ReleaseFolder = "edited-release";
            Require(store.Load(a.Id).ReleaseFolder != "edited-release");
            vm.IsPlaintextPassword = false;
            vm.IsSeparatePassword = true;
            vm.SaveCommand();
            Require(vm.SavedProfile is not null);
            var saved = store.Load(a.Id);
            Require(saved.ReleaseFolder == "edited-release" && saved.Signing.Password is null && saved.Signing.PasswordStorage == ReleasePasswordStorage.Separate);
            Require(secrets.Get(a.Id, a.Signing.Thumbprint) == "test-password");
            vm = new ReleaseSettingsDialogVm();
            vm.Load(store, saved, store.List().First(e => e.Profile?.Id == a.Id).LastWriteUtc, [], secrets, "10.0.100", true, false);
            vm.IsSeparatePassword = false;
            vm.IsPlaintextPassword = true;
            vm.SaveCommand();
            Require(store.Load(a.Id).Signing.Password == "test-password");
         });
         Check("Public certificate retained without private key; empty-state commands", () => {
            var copy = store.Duplicate(a.Id);
            File.Delete(store.KeyFilePath(copy.Id));
            var vm = new ReleaseManagerVm();
            vm.Initialize(preferences, store, secrets);
            vm.LoadProfiles(copy.Id);
            Require(vm.HasSigningKey && vm.SigningCaption.Contains("Key file missing") && !vm.SyncCommandAllowed());
            var empty = new ReleaseProfileStore(Path.Combine(temporary, "empty"));
            vm.Initialize(preferences, empty, secrets);
            Require(vm.IsEmptyState && !vm.SettingsCommandAllowed() && !vm.PrepareCommandAllowed() && !vm.CompareCommandAllowed() && !vm.VerifyCommandAllowed());
         });
         Check("ZIP duplicate entries and oversized entry rejected", () => {
            var bad = Path.Combine(temporary, "duplicate.zip");
            using (var zip = new ZipArchive(File.Create(bad), ZipArchiveMode.Create)) {
               zip.CreateEntry("profile.json"); zip.CreateEntry("profile.json");
            }
            Throws<InvalidDataException>(() => store.Import(bad, true));
            using (var zip = new ZipArchive(File.Create(bad), ZipArchiveMode.Create)) {
               using var output = zip.CreateEntry("profile.json").Open();
               output.Write(new byte[10 * 1024 * 1024 + 1]);
            }
            Throws<InvalidDataException>(() => store.Import(bad, true));
         });
      }
      finally {
         typeof(ReleaseSigningSecrets).GetMethod("ClearSessionForTesting", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null);
         var validated = PublishPaths.Inside(Path.GetTempPath(), Path.GetFileName(temporary));
         PublishPaths.ValidateTree(validated);
         Directory.Delete(validated, recursive: true);
         Registry.CurrentUser.DeleteSubKeyTree(registry, throwOnMissingSubKey: false);
      }
      Console.WriteLine($"{(failures == 0 ? "PASS" : "FAIL")} cleanup temporary folder and test Registry; failures={failures}");
      return failures == 0 ? 0 : 1;
   }
}
