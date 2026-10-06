using System.Security.Cryptography;
using Em.Api.Core;
using Em.Shared;
using Em.Test.Models;
using Microsoft.EntityFrameworkCore;

namespace Em.Test.Api
{
   public partial class TestServices
   {
      private const int MaxDownloadKb = 50 * 1024;
      private const int MaxWaitSeconds = 60;

      #region Meta's

      [GetAction(IsPublicAction = true)]
      public Task<string> GetMeta_TestPublicPing() =>
         Task.FromResult($"pong (public) {DateTime.UtcNow:O}");

      [GetAction]
      public Task<string> GetMeta_TestPing() =>
         Task.FromResult($"pong {Request.cUserAccount ?? "(no user)"} {DateTime.UtcNow:O}");

      [GetAction]
      public Task<TestSessionInfo> GetMeta_TestSession() =>
         Task.FromResult(new TestSessionInfo {
            UserId = Request.cUserId,
            Account = Request.cUserAccount,
            IsAdmin = Request.IsAdmin,
            IsDebugRequest = Request.IsDebugRequest,
            Source = Request.Source.ToString(),
            Claims = [.. Request.Claims.Select(r => r.Key).Order(StringComparer.OrdinalIgnoreCase)]
         });

      [GetAction]
      public Task<TestEchoResult> GetMeta_TestEchoSimple(string text, int number, decimal amount, bool flag,
         DateTime when, TestItemState state) =>
         Task.FromResult(Echo("GET simple parameters", new TestEchoRequest {
            Text = text, Number = number, Amount = amount, Flag = flag, When = when, State = state
         }));

      [PostAction]
      public Task<TestEchoResult> PostGetMeta_TestEcho(TestEchoRequest request, string[] tags) {
         request.Tags = tags;
         return Task.FromResult(Echo("POST positional body", request));
      }

      [GetAction]
      public Task<string> GetMeta_TestFail(int status) {
         // Zero stands for the failure nobody planned for: it must reach the caller as a plain 500
         // that does not reveal the message, unlike the deliberate ones below.
         if (status <= 0) throw new InvalidOperationException("Unplanned failure raised by GetMeta_TestFail.");
         if (status is < 400 or > 599) throw new ActionException("The status to raise must be 400 to 599, or 0.", 400);
         throw new ActionException($"Deliberate failure with status {status}.", status);
      }

      [GetAction(5)]
      public async Task<string> GetMeta_TestSlow(int seconds) {
         seconds = Math.Clamp(seconds, 0, MaxWaitSeconds);
         await Task.Delay(TimeSpan.FromSeconds(seconds), AbortToken);
         return $"Waited {seconds}s and was not cancelled.";
      }

      [GetAction(-1)]
      public async Task<string> GetMeta_TestSlowUnlimited(int seconds) {
         seconds = Math.Clamp(seconds, 0, MaxWaitSeconds);
         await Task.Delay(TimeSpan.FromSeconds(seconds), AbortToken);
         return $"Waited {seconds}s with no server time limit.";
      }

      [GetAction(claim: ITestServices.ProbeClaim)]
      public Task<string> GetMeta_TestClaimGated() =>
         Task.FromResult($"Allowed: {Request.cUserAccount ?? "debug"} holds '{ITestServices.ProbeClaim}' or is an administrator.");

      [GetAction]
      public Task<string> GetMeta_TestAdminOnly() {
         Request.RequireAdmin();
         return Task.FromResult("Allowed: the caller is an administrator.");
      }

      [GetAction]
      public Task<string> GetMeta_TestSelfOrAdmin(string cUserId) {
         Request.RequireSelfOrAdmin(cUserId);
         return Task.FromResult($"Allowed: the caller is {cUserId} or an administrator.");
      }

      [PostAction]
      public async Task<TestStreamResult> PostGetMeta_TestStreamUpload(TestStreamRequest request, Stream content) {
         using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
         var buffer = new byte[81920];
         long length = 0;

         int read;
         while ((read = await content.ReadAsync(buffer, AbortToken)) > 0) {
            hash.AppendData(buffer, 0, read);
            length += read;
         }

         return new TestStreamResult {
            Name = request.Name,
            Length = length,
            Sha256 = Convert.ToHexString(hash.GetHashAndReset())
         };
      }

      [GetAction]
      public Task<Stream> GetMeta_TestStreamDownload(int kilobytes) {
         kilobytes = Math.Clamp(kilobytes, 1, MaxDownloadKb);
         return Task.FromResult<Stream>(new PatternStream(kilobytes * 1024L));
      }

      [GetAction]
      public Task<Stream> GetMeta_TestPdfSample(int pages) =>
         Task.FromResult(TestPdf.Sample(Math.Clamp(pages, 1, 50)));

      [PostAction]
      public async Task<int> PostGetMeta_TestSeedItems() {
         var wanted = Enumerable.Range(1, 10).Select(i => $"TST-{i:000}").ToArray();
         var existing = (await ctx.ta_TestItems.Where(r => wanted.Contains(r.cTestItemCode))
            .Select(r => r.cTestItemCode).ToListAsync(AbortToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
         var now = DateTime.UtcNow;
         var rows = wanted.Where(code => !existing.Contains(code)).Select((code, i) => new ta_TestItem {
            cTestItemId = $"{Ulid.NewUlid()}",
            cTestItemCode = code,
            cTestItemName = $"Sample item {code}",
            cTestItemQty = 10 * (i + 1),
            cTestItemPrice = 1000m * (i + 1),
            cTestItemState = i % 4 == 3 ? TestItemState.Disabled : TestItemState.Active,
            cTestItemNote = "Created by PostGetMeta_TestSeedItems",
            ustamp = now,
            datestamp = now
         }).ToArray();

         ctx.ta_TestItems.AddRange(rows);
         await ctx.SaveChangesAsync();
         return rows.Length;
      }

      #endregion

      private TestEchoResult Echo(string via, TestEchoRequest received) => new() {
         Received = received,
         Via = via,
         CallerAccount = Request.cUserAccount,
         ServerTimeUtc = DateTime.UtcNow
      };

      /// <summary>
      /// Stream baca-saja berisi byte yang ditentukan posisinya, tanpa menampung isinya di memori. Dipakai
      /// untuk unduhan besar: yang diuji adalah jalur streaming-nya, bukan isinya.
      /// </summary>
      private sealed class PatternStream(long length) : Stream
      {
         private long _position;

         public override bool CanRead => true;

         public override bool CanSeek => false;

         public override bool CanWrite => false;

         public override long Length => length;

         public override long Position {
            get => _position;
            set => throw new NotSupportedException();
         }

         public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

         public override int Read(Span<byte> buffer) {
            var take = (int)Math.Min(buffer.Length, length - _position);
            for (var i = 0; i < take; i++) buffer[i] = (byte)((_position + i) % 251);
            _position += take;
            return take;
         }

         public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

         public override void Flush() { }

         public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

         public override void SetLength(long value) => throw new NotSupportedException();

         public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
      }
   }
}
