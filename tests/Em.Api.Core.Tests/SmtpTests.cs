using System.Security.Cryptography;
using System.Text.Json;
using Em.Api.Core.Models;
using Em.Api.Core.Smtp;
using Em.Api.Shared;
using Em.Shared;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace Em.Api.Core.Tests;

public class SmtpTests
{
   private static SmtpSettings Config() => new() { Enabled = true, Host = "smtp.example.com", Authenticate = false, FromAddress = "sender@example.com" };
   private static SmtpMessage Email() => new() { To = ["to@example.com"], Subject = "Test", TextBody = "Text", HtmlBody = "<p>Text</p>" };

   [Theory]
   [InlineData("bad\r\nBcc: other@example.com")]
   [InlineData("alice@example.com,bob@example.com")]
   [InlineData("Alice <alice@example.com>")]
   [InlineData("")]
   public void InvalidRecipientsRejected(string address) {
      var email = Email(); email.To = [address];
      Assert.Equal(400, Assert.Throws<ActionException>(() => SmtpValidation.Message(Config(), email)).StatusCode);
   }

   [Fact]
   public void BuildsAlternativeBodyWithAttachmentsAndEnvelopeRecipients() {
      var input = Email(); input.Cc = ["cc@example.com"]; input.Bcc = ["private@example.com"]; input.ReplyTo = "reply@example.com";
      input.Attachments = [new() { FileName = "report.txt", ContentType = "text/plain", Content = [65, 66] }];
      using var mime = SmtpValidation.Message(Config(), input);
      Assert.Equal("sender@example.com", mime.From.Mailboxes.Single().Address);
      Assert.Single(mime.Cc); Assert.Single(mime.Bcc); Assert.Single(mime.Attachments);
      Assert.Equal("Text", mime.TextBody); Assert.Equal("<p>Text</p>", mime.HtmlBody);
      Assert.Equal("reply@example.com", mime.ReplyTo.Mailboxes.Single().Address);
   }

   [Fact]
   public void RejectsInjectionAndContentLimits() {
      var settings = Config();
      var email = Email(); email.Subject = "Subject\r\nBcc: leak@example.com";
      Assert.Throws<ActionException>(() => SmtpValidation.Message(settings, email));
      email = Email(); email.To = Enumerable.Repeat("to@example.com", 101).ToArray();
      Assert.Throws<ActionException>(() => SmtpValidation.Message(settings, email));
      email = Email(); email.Attachments = [new() { FileName = "../secret.txt" }];
      Assert.Throws<ActionException>(() => SmtpValidation.Message(settings, email));
      email.Attachments = [new() { FileName = "large.bin", Content = new byte[10 * 1024 * 1024 + 1] }];
      Assert.Throws<ActionException>(() => SmtpValidation.Message(settings, email));
      email = Email(); email.TextBody = new string('x', 2 * 1024 * 1024 + 1);
      Assert.Throws<ActionException>(() => SmtpValidation.Message(settings, email));
   }

   [Fact]
   public void RequiresTlsForAuthenticationAndRejectsUrlHost() {
      var settings = Config(); settings.Authenticate = true; settings.Username = "user"; settings.Security = SmtpSecurity.None;
      Assert.Throws<ActionException>(() => SmtpValidation.Settings(settings, true));
      settings.Security = SmtpSecurity.StartTls;
      SmtpValidation.Settings(settings, true);
      Assert.Throws<ActionException>(() => SmtpValidation.Settings(settings, false));
      settings.Host = "smtp.example.com:587";
      Assert.Throws<ActionException>(() => SmtpValidation.Settings(settings, true));
      settings.Enabled = false; settings.Host = ""; settings.FromAddress = "";
      SmtpValidation.Settings(settings, false);
      Assert.Throws<ActionException>(() => SmtpValidation.Settings(settings, false, true));
   }

   [Fact]
   public void SecretRoundTripRandomizesCiphertextAndDetectsTampering() {
      var key = RandomNumberGenerator.GetBytes(32);
      var data = SmtpSecrets.Encrypt(key, "test-secret");
      Assert.Equal("test-secret", SmtpSecrets.Decrypt(key, data));
      Assert.NotEqual(data, SmtpSecrets.Encrypt(key, "test-secret"));
      data[^1] ^= 1;
      Assert.ThrowsAny<CryptographicException>(() => SmtpSecrets.Decrypt(key, data));
   }

