using Em.Api.Core.Models;
using Em.Api.Core.Smtp;
using Em.Api.Shared;
using Em.Shared;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace Em.Api.Core.Tests;

public class SmtpProfileServiceTests
{
   [Fact]
   public async Task NamedSendingAndSelectedDiagnosticsUseSavedProfileAndDoNotFallback() {
      var store = new Store(); var transport = new Transport();
      var sender = new SmtpService(store, transport); var service = new SmtpProfileService(store, sender);
      var message = new SmtpMessage { To = ["recipient@example.com"], Subject = "Test", TextBody = "Body" };
      await ((ISmtpProfileService)service).SendByNameAsync("secondary", message);
      Assert.Equal("secondary", store.Selector); Assert.False(store.ById); Assert.Equal("secondary.example.com", transport.Host);
      await service.PostGetMeta_SmtpProfileTestConnection("profile-id");
      Assert.True(store.ById); Assert.Equal("profile-id", store.Selector); Assert.True(transport.Diagnostic);
      await service.PostGetMeta_SmtpProfileTestEmail("profile-id", "recipient@example.com"); Assert.False(transport.Diagnostic);
      store.NoSender = true;
      await service.PostGetMeta_SmtpProfileTestConnection("profile-id");
      await service.PostGetMeta_SmtpProfileTestEmailFrom("profile-id", "test-sender@example.com", "recipient@example.com");
      Assert.Equal("test-sender@example.com", transport.From);
      var callsBeforeInvalidSender = transport.Calls;
      await Assert.ThrowsAsync<ActionException>(() => service.PostGetMeta_SmtpProfileTestEmailFrom("profile-id", "", "recipient@example.com"));
      Assert.Equal(callsBeforeInvalidSender, transport.Calls);
      store.Disabled = true;
      await service.PostGetMeta_SmtpProfileTestConnection("profile-id");
      Assert.Equal(409, (await Assert.ThrowsAsync<ActionException>(() => service.SendByNameAsync("secondary", message, TestContext.Current.CancellationToken))).StatusCode);
      var count = transport.Calls; store.Missing = true;
      Assert.Equal(404, (await Assert.ThrowsAsync<ActionException>(() => service.SendByNameAsync("missing", message, TestContext.Current.CancellationToken))).StatusCode);
      Assert.Equal(count, transport.Calls);
      using var cts = new CancellationTokenSource(); cts.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SendByNameAsync("secondary", message, cts.Token));
   }

   [Fact]
   public void ExistingActionSignaturesAndClaimsRemainIntactAndNamedSendingHasSendClaim() {
      var builder = new EmAppBuilder { Services = new ServiceCollection() }; builder.AddSmtp();
      var legacy = builder.ActionDefinitions.Where(x => x.MethodInfo.DeclaringType == typeof(SmtpService)).ToArray();
      Assert.Equal(5, legacy.Length);
      var signatures = new Dictionary<string, Type[]> {
         ["GetMeta_SmtpSettings"] = [], ["PostGetMeta_SmtpSettingsSave"] = [typeof(SmtpSettingsSave)],
         ["PostGetMeta_SmtpTestConnection"] = [], ["PostGetMeta_SmtpTestEmail"] = [typeof(string)],
         ["PostGetMeta_SmtpSend"] = [typeof(SmtpMessage)]
      };
      foreach (var action in legacy) {
         Assert.Equal(signatures[action.ActionName], action.MethodInfo.GetParameters().Select(x => x.ParameterType));
         Assert.Equal(action.ActionName == "PostGetMeta_SmtpSend" ? ISmtpService.SendClaim : ISmtpService.ManagerClaim, action.RequiredClaim);
      }
      Assert.Equal(ISmtpService.SendClaim, builder.ActionDefinitions.Single(x => x.ActionName == "PostGetMeta_SmtpSendByName").RequiredClaim);
      Assert.DoesNotContain(builder.ActionDefinitions, x => x.ActionName is "SendAsync" or "SendByNameAsync");
      Assert.Equal(6, typeof(ISmtpService).GetMethods().Length);
   }

   private sealed class Store : ISmtpSettingsStore, ISmtpProfileStore
   {
      public string? Selector; public bool ById; public bool Missing; public bool Disabled; public bool NoSender;
      public Task<(SmtpSettings Settings, string? Password)> SelectedCredentialsAsync(string? selector, bool byId, CancellationToken ct) {
         ct.ThrowIfCancellationRequested(); Selector = selector; ById = byId;
         if (Missing) throw new ActionException("Not found", 404);
         return Task.FromResult<(SmtpSettings, string?)>((new() { Enabled = !Disabled, Authenticate = false, Host = "secondary.example.com", FromAddress = NoSender ? "" : "sender@example.com" }, null));
      }
      public Task<(SmtpSettings Settings, string? Password)> CredentialsAsync(CancellationToken ct) => SelectedCredentialsAsync(null, false, ct);
      public Task<SmtpSettingsDetail> ReadAsync(CancellationToken ct) => throw new NotImplementedException();
      public Task<SmtpSettingsDetail> SaveAsync(SmtpSettingsSave request, CancellationToken ct) => throw new NotImplementedException();
      public Task<SmtpProfileList> ProfilesAsync(CancellationToken ct) => throw new NotImplementedException();
      public Task<SmtpProfileDetail> SaveProfileAsync(SmtpProfileSave request, CancellationToken ct) => throw new NotImplementedException();
      public Task<SmtpProfileList> DeleteAsync(string id, long revision, CancellationToken ct) => throw new NotImplementedException();
      public Task<SmtpProfileList> DefaultAsync(string? id, long revision, CancellationToken ct) => throw new NotImplementedException();
   }
   private sealed class Transport : ISmtpTransport
   {
      public string? Host; public string? From; public bool Diagnostic; public int Calls;
      public Task<bool> ExecuteAsync(SmtpSettings settings, string? password, MimeMessage? message, CancellationToken ct) {
         Host = settings.Host; From = message?.From.Mailboxes.Single().Address; Diagnostic = message is null; Calls++; return Task.FromResult(true);
      }
   }
}
