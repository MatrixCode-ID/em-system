using System.Net;
using System.Text;
using System.Text.Json;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Ui.Core.Tests;

public class SmtpClientServiceTests
{
   [Fact]
   public async Task ProxyMapsEveryActionAndFollowsTheActiveConnection() {
      using var firstHandler = new ResponseHandler(); using var secondHandler = new ResponseHandler();
      using var first = ApiClient.Create(new() { Host = "https://first.example.com", Timeout = 30 }, firstHandler);
      using var second = ApiClient.Create(new() { Host = "https://second.example.com", Timeout = 30 }, secondHandler);
      ApiClient? active = first;
      var service = new SmtpClientService(new StubApp(), () => active);
      Assert.Equal(4, (await service.GetMeta_SmtpSettings()).Revision);
      await service.PostGetMeta_SmtpSettingsSave(new() { ExpectedRevision = 4, Password = "replacement-test-secret" });
      var save = Argument<SmtpSettingsSave>(firstHandler.Body!);
      Assert.Equal(4, save!.ExpectedRevision); Assert.Equal("replacement-test-secret", save.Password);
      Assert.Equal("/api/Administrative%20Tools/PostGetMeta_SmtpSettingsSave", firstHandler.Path);
      await service.PostGetMeta_SmtpTestConnection();
      Assert.Equal("POST", firstHandler.Method);
      Assert.Empty(JsonSerializer.Deserialize<PostMethodPayload[]>(firstHandler.Body!)!);
      await service.PostGetMeta_SmtpTestEmail("recipient@example.com");
      Assert.Equal("recipient@example.com", Argument<string>(firstHandler.Body!));
      active = second;
      var sent = await service.SendAsync(new() { To = ["recipient@example.com"], Subject = "Test", TextBody = "Body" });
      Assert.Equal("accepted", sent.MessageId);
      Assert.Equal("/api/Administrative%20Tools/PostGetMeta_SmtpSend", secondHandler.Path);
      Assert.Equal("second.example.com", secondHandler.Host);
      var message = Argument<SmtpMessage>(secondHandler.Body!);
      Assert.Equal("Body", message!.TextBody);
      active = null;
      await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetMeta_SmtpSettings());
   }

   private static T Argument<T>(string body) {
      var payload = JsonSerializer.Deserialize<PostMethodPayload[]>(body)!.Single();
      Assert.Equal(0, payload.ParameterOrdinal);
      Assert.True(payload.ConstructObject<T>(out var value, out var error), error);
      return value;
   }

   private sealed class StubApp : IEmApp, IServiceProvider
   {
      public IServiceProvider ServiceProvider => this;
      public object? GetService(Type serviceType) => null;
      public Task<DateTime> GetDateStampAsync() => Task.FromResult(DateTime.UtcNow);
   }
   private sealed class ResponseHandler : HttpClientHandler
   {
      public string? Path { get; private set; }
      public string? Host { get; private set; }
      public string? Method { get; private set; }
      public string? Body { get; private set; }
      protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
         Path = request.RequestUri!.AbsolutePath; Host = request.RequestUri.Host; Method = request.Method.Method;
         Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
         object result = Path.EndsWith("GetMeta_SmtpSettings") || Path.EndsWith("PostGetMeta_SmtpSettingsSave")
            ? new SmtpSettingsDetail { Revision = 4 }
            : Path.EndsWith("PostGetMeta_SmtpTestConnection") ? new SmtpConnectionResult { IsSecure = true }
            : new SmtpSendResult { MessageId = "accepted" };
         return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new ActionResult {
            ValidResult = true, HasData = true, Data = result, StatusCode = 200
         }), Encoding.UTF8, "application/json") };
      }
   }
}