   [Fact]
   public async Task SendIsAvailableWithoutHttpInitializationAndReadsLatestSettings() {
      var store = new FakeStore(); var transport = new FakeTransport();
      var service = new SmtpService(store, transport);
      var result = await service.SendAsync(Email(), TestContext.Current.CancellationToken);
      Assert.NotEmpty(result.MessageId); Assert.Equal(1, transport.Calls);
      store.Settings.Enabled = false;
      Assert.Equal(409, (await Assert.ThrowsAsync<ActionException>(() => service.SendAsync(Email(), TestContext.Current.CancellationToken))).StatusCode);
      Assert.Equal(1, transport.Calls);
      await service.PostGetMeta_SmtpTestConnection();
      Assert.Equal(2, transport.Calls); Assert.True(transport.LastWasDiagnostic);
   }

   [Fact]
   public async Task SafeErrorsDoNotEchoTransportSecretsAndCallerCancellationIsPreserved() {
      var store = new FakeStore(); var transport = new FakeTransport { Failure = new IOException("test-secret from server") };
      var service = new SmtpService(store, transport);
      var ex = await Assert.ThrowsAsync<ActionException>(() => service.SendAsync(Email(), TestContext.Current.CancellationToken));
      Assert.Equal(502, ex.StatusCode); Assert.DoesNotContain("test-secret", ex.Message);
      transport.Failure = new OperationCanceledException();
      var timeout = await Assert.ThrowsAsync<ActionException>(() => service.SendAsync(Email(), TestContext.Current.CancellationToken));
      Assert.Equal(504, timeout.StatusCode);
      using var cts = new CancellationTokenSource(); cts.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SendAsync(Email(), cts.Token));
   }

   [Fact]
   public void RegistrationDeclaresSeparateClaimsAndDoesNotExposeInternalSend() {
      var builder = new EmAppBuilder { Services = new ServiceCollection() };
      builder.AddSmtp();
      Assert.Throws<InvalidOperationException>(() => builder.AddSmtp());
      Assert.Equal(5, builder.ActionDefinitions.Count);
      Assert.DoesNotContain(builder.ActionDefinitions, action => action.MethodInfo.Name == nameof(ISmtpService.SendAsync));
      Assert.Contains(builder.ClaimActions, claim => claim.Name == ISmtpService.ManagerClaim);
      Assert.Contains(builder.ClaimActions, claim => claim.Name == ISmtpService.SendClaim);
      Assert.All(builder.ActionDefinitions, action => Assert.False(action.IsPublicAction));
      Assert.Equal(ISmtpService.SendClaim, builder.ActionDefinitions.Single(action => action.ActionName == nameof(ISmtpService.PostGetMeta_SmtpSend)).RequiredClaim);
      var json = JsonSerializer.Serialize(new SmtpSettingsDetail { HasPassword = true });
      Assert.DoesNotContain("test-secret", json);
   }

   private sealed class FakeStore : ISmtpSettingsStore
   {
      public SmtpSettings Settings { get; } = Config();
      public Task<(SmtpSettings Settings, string? Password)> CredentialsAsync(CancellationToken ct) {
         ct.ThrowIfCancellationRequested(); return Task.FromResult<(SmtpSettings, string?)>((Settings, "test-secret"));
      }
      public Task<SmtpSettingsDetail> ReadAsync(CancellationToken ct) => Task.FromResult(new SmtpSettingsDetail { Settings = Settings });
      public Task<SmtpSettingsDetail> SaveAsync(SmtpSettingsSave request, CancellationToken ct) => throw new NotImplementedException();
   }
   private sealed class FakeTransport : ISmtpTransport
   {
      public Exception? Failure { get; set; }
      public int Calls { get; private set; }
      public bool LastWasDiagnostic { get; private set; }
      public Task<bool> ExecuteAsync(SmtpSettings settings, string? password, MimeMessage? message, CancellationToken ct) {
         Calls++; LastWasDiagnostic = message is null;
         if (Failure is not null) return Task.FromException<bool>(Failure);
         return Task.FromResult(true);
      }
   }
}
