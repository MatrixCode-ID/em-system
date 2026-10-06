using System.IO;
using Em.Ui.Wpf.Publish;

namespace Em.Ui.Wpf.Core.Tests
{
   public sealed class ProfileTransferTests : IDisposable
   {
      private const string Passphrase = "correct horse battery";
      private const string Secret = "s3cret-registry-password";
      private readonly string folder = Path.Combine(Path.GetTempPath(), "em-profile-transfer-" + Guid.NewGuid().ToString("N"));

      public void Dispose() {
         try {
            Directory.Delete(folder, true);
         } catch (IOException) {
         } catch (UnauthorizedAccessException) {
         }
      }

      [Fact]
      public void Bundle_RoundTripsAndRejectsWrongPassphrase() {
         var envelope = ProfileBundle.Encrypt("{\"hello\":\"world\"}", Passphrase);

         Assert.DoesNotContain("hello", envelope);
         Assert.Equal("{\"hello\":\"world\"}", ProfileBundle.Decrypt(envelope, Passphrase));
         var wrong = Assert.Throws<InvalidDataException>(() => ProfileBundle.Decrypt(envelope, "another passphrase"));
         Assert.Contains("Wrong passphrase", wrong.Message);
         Assert.Throws<InvalidDataException>(() => ProfileBundle.Encrypt("x", "short"));
      }

      [Theory]
      [InlineData(ExportSecrets.PlainText, true)]
      [InlineData(ExportSecrets.Encrypted, false)]
      public void ExportThenImport_RestoresSecretIntoSeparateStore(ExportSecrets mode, bool fileContainsSecret) {
         var (source, profile, secrets) = CreateProfile("source");
         var file = Path.Combine(folder, "export" + (mode == ExportSecrets.Encrypted ? ProfileBundle.ContainerExtension : ".json"));

         var missing = source.Export(profile, file, mode, secrets, Passphrase);

         Assert.Equal(0, missing);
         Assert.Equal(fileContainsSecret, File.ReadAllText(file).Contains(Secret));

         // A different computer: its own store and an empty secret store.
         var target = new ProfileStore(Path.Combine(folder, "target"));
         var targetSecrets = new PublishSecretStore();
         var imported = target.Import(file, false, Passphrase, targetSecrets, rememberSecrets: false).Profile!;

         var credential = Assert.Single(imported.Credentials);
         Assert.NotEqual(profile.Credentials[0].Id, credential.Id);
         Assert.Null(credential.Secret);
         Assert.Equal(Secret, targetSecrets.Get(credential, "registry.example.com"));
         Assert.DoesNotContain(Secret, File.ReadAllText(Directory.GetFiles(Path.Combine(folder, "target", "Container"), "*.json").Single()));
      }

      [Fact]
      public void Export_WithoutSecrets_LeavesCredentialEmpty_AndCountsMissingWhenAsked() {
         var (source, profile, secrets) = CreateProfile("source");
         var none = Path.Combine(folder, "none.json");
         var emptyStore = new PublishSecretStore();
         var plain = Path.Combine(folder, "plain.json");

         Assert.Equal(0, source.Export(profile, none, ExportSecrets.None, secrets));
         Assert.DoesNotContain(Secret, File.ReadAllText(none));
         Assert.Equal(1, source.Export(profile, plain, ExportSecrets.PlainText, emptyStore));
      }

      [Fact]
      public void Import_WithWrongPassphrase_FailsWithoutSavingProfile() {
         var (source, profile, secrets) = CreateProfile("source");
         var file = Path.Combine(folder, "locked" + ProfileBundle.ContainerExtension);
         source.Export(profile, file, ExportSecrets.Encrypted, secrets, Passphrase);
         var target = new ProfileStore(Path.Combine(folder, "target"));

         var failure = Assert.Throws<InvalidDataException>(() => target.Import(file, false, "not the passphrase", new PublishSecretStore(), false));

         Assert.Contains("Wrong passphrase", failure.Message);
         Assert.Empty(target.List(PublishKind.Container));
      }

      [Fact]
      public void Credential_ForAnotherHost_NamesTheMissingHostAndTheOnesTheProfileHas() {
         var (_, profile, secrets) = CreateProfile("source");
         var targets = new PublishTargets(null, secrets);

         var failure = Assert.Throws<InvalidDataException>(() => targets.Credential(profile, "localhost:5132"));

         Assert.Contains("'localhost:5132'", failure.Message);
         Assert.Contains("registry.example.com", failure.Message);
         Assert.Equal(Secret, targets.Credential(profile, "registry.example.com").Secret);
      }

      [Fact]
      public void Credential_WithoutSecretInThisSession_SaysSo() {
         var (_, profile, _) = CreateProfile("source");
         var targets = new PublishTargets(null, new PublishSecretStore());

         var failure = Assert.Throws<InvalidDataException>(() => targets.Credential(profile, "registry.example.com"));

         Assert.Contains("has no secret in this session", failure.Message);
      }

      private (ProfileStore Store, PublishProfile Profile, PublishSecretStore Secrets) CreateProfile(string name) {
         var store = new ProfileStore(Path.Combine(folder, name));
         var profile = PublishProfile.Create(PublishKind.Container);
         profile.Name = "Registry push";
         profile.Workspace = folder;
         var credential = new PublishCredential { ScopeHost = "registry.example.com", Username = "ci" };
         profile.Credentials.Add(credential);
         var secrets = new PublishSecretStore();
         secrets.Put(credential, Secret, remember: false);
         store.Save(profile);
         return (store, profile, secrets);
      }
   }
}
