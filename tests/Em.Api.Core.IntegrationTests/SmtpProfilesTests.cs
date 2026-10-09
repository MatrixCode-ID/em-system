using System.Security.Cryptography;
using System.Text.Json;
using Em.Api.Core.Models;
using Em.Api.Core.Smtp;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.IntegrationTests;

public class SmtpProfilesTests(SqlServerDatabase database)
{
   private static SmtpSettings Settings(string host) => new() { Enabled = true, Authenticate = false, Host = host, FromAddress = "sender@example.com" };
   private async Task<Factory> CreateAsync(CancellationToken ct) {
      var connection = await database.CreateExtraDatabaseAsync(ct);
      var factory = new Factory(new DbContextOptionsBuilder<ApiCoreContext>().UseSqlServer(connection).Options);
      await using var db = await factory.CreateDbContextAsync(ct); await db.Database.EnsureCreatedAsync(ct);
      return factory;
   }

   [Fact]
   public async Task EnabledRelayWithoutSenderCanBeSavedAndListed() {
      var ct = TestContext.Current.CancellationToken; var factory = await CreateAsync(ct);
      var store = new SmtpSettingsStore(factory);
      var saved = await store.SaveProfileAsync(new() { Name = "relay", Settings = new() {
         Enabled = true, Host = "relay.example.com", Port = 25, Security = SmtpSecurity.None, Authenticate = false
      } }, ct);
      var listed = Assert.Single((await store.ProfilesAsync(ct)).Profiles);
      Assert.Equal(saved.Id, listed.Id); Assert.True(listed.Settings.Enabled);
      Assert.Equal("", listed.Settings.FromAddress); Assert.False(listed.HasPassword);
      SmtpValidation.Settings((await store.SelectedCredentialsAsync(saved.Id, true, ct)).Settings, false, true);
   }

   [Fact]
   public async Task MigrationPreservesCredentialsLongRevisionAndNeverResurrectsDeletedDefault() {
      var ct = TestContext.Current.CancellationToken; var factory = await CreateAsync(ct);
      var key = RandomNumberGenerator.GetBytes(32); var encrypted = SmtpSecrets.Encrypt(key, "legacy-secret");
      const long revision = (long)int.MaxValue + 17;
      await using (var db = await factory.CreateDbContextAsync(ct)) {
         db.ta_Metas.Add(new() { cMetaKey = SmtpSettingsStore.SecretKey, cMetaValue = Convert.ToBase64String(key), ustamp = DateTime.UtcNow });
         db.ta_Metas.Add(new() { cMetaKey = SmtpSettingsStore.SettingsKey, ustamp = DateTime.UtcNow,
            cMetaValue = JsonSerializer.Serialize(new SmtpSettingsDocument { Revision = revision, Settings = Settings("legacy.example.com"), Password = encrypted }) });
         await db.SaveChangesAsync(ct);
      }
      var store = new SmtpSettingsStore(factory); var list = await store.ProfilesAsync(ct);
      var profile = Assert.Single(list.Profiles); Assert.True(profile.IsDefault); Assert.True(profile.HasPassword);
      Assert.Equal(revision, profile.Revision); Assert.Equal("legacy.example.com", profile.Settings.Host);
      Assert.Equal("legacy-secret", (await store.CredentialsAsync(ct)).Password);
      Assert.DoesNotContain("legacy-secret", JsonSerializer.Serialize(list));
      await using (var db = await factory.CreateDbContextAsync(ct)) {
         Assert.Equal(encrypted, (await db.ta_Smtps.SingleAsync(ct)).cSmtpPassword);
         Assert.Equal(3, await db.ta_Metas.CountAsync(ct));
      }
      Assert.Single((await new SmtpSettingsStore(factory).ProfilesAsync(ct)).Profiles);
      await store.DeleteAsync(profile.Id, list.Revision, ct);
      Assert.Empty((await new SmtpSettingsStore(factory).ProfilesAsync(ct)).Profiles);
      var error = await Assert.ThrowsAsync<ActionException>(() => store.CredentialsAsync(ct));
      Assert.Equal(409, error.StatusCode); Assert.Contains("No default SMTP", error.Message);
   }

