using System.Net;
using System.Text;
using System.Text.Json;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Ui.Core.Tests;

public class SmtpProfileClientServiceTests
{
   [Fact]
   public async Task ProfileProxyPreservesParameterOrdinalsNullDefaultAndFollowsConnection() {
      using var handler = new Handler(); using var secondHandler = new Handler();
      using var first = ApiClient.Create(new() { Host = "https://first.example.com", Timeout = 30 }, handler);
      using var second = ApiClient.Create(new() { Host = "https://second.example.com", Timeout = 30 }, secondHandler);
      ApiClient? active = first; var service = new SmtpProfileClientService(new App(), () => active);
      await service.GetMeta_SmtpProfiles(); Assert.Equal("GET", handler.Method);
      await service.PostGetMeta_SmtpProfileSave(new() { Name = "named", ExpectedRevision = 7 });
      Assert.Equal("named", handler.Arg<SmtpProfileSave>(0).Name);
      await service.PostGetMeta_SmtpDefault(null, 8);
      Assert.DoesNotContain(JsonSerializer.Deserialize<PostMethodPayload[]>(handler.Body)!, x => x.ParameterOrdinal == 0);
      Assert.Equal(8, handler.Arg<long>(1));
      await service.PostGetMeta_SmtpProfileDelete("id", 9); Assert.Equal("id", handler.Arg<string>(0)); Assert.Equal(9, handler.Arg<long>(1));
      await service.PostGetMeta_SmtpProfileTestConnection("id"); Assert.Equal("id", handler.Arg<string>(0));
      await service.PostGetMeta_SmtpProfileTestEmail("id", "to@example.com"); Assert.Equal("to@example.com", handler.Arg<string>(1));
      await service.PostGetMeta_SmtpProfileTestEmailFrom("id", "sender@example.com", "to@example.com");
      Assert.Equal("id", handler.Arg<string>(0)); Assert.Equal("sender@example.com", handler.Arg<string>(1)); Assert.Equal("to@example.com", handler.Arg<string>(2));
      active = second;
      await service.SendByNameAsync("named", new() { TextBody = "Body" });
      Assert.EndsWith("PostGetMeta_SmtpSendByName", secondHandler.Path); Assert.Equal("second.example.com", secondHandler.Host);
      Assert.Equal("named", secondHandler.Arg<string>(0)); Assert.Equal("Body", secondHandler.Arg<SmtpMessage>(1).TextBody);
      await service.SendByNameAsync(null, new() { TextBody = "Default body" });
      Assert.DoesNotContain(JsonSerializer.Deserialize<PostMethodPayload[]>(secondHandler.Body)!, x => x.ParameterOrdinal == 0);
      Assert.Equal("Default body", secondHandler.Arg<SmtpMessage>(1).TextBody);
      active = null; await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetMeta_SmtpProfiles());
   }
   private sealed class App : IEmApp, IServiceProvider
   {
      public IServiceProvider ServiceProvider => this;
      public object? GetService(Type type) => null;
      public Task<DateTime> GetDateStampAsync() => Task.FromResult(DateTime.UtcNow);
   }
   private sealed class Handler : HttpClientHandler
   {
      public string Path = ""; public string Host = ""; public string Method = ""; public string Body = "";
      public T Arg<T>(int ordinal) {
         var payload = JsonSerializer.Deserialize<PostMethodPayload[]>(Body)!.Single(x => x.ParameterOrdinal == ordinal);
         Assert.True(payload.ConstructObject<T>(out var value, out var error), error); return value!;
      }
      protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
         Path = request.RequestUri!.AbsolutePath; Host = request.RequestUri.Host; Method = request.Method.Method;
         Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
         object data = Path.EndsWith("SmtpProfileSave") ? new SmtpProfileDetail()
            : Path.EndsWith("TestConnection") ? new SmtpConnectionResult()
            : Path.EndsWith("TestEmail") || Path.EndsWith("TestEmailFrom") || Path.EndsWith("SendByName") ? new SmtpSendResult() : new SmtpProfileList();
         return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new ActionResult {
            ValidResult = true, HasData = true, Data = data, StatusCode = 200
         }), Encoding.UTF8, "application/json") };
      }
   }
}
