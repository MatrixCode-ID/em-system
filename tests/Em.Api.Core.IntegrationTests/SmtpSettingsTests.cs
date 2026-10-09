using System.Text.Json;
using Em.Api.Core.Models;
using Em.Api.Core.Smtp;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.IntegrationTests;

public class SmtpSettingsTests(SqlServerDatabase database)
{
   [Fact]
   public async Task SavesEncryptedPasswordRetainsClearsAndRejectsStaleRevision() {
      var ct = TestContext.Current.CancellationToken;
      var connection = await database.CreateExtraDatabaseAsync(ct);
      var factory = new ContextFactory(new DbContextOptionsBuilder<ApiCoreContext>().UseSqlServer(connection).Options);
      await using var db = await factory.CreateDbContextAsync(ct);
      await db.Database.EnsureCreatedAsync(ct);
      var store = new SmtpSettingsStore(factory);
      var initial = await store.ReadAsync(ct);
      Assert.Equal(0, initial.Revision); Assert.False(initial.Settings.Enabled);
      var settings = new SmtpSettings { Enabled = true, Host = "smtp.example.com", Username = "user", FromAddress = "sender@example.com" };
      var saved = await store.SaveAsync(new() { Settings = settings, Password = "private-test-password" }, ct);
      Assert.True(saved.HasPassword); Assert.Equal(1, saved.Revision);
      var encrypted = await db.ta_Smtps.AsNoTracking().Select(m => m.cSmtpPassword).SingleAsync(ct);
      Assert.DoesNotContain("private-test-password", System.Text.Encoding.UTF8.GetString(encrypted!));
      Assert.DoesNotContain("private-test-password", JsonSerializer.Serialize(saved));
      Assert.Equal("private-test-password", (await store.CredentialsAsync(ct)).Password);
      var conflict = await Assert.ThrowsAsync<ActionException>(() => store.SaveAsync(new() { Settings = settings, ExpectedRevision = 0, Password = "overwritten" }, ct));
      Assert.Equal(409, conflict.StatusCode);
      Assert.Equal("private-test-password", (await store.CredentialsAsync(ct)).Password);
      settings.FromName = "Updated";
      saved = await store.SaveAsync(new() { Settings = settings, ExpectedRevision = 1 }, ct);
      Assert.True(saved.HasPassword); Assert.Equal(2, saved.Revision);
      Assert.Equal("private-test-password", (await store.CredentialsAsync(ct)).Password);
      await Assert.ThrowsAsync<ActionException>(() => store.SaveAsync(new() { Settings = settings, ExpectedRevision = 2, ClearPassword = true }, ct));
      Assert.Equal(2, (await store.ReadAsync(ct)).Revision);
      settings.Authenticate = false;
      saved = await store.SaveAsync(new() { Settings = settings, ExpectedRevision = 2, ClearPassword = true }, ct);
      Assert.False(saved.HasPassword); Assert.Null((await store.CredentialsAsync(ct)).Password);
      Assert.Equal(3, saved.Revision);
   }

   [Fact]
   public async Task ConcurrentFirstWritesCommitOnlyOneDocumentAndMatchingKey() {
      var ct = TestContext.Current.CancellationToken;
      var connection = await database.CreateExtraDatabaseAsync(ct);
      var factory = new ContextFactory(new DbContextOptionsBuilder<ApiCoreContext>().UseSqlServer(connection).Options);
      await using var db = await factory.CreateDbContextAsync(ct);
      await db.Database.EnsureCreatedAsync(ct);
      async Task<bool> Save(string password) {
         try { await new SmtpSettingsStore(factory).SaveAsync(new() { Password = password }, ct); return true; }
         catch (ActionException ex) when (ex.StatusCode == 409) { return false; }
      }
      var outcomes = await Task.WhenAll(Save("first"), Save("second"));
      Assert.Single(outcomes, success => success);
      Assert.Equal(2, await db.ta_Metas.CountAsync(ct));
      var password = (await new SmtpSettingsStore(factory).CredentialsAsync(ct)).Password;
      Assert.Contains(password, new[] { "first", "second" });
   }
   private sealed class ContextFactory(DbContextOptions<ApiCoreContext> options) : IDbContextFactory<ApiCoreContext>
   {
      public ApiCoreContext CreateDbContext() => new(options);
      public Task<ApiCoreContext> CreateDbContextAsync(CancellationToken cancellationToken = default) {
         cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(CreateDbContext());
      }
   }
}