   [Fact]
   public async Task NamedProfilesDefaultSwitchingAndLegacyRevisionShareOneConcurrencyBoundary() {
      var ct = TestContext.Current.CancellationToken; var factory = await CreateAsync(ct); var store = new SmtpSettingsStore(factory);
      var a = await store.SaveProfileAsync(new() { Name = "primary", Settings = Settings("a.example.com") }, ct);
      Assert.False(a.IsDefault);
      await Assert.ThrowsAsync<ActionException>(() => store.CredentialsAsync(ct));
      Assert.Equal("a.example.com", (await store.SelectedCredentialsAsync("PRIMARY", false, ct)).Settings.Host);
      var list = await store.DefaultAsync(a.Id, a.Revision, ct);
      var legacyRead = await store.ReadAsync(ct); Assert.Equal("a.example.com", legacyRead.Settings.Host);
      var b = await store.SaveProfileAsync(new() { Name = "secondary", ExpectedRevision = list.Revision, Settings = Settings("b.example.com") }, ct);
      Assert.False(b.IsDefault); Assert.Equal(2, (await store.ProfilesAsync(ct)).Profiles.Length);
      list = await store.DefaultAsync(b.Id, b.Revision, ct);
      Assert.Equal("b.example.com", (await store.CredentialsAsync(ct)).Settings.Host);
      var stale = await Assert.ThrowsAsync<ActionException>(() => store.SaveAsync(new() { ExpectedRevision = legacyRead.Revision, Settings = Settings("wrong.example.com") }, ct));
      Assert.Equal(409, stale.StatusCode); Assert.Equal("b.example.com", (await store.CredentialsAsync(ct)).Settings.Host);
      await Assert.ThrowsAsync<ActionException>(() => store.SaveProfileAsync(new() { Name = "PRIMARY", ExpectedRevision = list.Revision }, ct));
      await Assert.ThrowsAsync<ActionException>(() => store.SelectedCredentialsAsync("missing", false, ct));
      var legacySaved = await store.SaveAsync(new() { Settings = Settings("updated.example.com"), ExpectedRevision = list.Revision }, ct);
      Assert.Equal("updated.example.com", (await store.SelectedCredentialsAsync("secondary", false, ct)).Settings.Host);
      list = await store.DefaultAsync(null, legacySaved.Revision, ct);
      Assert.DoesNotContain(list.Profiles, x => x.IsDefault);
      await Assert.ThrowsAsync<ActionException>(() => store.CredentialsAsync(ct));
      Assert.Equal("a.example.com", (await store.SelectedCredentialsAsync(a.Id, true, ct)).Settings.Host);
   }

   [Fact]
   public async Task ProfileSecretEditsRetainClearAndStaleWritesCannotOverwrite() {
      var ct = TestContext.Current.CancellationToken; var factory = await CreateAsync(ct); var store = new SmtpSettingsStore(factory);
      var profile = await store.SaveProfileAsync(new() { Name = "auth", Settings = Settings("smtp.example.com"), Password = "first-secret" }, ct);
      Assert.True(profile.HasPassword);
      profile = await store.SaveProfileAsync(new() { Id = profile.Id, Name = "renamed", Settings = profile.Settings, ExpectedRevision = profile.Revision }, ct);
      Assert.Equal("first-secret", (await store.SelectedCredentialsAsync("renamed", false, ct)).Password);
      var revision = profile.Revision;
      async Task<bool> Save(string secret) {
         try { await store.SaveProfileAsync(new() { Id = profile.Id, Name = profile.Name, Settings = profile.Settings, ExpectedRevision = revision, Password = secret }, ct); return true; }
         catch (ActionException ex) when (ex.StatusCode == 409) { return false; }
      }
      Assert.Single(await Task.WhenAll(Save("second-secret"), Save("third-secret")), x => x);
      profile = Assert.Single((await store.ProfilesAsync(ct)).Profiles);
      await store.SaveProfileAsync(new() { Id = profile.Id, Name = profile.Name, Settings = profile.Settings, ExpectedRevision = profile.Revision, ClearPassword = true }, ct);
      Assert.Null((await store.SelectedCredentialsAsync(profile.Id, true, ct)).Password);
      var currentRevision = (await store.ProfilesAsync(ct)).Revision;
      await Assert.ThrowsAsync<ActionException>(() => store.SaveProfileAsync(new() { Name = "bad", Settings = Settings("smtp.example.com"), ExpectedRevision = currentRevision, Password = "x", ClearPassword = true }, ct));
   }

