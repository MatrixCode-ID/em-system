using Em.Api.Core.Log;
using Em.Api.Core.Models;
using Microsoft.Extensions.Logging;

namespace Em.Api.Core.Tests.Log;

public class LogProviderTests
{
   private static (LogQueue Queue, ILoggerFactory Factory, LogRuntime Runtime) Create(int capacity = 1000, bool startLines = false) {
      var runtime = new LogRuntime("Tests", "Test");
      var queue = new LogQueue(capacity, runtime);
      // The first provider of the process takes what other tests left in the bootstrap buffer.
      BootstrapLogBuffer.TakeAll();
      var provider = new EmLogProvider(queue, runtime, startLines);
      var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
      return (queue, factory, runtime);
   }

   [Fact]
   public void TemplateAndPropertiesAreKept() {
      var (queue, factory, _) = Create();
      factory.CreateLogger("Em.Test").LogInformation("Saved {Count} rows for {Account}", 3, "budi");
      var e = Assert.Single(queue.Drain(10));
      Assert.Equal("Saved {Count} rows for {Account}", e.Template);
      Assert.Equal("Saved 3 rows for budi", e.Message);
      Assert.Contains("\"Count\":3", e.Properties);
      Assert.Equal("budi", e.Account);
      Assert.Equal(LogEventKind.Diagnostic, e.Kind);
      Assert.Equal("Tests", e.App);
   }

   [Fact]
   public void ScopeValuesBecomeEnvelopeFieldsAndInnerScopeWins() {
      var (queue, factory, _) = Create();
      var logger = factory.CreateLogger("Em.Test");
      using (logger.BeginScope(new Dictionary<string, object?> { ["RequestId"] = "outer" }))
      using (logger.BeginScope(new Dictionary<string, object?> { ["RequestId"] = "srv:0001A:20261012", ["UserId"] = "U1" })) {
         logger.LogWarning("Inside");
      }
      var e = Assert.Single(queue.Drain(10));
      Assert.Equal("srv:0001A:20261012", e.RequestId);
      Assert.Equal("U1", e.UserId);
   }

   [Fact]
   public void ExceptionsAtErrorLevelAreErrorsWithAFingerprint() {
      var (queue, factory, _) = Create();
      try { throw new InvalidOperationException("boom"); }
      catch (Exception ex) { factory.CreateLogger("Em.Test").LogError(ex, "Failed"); }
      var e = Assert.Single(queue.Drain(10));
      Assert.Equal(LogEventKind.Error, e.Kind);
      Assert.NotNull(e.Fingerprint);
      Assert.Contains("boom", e.Exception);
   }

   [Fact]
   public void RequestLinesAreAccessAndStartLinesAreNotStoredByDefault() {
      var (queue, factory, _) = Create();
      var logger = factory.CreateLogger(LogEventFactory.RequestCategory);
      logger.LogInformation("#{Id} {Phase,-6} {Method}", "0001A", "IN", "GET");
      logger.LogInformation("#{Id} {Phase,-6} {Method}", "0001A", "START", "GET");
      logger.LogInformation("#{Id} {Phase,-6} {Method}", "0001A", "DONE", "GET");
      var e = Assert.Single(queue.Drain(10));
      Assert.Equal(LogEventKind.Access, e.Kind);
      Assert.Equal("#0001A DONE   GET", e.Message);
   }

   [Fact]
   public void StartLinesAreStoredWhenAsked() {
      var (queue, factory, _) = Create(startLines: true);
      factory.CreateLogger(LogEventFactory.RequestCategory).LogInformation("#{Id} {Phase,-6}", "1", "IN");
      Assert.Single(queue.Drain(10));
   }

   [Fact]
   public void FullQueueDropsInformationButKeepsWarnings() {
      var (queue, factory, runtime) = Create(capacity: 2);
      var logger = factory.CreateLogger("Em.Test");
      for (var i = 0; i < 5; i++) logger.LogInformation("info {I}", i);
      logger.LogWarning("warning");
      var events = queue.Drain(100);
      Assert.Equal(3, events.Count);
      Assert.Contains(events, e => e.Level == LogEventLevel.Warning);
      Assert.Equal(3, runtime.Dropped);
   }

   [Fact]
   public void SuppressedScopeWritesNothingAndTheLoggerNeverThrows() {
      var (queue, factory, _) = Create();
      var logger = factory.CreateLogger("Em.Test");
      using (LogGuard.Suppress()) logger.LogError("inside the writer");
      logger.Log(LogLevel.Information, default, "state", null, (_, _) => throw new FormatException("bad formatter"));
      var e = Assert.Single(queue.Drain(10));
      Assert.StartsWith("(message could not be formatted", e.Message);
   }
}
