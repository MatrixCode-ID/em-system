using System.Net;
using System.Net.Sockets;
using System.Text;
using Em.Api.Core.Models;
using Em.Api.Core.Smtp;

namespace Em.Api.Core.Tests;

public class SmtpTransportTests
{
   [Theory]
   [InlineData(false)]
   [InlineData(true)]
   public async Task LocalSmtpReceivesAttachmentAndBccOnlyInEnvelope(bool dropOnQuit) {
      using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      limit.CancelAfter(TimeSpan.FromSeconds(15));
      using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
      var port = ((IPEndPoint)listener.LocalEndpoint).Port;
      var commands = new List<string>(); var data = new StringBuilder();
      var server = ReceiveAsync(listener, commands, data, dropOnQuit, limit.Token);
      var settings = new SmtpSettings { Host = "127.0.0.1", Port = port, Security = SmtpSecurity.None, Authenticate = false, FromAddress = "sender@example.com", Enabled = true };
      using var mime = SmtpValidation.Message(settings, new() {
         To = ["to@example.com"], Cc = ["cc@example.com"], Bcc = ["hidden@example.com"],
         Subject = "Local SMTP test", TextBody = "Hello", HtmlBody = "<p>Hello</p>",
         Attachments = [new() { FileName = "test.txt", Content = [65, 66], ContentType = "text/plain" }]
      });
      Assert.False(await new SmtpTransport().ExecuteAsync(settings, null, mime, limit.Token));
      await server;
      Assert.Contains(commands, command => command.Contains("RCPT TO:<hidden@example.com>", StringComparison.OrdinalIgnoreCase));
      Assert.DoesNotContain("hidden@example.com", data.ToString());
      Assert.DoesNotContain("Bcc:", data.ToString(), StringComparison.OrdinalIgnoreCase);
      Assert.Contains("test.txt", data.ToString());
      Assert.Contains("multipart/alternative", data.ToString());
      Assert.Single(commands, command => command == "DATA");
   }

   [Fact]
   public async Task RequiredStartTlsRefusesServerWithoutTlsBeforeSendingCredentialsOrMail() {
      using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      limit.CancelAfter(TimeSpan.FromSeconds(15));
      using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
      var commands = new List<string>();
      var server = ReceiveAsync(listener, commands, new(), false, limit.Token);
      var settings = new SmtpSettings { Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port,
         Security = SmtpSecurity.StartTls, Authenticate = true, Username = "test-user" };
      await Assert.ThrowsAsync<NotSupportedException>(() => new SmtpTransport().ExecuteAsync(settings, "test-password", null, limit.Token));
      await server;
      Assert.DoesNotContain(commands, command => command.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase));
      Assert.DoesNotContain(commands, command => command.StartsWith("MAIL", StringComparison.OrdinalIgnoreCase));
   }

   private static async Task ReceiveAsync(TcpListener listener, List<string> commands, StringBuilder data, bool dropOnQuit, CancellationToken ct) {
      using var socket = await listener.AcceptTcpClientAsync(ct);
      await using var stream = socket.GetStream();
      using var reader = new StreamReader(stream, Encoding.UTF8, false, leaveOpen: true);
      await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
      await writer.WriteLineAsync("220 localhost SMTP ready");
      var inData = false;
      while (await reader.ReadLineAsync(ct) is { } line) {
         if (inData) {
            if (line != ".") { data.AppendLine(line); continue; }
            inData = false; await writer.WriteLineAsync("250 Message accepted"); continue;
         }
         commands.Add(line);
         if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)) await writer.WriteLineAsync("250 localhost");
         else if (line == "DATA") { inData = true; await writer.WriteLineAsync("354 End with dot"); }
         else if (line == "QUIT") { if (!dropOnQuit) await writer.WriteLineAsync("221 Bye"); return; }
         else await writer.WriteLineAsync("250 OK");
      }
   }
}