   [Fact]
   public async Task LegacySaveDoesNotRepurposeNonDefaultAndMissingSchemaHasActionableError() {
      var ct = TestContext.Current.CancellationToken; var factory = await CreateAsync(ct); var store = new SmtpSettingsStore(factory);
      var named = await store.SaveProfileAsync(new() { Name = "DEFAULT", Settings = Settings("named.example.com") }, ct);
      var saved = await store.SaveAsync(new() { ExpectedRevision = named.Revision, Settings = Settings("legacy.example.com") }, ct);
      var profiles = (await store.ProfilesAsync(ct)).Profiles;
      Assert.Equal(2, profiles.Length); Assert.False(profiles.Single(x => x.Id == named.Id).IsDefault);
      Assert.Equal("named.example.com", (await store.SelectedCredentialsAsync("default", false, ct)).Settings.Host);
      Assert.Equal("legacy.example.com", saved.Settings.Host);
      var missing = await CreateAsync(ct);
      await using (var db = await missing.CreateDbContextAsync(ct)) await db.Database.ExecuteSqlRawAsync("DROP TABLE dbo.ta_Smtp;", ct);
      var error = await Assert.ThrowsAsync<ActionException>(() => new SmtpSettingsStore(missing).ReadAsync(ct));
      Assert.Equal(503, error.StatusCode); Assert.Contains("050-smtp.sql", error.Message);
   }

   [Fact]
   public async Task LegacyFirstSaveCreatesDefaultAndFailedMigrationLeavesNoMarkerOrProfiles() {
      var ct = TestContext.Current.CancellationToken; var factory = await CreateAsync(ct); var store = new SmtpSettingsStore(factory);
      var saved = await store.SaveAsync(new() { Settings = Settings("legacy-client.example.com") }, ct);
      Assert.True(Assert.Single((await store.ProfilesAsync(ct)).Profiles).IsDefault);
      Assert.Equal(1, saved.Revision);
      var brokenFactory = await CreateAsync(ct);
      await using (var db = await brokenFactory.CreateDbContextAsync(ct)) {
         db.ta_Metas.Add(new() { cMetaKey = SmtpSettingsStore.SettingsKey, ustamp = DateTime.UtcNow,
            cMetaValue = JsonSerializer.Serialize(new SmtpSettingsDocument { Password = [1], Settings = Settings("broken.example.com") }) });
         await db.SaveChangesAsync(ct);
      }
      await Assert.ThrowsAsync<ActionException>(() => new SmtpSettingsStore(brokenFactory).ProfilesAsync(ct));
      await using var check = await brokenFactory.CreateDbContextAsync(ct);
      Assert.Empty(await check.ta_Smtps.ToArrayAsync(ct));
      Assert.False(await check.ta_Metas.AnyAsync(x => x.cMetaKey == SmtpSettingsStore.RevisionKey, ct));
      Assert.True(await check.ta_Metas.AnyAsync(x => x.cMetaKey == SmtpSettingsStore.SettingsKey, ct));
   }

   private sealed class Factory(DbContextOptions<ApiCoreContext> options) : IDbContextFactory<ApiCoreContext>
   {
      public ApiCoreContext CreateDbContext() => new(options);
      public Task<ApiCoreContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
   }
}
