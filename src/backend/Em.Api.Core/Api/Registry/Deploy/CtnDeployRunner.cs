using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Registry.Deploy
{
   /// <summary>
   /// Runs deploys and records them in the history. One deploy per target at a time inside this process (a second
   /// request gets 409); like garbage collection this assumes one API instance per registry. A deploy has its own
   /// 15-minute limit and ignores the request's abort token, so a client that disconnects does not leave a
   /// half-recreated container behind; the run is always closed.
   /// </summary>
   internal sealed class CtnDeployRunner(ICtnDeployTransports transports)
   {
      /// <summary>Longest a deploy may take on the server.</summary>
      public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

      private readonly ConcurrentDictionary<string, byte> _busy = new(StringComparer.Ordinal);

      public CtnDeployExecutor Executor { get; } = new(transports);

      /// <summary>Runs <paramref name="work"/> while holding the lock of target <paramref name="deployId"/>.</summary>
      /// <exception cref="ActionException">409 while another deploy of the target runs.</exception>
      public async Task<T> ExclusiveAsync<T>(string deployId, Func<CancellationToken, Task<T>> work) {
         if (!_busy.TryAdd(deployId, 0)) throw new ActionException("A deploy of this container is already running; wait for it to finish.", 409);
         try {
            using var timeout = new CancellationTokenSource(Timeout);
            return await work(timeout.Token);
         }
         finally {
            _busy.TryRemove(deployId, out _);
         }
      }

      /// <summary>
      /// Deploys <paramref name="digest"/> to <paramref name="target"/> and records the run. A failure is recorded and
      /// answered as <see cref="CtnDeployResult.Failed"/>, not thrown.
      /// </summary>
      public Task<CtnDeployRunInfo> RunAsync(CtnContext db, ta_CtnDeploy row, CtnDeployTarget target, string repository,
         CtnDeployTrigger trigger, string digest, string? tag, string? userId, string? userName) =>
         ExclusiveAsync(row.cCtnDeployId, async ct => {
            var now = DateTime.UtcNow;
            var run = new ta_CtnDeployRun {
               cCtnDeployRunId = $"{Ulid.NewUlid()}", cCtnDeployId = row.cCtnDeployId, cCtnDeployRunTrigger = (int)trigger,
               cCtnDeployRunTag = tag, cCtnDeployRunDigest = digest, cCtnDeployRunResult = (int)CtnDeployResult.Running,
               cCtnDeployRunBy_cUserId = userId, cCtnDeployRunStarted = now, ustamp = now, datestamp = now
            };
            db.DeployRuns.Add(run);
            await db.SaveChangesAsync(CancellationToken.None);

            var log = new CtnDeployLog(target.Secrets);
            log.Line($"{trigger} deploy started" + (userName is null ? "." : $" by {userName}."));
            try {
               var outcome = await Executor.DeployAsync(target, repository, digest, log, ct);
               run.cCtnDeployRunResult = (int)CtnDeployResult.Success;
               run.cCtnDeployRunPrevDigest = outcome.PrevDigest;
               run.cCtnDeployRunOldFile = outcome.OldFile;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
               log.Line($"Stopped: the deploy took longer than {Timeout.TotalMinutes:0} minutes.");
               run.cCtnDeployRunResult = (int)CtnDeployResult.Failed;
            }
            catch (Exception x) {
               log.Line("Failed: " + x.Message);
               run.cCtnDeployRunResult = (int)CtnDeployResult.Failed;
            }

            run.cCtnDeployRunOutput = log.ToString();
            run.cCtnDeployRunFinished = run.ustamp = DateTime.UtcNow;
            db.UpdateRow(run);
            await db.SaveChangesAsync(CancellationToken.None);
            return CtnDeployStore.ToInfo(run, row.cCtnImageId, userName, canRollback: false);
         });

      /// <summary>Marks runs left <c>Running</c> by a stopped API as failed when they are older than the deploy limit.</summary>
      public static async Task CloseAbandonedAsync(CtnContext db, string deployId) {
         var cutoff = DateTime.UtcNow - Timeout - TimeSpan.FromMinutes(1);
         var now = DateTime.UtcNow;
         await db.DeployRuns
            .Where(r => r.cCtnDeployId == deployId && r.cCtnDeployRunResult == (int)CtnDeployResult.Running && r.cCtnDeployRunStarted < cutoff)
            .ExecuteUpdateAsync(s => s
               .SetProperty(r => r.cCtnDeployRunResult, (int)CtnDeployResult.Failed)
               .SetProperty(r => r.cCtnDeployRunFinished, now)
               .SetProperty(r => r.ustamp, now));
      }
   }
}
