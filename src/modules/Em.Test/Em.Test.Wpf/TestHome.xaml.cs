using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Em.Shared;
using Em.Test.Models;
using Em.Ui.Core.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Test.Wpf
{
   /// <summary>
   /// The main screen of the test module: a launcher to the other test screens and to the engine's built-in
   /// managers, plus probes for parameter binding, status failures, time limits, claims, and streams. The
   /// Self-test button runs all of them with their expected results.
   /// </summary>
   public partial class TestHome : UserControl, INavigationBody
   {
      public TestHome() {
         InitializeComponent();
      }

      public TestHomeVm Vm => (TestHomeVm)DataContext;

      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReadSessionCommand();

      public Task OnRelease(INavigation sender) => Task.CompletedTask;
   }

   public class TestHomeVm : TestVmBase
   {
      public TestHomeVm() {
         RegisterCommand<string?>(nameof(OpenCommand), OpenCommand);
         RegisterCommand(nameof(ReadSessionCommand), ReadSessionCommand);
         RegisterCommand(nameof(PublicPingCommand), PublicPingCommand);
         RegisterCommand(nameof(PingCommand), PingCommand);
         RegisterCommand(nameof(EchoSimpleCommand), EchoSimpleCommand);
         RegisterCommand(nameof(EchoPostCommand), EchoPostCommand);
         RegisterCommand(nameof(FailCommand), FailCommand);
         RegisterCommand(nameof(SlowCommand), SlowCommand);
         RegisterCommand(nameof(SlowUnlimitedCommand), SlowUnlimitedCommand);
         RegisterCommand(nameof(ClaimGatedCommand), ClaimGatedCommand);
         RegisterCommand(nameof(AdminOnlyCommand), AdminOnlyCommand);
         RegisterCommand(nameof(SelfOrAdminSelfCommand), SelfOrAdminSelfCommand);
         RegisterCommand(nameof(SelfOrAdminOtherCommand), SelfOrAdminOtherCommand);
         RegisterCommand(nameof(UploadCommand), UploadCommand);
         RegisterCommand(nameof(DownloadCommand), DownloadCommand);
         RegisterCommand(nameof(RunSelfTestCommand), RunSelfTestCommand, RunSelfTestCommandAllowed);
      }

      #region Properties

      public string EchoText {
         get => Get("héllo wörld & co. 100%");
         set => Set(value);
      }

      public int EchoNumber {
         get => Get(42);
         set => Set(value);
      }

      public IReadOnlyList<int> FailStatuses { get; } = [0, 400, 401, 403, 404, 409, 422, 500, 503];

      public int FailStatus {
         get => Get(404);
         set => Set(value);
      }

      public int SlowSeconds {
         get => Get(3);
         set => Set(value);
      }

      public int StreamKb {
         get => Get(256);
         set => Set(value);
      }

      public string ClientSummary {
         get => Get(string.Empty) ?? string.Empty;
         private set => Set(value);
      }

      public string ServerSummary {
         get => Get(string.Empty) ?? string.Empty;
         private set => Set(value);
      }

      public string SelfTestSummary {
         get => Get(string.Empty) ?? string.Empty;
         private set => Set(value);
      }

      #endregion

      #region Open

      public async Task OpenCommand(string? name) {
         if (NavigationEntry is null || string.IsNullOrEmpty(name)) return;

         var opened = name == "approval"
            ? await NavigationEntry.NavigateTo(ApprovalManagerNavigationPayload.NavigationName,
               new ApprovalManagerNavigationPayload { WaitingForMeOnly = false })
            : await NavigationEntry.NavigateTo(name);

         if (!opened) Log($"Open '{name}'", "Not opened: unknown navigation, no access to it, or the current screen refused to leave.", true);
      }

      #endregion

      #region Session

      public async Task ReadSessionCommand() {
         await RunAsync("Session: client and server view", async () => {
            var user = EmApp!.ActiveUser;
            var clientClaims = (user?.AvailableClaims ?? [])
               .Where(r => r.ModuleName.Equals(ITestServices.ModuleName, StringComparison.OrdinalIgnoreCase))
               .Select(r => r.Key).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            ClientSummary = new StringBuilder()
               .AppendLine("CLIENT")
               .AppendLine($"  account      : {user?.cUserAccount ?? "(none)"}")
               .AppendLine($"  administrator: {user?.cUserIsAdmin == true}")
               .AppendLine($"  debug mode   : {EmApp.IsDebugMode}")
               .AppendLine($"  simulating   : {EmApp.IsSimulatingLogin}")
               .AppendLine($"  debug bypass : {EmApp.IsDebugBypass}")
               .AppendLine($"  'test' claims: {(clientClaims.Length == 0 ? "(none)" : string.Join(", ", clientClaims))}")
               .AppendLine($"  catalog size : {EmApp.AllClaims.Count} claim(s) known to the client")
               .ToString().TrimEnd();

            var server = await Service.GetMeta_TestSession();
            ServerSummary = new StringBuilder()
               .AppendLine("SERVER")
               .AppendLine($"  account      : {server.Account ?? "(none)"} ({server.UserId})")
               .AppendLine($"  administrator: {server.IsAdmin}")
               .AppendLine($"  debug request: {server.IsDebugRequest} (source {server.Source})")
               .AppendLine($"  claims       : {(server.Claims.Length == 0 ? "(none)" : string.Join(", ", server.Claims))}")
               .ToString().TrimEnd();

            var serverTest = server.Claims
               .Where(r => r.StartsWith(ITestServices.ModuleName + ":", StringComparison.OrdinalIgnoreCase))
               .Order(StringComparer.OrdinalIgnoreCase);
            return clientClaims.SequenceEqual(serverTest, StringComparer.OrdinalIgnoreCase)
               ? "Client and server agree on the claims of this module."
               : "MISMATCH: client and server disagree on the claims of this module.";
         });
         RaiseCommandsChanged();
      }

      public Task ClaimGatedCommand() =>
         RunAsync("Claim gate: 'Run Probes'", () => Service.GetMeta_TestClaimGated());

      public Task AdminOnlyCommand() => RunAsync("Admin only", () => Service.GetMeta_TestAdminOnly());

      public Task SelfOrAdminSelfCommand() =>
         RunAsync("Self or admin: myself", () => Service.GetMeta_TestSelfOrAdmin(MyUserId));

      public Task SelfOrAdminOtherCommand() =>
         RunAsync("Self or admin: someone else", () => Service.GetMeta_TestSelfOrAdmin("someone-else-0000000000000"));

      private string MyUserId => EmApp?.ActiveUser?.cUserId ?? "debug";

      #endregion

      #region Actions

      public Task PublicPingCommand() => RunAsync("Public ping", () => Service.GetMeta_TestPublicPing());

      public Task PingCommand() => RunAsync("Ping", () => Service.GetMeta_TestPing());

      public Task EchoSimpleCommand() =>
         RunAsync("Echo: GET simple parameters", async () => {
            var sent = SampleWhen;
            var result = await Service.GetMeta_TestEchoSimple(EchoText, EchoNumber, 1234.56m, true, sent,
               TestItemState.Disabled);
            return Compare(result.Received, EchoText, EchoNumber, sent) + $"  via {result.Via}";
         });

      public Task EchoPostCommand() =>
         RunAsync("Echo: POST body", async () => {
            var request = SampleRequest();
            var result = await Service.PostGetMeta_TestEcho(request, request.Tags);
            return Compare(result.Received, request.Text, request.Number, request.When, request.Tags) +
                   $"  via {result.Via}";
         });

      public Task FailCommand() =>
         RunAsync($"Raise status {FailStatus}", () => Service.GetMeta_TestFail(FailStatus));

      public Task SlowCommand() =>
         RunAsync($"Slow {SlowSeconds}s with a 5s server limit", () => Service.GetMeta_TestSlow(SlowSeconds));

      public Task SlowUnlimitedCommand() =>
         RunAsync($"Slow {SlowSeconds}s without a server limit", () => Service.GetMeta_TestSlowUnlimited(SlowSeconds));

      private static DateTime SampleWhen => new(2026, 3, 14, 15, 9, 26, DateTimeKind.Utc);

      private TestEchoRequest SampleRequest() => new() {
         Text = EchoText,
         Number = EchoNumber,
         Amount = 98765.43m,
         Flag = true,
         When = SampleWhen,
         State = TestItemState.Disabled,
         Token = Guid.Parse("8d1c6d7e-2f4a-4a37-9d3f-0f1f2e3d4c5b"),
         Tags = ["alpha", "beta & gamma", "ünïcode", ""]
      };

      // Everything that was sent has to come back unchanged; the first difference is what gets reported.
      private static string Compare(TestEchoRequest received, string text, int number, DateTime when,
         string[]? tags = null) {
         var problems = new List<string>();
         if (received.Text != text) problems.Add($"Text '{received.Text}' != '{text}'");
         if (received.Number != number) problems.Add($"Number {received.Number} != {number}");
         if (received.When.ToUniversalTime() != when) problems.Add($"When {received.When:O} != {when:O}");
         if (tags is not null && !received.Tags.SequenceEqual(tags)) problems.Add("Tags differ");
         if (problems.Count > 0) throw new InvalidOperationException("Echo mismatch: " + string.Join("; ", problems));

         return $"Round trip is identical (text, number, date{(tags is null ? "" : ", tags")}).";
      }

      #endregion

      #region Streams

      public Task UploadCommand() =>
         RunAsync($"Upload {StreamKb} KB", async () => {
            var bytes = Pattern(StreamKb * 1024L);
            var localHash = Convert.ToHexString(SHA256.HashData(bytes));
            await using var stream = new MemoryStream(bytes);
            var result = await Service.PostGetMeta_TestStreamUpload(new TestStreamRequest { Name = "upload.bin" }, stream);

            if (result.Length != bytes.Length) {
               throw new InvalidOperationException($"Server received {result.Length:N0} byte(s), sent {bytes.Length:N0}.");
            }

            if (!string.Equals(result.Sha256, localHash, StringComparison.OrdinalIgnoreCase)) {
               throw new InvalidOperationException($"SHA-256 differs: server {result.Sha256}, local {localHash}.");
            }

            return $"{result.Length:N0} byte(s) arrived intact, SHA-256 {result.Sha256[..16]}...";
         });

      public Task DownloadCommand() =>
         RunAsync($"Download {StreamKb} KB", async () => {
            await using var stream = await Service.GetMeta_TestStreamDownload(StreamKb);
            return await VerifyPatternAsync(stream, StreamKb * 1024L);
         });

      // The server's PatternStream writes (position % 251): both sides can check the other without a copy.
      private static byte[] Pattern(long length) {
         var bytes = new byte[length];
         for (long i = 0; i < length; i++) bytes[i] = (byte)((i * 7 + 3) % 251);
         return bytes;
      }

      private static async Task<string> VerifyPatternAsync(Stream stream, long expectedLength) {
         var buffer = new byte[81920];
         long position = 0;
         int read;
         while ((read = await stream.ReadAsync(buffer)) > 0) {
            for (var i = 0; i < read; i++) {
               if (buffer[i] != (byte)((position + i) % 251)) {
                  throw new InvalidOperationException($"Byte {position + i:N0} is {buffer[i]}, expected {(byte)((position + i) % 251)}.");
               }
            }

            position += read;
         }

         if (position != expectedLength) {
            throw new InvalidOperationException($"Received {position:N0} byte(s), expected {expectedLength:N0}.");
         }

         return $"{position:N0} byte(s) received, every byte matches the pattern.";
      }

      #endregion

      #region Self-test

      public async Task RunSelfTestCommand() {
         if (EmApp is null) return;

         IsBusy = true;
         RaiseCommandsChanged();
         var watch = Stopwatch.StartNew();
         int passed = 0, failed = 0;
         SelfTestSummary = "Running...";

         async Task Case(string name, Func<Task<string>> call, int? expectStatus = null, bool expectAnyFailure = false) {
            var caseWatch = Stopwatch.StartNew();
            try {
               var result = await call();
               if (expectStatus is not null || expectAnyFailure) {
                  failed++;
                  Log($"FAIL  {name}", $"Expected a failure ({(expectStatus is null ? "any" : expectStatus.ToString())}) but got: {result}", true,
                     caseWatch.ElapsedMilliseconds);
                  return;
               }

               passed++;
               Log($"PASS  {name}", result, false, caseWatch.ElapsedMilliseconds);
            }
            catch (Exception x) {
               var status = (x as ActionException)?.StatusCode;
               if (expectAnyFailure || (expectStatus is not null && status == expectStatus)) {
                  passed++;
                  Log($"PASS  {name}", $"Refused as expected: {Describe(x)}", false, caseWatch.ElapsedMilliseconds);
               }
               else {
                  failed++;
                  var expectation = expectStatus is null ? "success" : $"status {expectStatus}";
                  Log($"FAIL  {name}", $"Expected {expectation}, got {Describe(x)}", true, caseWatch.ElapsedMilliseconds);
               }
            }
         }

         try {
            var isAdmin = EmApp.IsDebugBypass || EmApp.ActiveUser?.cUserIsAdmin == true;
            var holdsProbe = Holds(ITestServices.ProbeClaim);
            var request = SampleRequest();

            await Case("Public ping", () => Service.GetMeta_TestPublicPing());
            await Case("Ping", () => Service.GetMeta_TestPing());
            await Case("Echo: GET simple parameters", async () => {
               var r = await Service.GetMeta_TestEchoSimple(request.Text, request.Number, request.Amount, true, request.When,
                  TestItemState.Disabled);
               return Compare(r.Received, request.Text, request.Number, request.When);
            });
            await Case("Echo: POST body", async () => {
               var r = await Service.PostGetMeta_TestEcho(request, request.Tags);
               return Compare(r.Received, request.Text, request.Number, request.When, request.Tags);
            });

            foreach (var status in new[] { 400, 403, 404, 409, 422 }) {
               await Case($"Failure {status} reaches the caller as {status}", () => Service.GetMeta_TestFail(status), status);
            }

            await Case("Unplanned exception reaches the caller as 500", () => Service.GetMeta_TestFail(0), 500);
            await Case("Status outside 400-599 is refused with 400", () => Service.GetMeta_TestFail(700), 400);

            await Case("Slow 1s finishes within its 5s limit", () => Service.GetMeta_TestSlow(1));
            await Case("Slow 8s is cut off by its 5s limit", () => Service.GetMeta_TestSlow(8), expectAnyFailure: true);
            await Case("Slow 2s with no server limit finishes", () => Service.GetMeta_TestSlowUnlimited(2));

            if (holdsProbe) await Case("Claim gate: caller holds 'Run Probes'", () => Service.GetMeta_TestClaimGated());
            else await Case("Claim gate: caller lacks 'Run Probes'", () => Service.GetMeta_TestClaimGated(), 403);

            if (isAdmin) await Case("Admin only: caller is an administrator", () => Service.GetMeta_TestAdminOnly());
            else await Case("Admin only: caller is not an administrator", () => Service.GetMeta_TestAdminOnly(), 403);

            await Case("Self or admin: myself", () => Service.GetMeta_TestSelfOrAdmin(MyUserId));
            if (isAdmin) await Case("Self or admin: someone else, as administrator", () => Service.GetMeta_TestSelfOrAdmin("someone-else-0000000000000"));
            else await Case("Self or admin: someone else, not an administrator", () => Service.GetMeta_TestSelfOrAdmin("someone-else-0000000000000"), 403);

            foreach (var kb in new[] { 1, 64, 1024 }) {
               await Case($"Upload {kb} KB arrives intact", async () => {
                  var bytes = Pattern(kb * 1024L);
                  await using var stream = new MemoryStream(bytes);
                  var r = await Service.PostGetMeta_TestStreamUpload(new TestStreamRequest { Name = "selftest.bin" }, stream);
                  if (r.Length != bytes.Length || !string.Equals(r.Sha256, Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase)) {
                     throw new InvalidOperationException("The server received different bytes than were sent.");
                  }

                  return $"{r.Length:N0} byte(s), hash equal";
               });
               await Case($"Download {kb} KB matches the pattern", async () => {
                  await using var stream = await Service.GetMeta_TestStreamDownload(kb);
                  return await VerifyPatternAsync(stream, kb * 1024L);
               });
            }

            await Case("Items: count agrees with the view", async () => {
               var table = await Service.GetTa_TestItems_Count();
               var view = await Service.GetVi_TestItems_Count();
               return table == view ? $"{table} item(s) in both" : throw new InvalidOperationException($"table {table} != view {view}");
            });
            await Case("Items: search returns a page and a total", async () => {
               var page = await Service.GetVi_TestItems_Search(null, null, 1, 3);
               return page.Items.Length <= 3 && page.Total >= page.Items.Length
                  ? $"{page.Items.Length} of {page.Total} item(s)"
                  : throw new InvalidOperationException("Page size or total is inconsistent.");
            });
         }
         finally {
            SelfTestSummary = $"{passed} passed, {failed} failed in {watch.Elapsed.TotalSeconds:N1}s";
            Log("Self-test finished", SelfTestSummary, failed > 0, watch.ElapsedMilliseconds);
            IsBusy = false;
            RaiseCommandsChanged();
         }
      }

      public bool RunSelfTestCommandAllowed() => IsNotBusy;

      #endregion
   }
}
