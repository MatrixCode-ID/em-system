using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Api.Core.Registry.Deploy;
using Em.Shared;

namespace Em.Api.Core.Registry
{
   // Deploy actions of the registry: target, connection test, Create stack, deploy after push, manual deploy,
   // rollback and history. All behind Container Manager Access. Deploys run synchronously: the request waits up to
   // CtnDeployRunner.Timeout and the client calls them with a longer timeout of its own.
   public partial class CtnServices
   {
      private CtnDeployStore DeployStore => new(Db, GetService<CtnDeploySecrets>()!) { UserNames = UserNamesAsync };

      private CtnDeployRunner DeployRunner => GetService<CtnDeployRunner>()!;

      #region Deploy

      /// <inheritdoc />
      [GetAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnDeployTargetInfo?> GetMeta_CtnDeployTarget(string imageId) {
         var store = DeployStore;
         await RequireImageAsync(Db, imageId);
         return await store.GetAsync(imageId, AbortToken);
      }

      /// <inheritdoc />
      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnDeployTargetInfo> PostGetMeta_CtnDeployTargetSave(CtnDeployTargetSave request) {
         var store = DeployStore;
         var row = await store.SaveAsync(request ?? throw new ActionException("The request is empty.", 400));
         return (await store.GetAsync(row.cCtnImageId))!;
      }

      /// <inheritdoc />
      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task PostMeta_CtnDeployTargetDelete(string imageId) {
         if (!await DeployStore.DeleteAsync(imageId)) throw new ActionException("This container has no deploy target.", 404);
      }

      /// <inheritdoc />
      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnDeployTestResult> PostGetMeta_CtnDeployTest(CtnDeployTargetSave request) {
         var store = DeployStore;
         await RequireImageAsync(Db, request?.ImageId ?? "");
         var target = await store.ToTargetAsync(request!);
         using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
         return await DeployRunner.Executor.TestAsync(target, timeout.Token);
      }

      /// <inheritdoc />
      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnDeployTestResult> PostGetMeta_CtnDeployRegisterPortainerRegistry(string imageId) {
         var store = DeployStore;
         var row = await RequireTargetAsync(store, imageId);
         if (row.cCtnDeployKind != (int)CtnDeployKind.Portainer) throw new ActionException("This is not a Portainer target.", 400);

         var target = await store.ToTargetAsync(row);
         using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
         try {
            await DeployRunner.Executor.RegisterPortainerRegistryAsync(target, timeout.Token);
         }
         catch (Exception x) when (x is not ActionException) {
            return new CtnDeployTestResult { Messages = ["Registering the registry failed: " + new CtnDeployLog(target.Secrets).Mask(x.Message)] };
         }

         return await DeployRunner.Executor.TestAsync(target, timeout.Token);
      }

      /// <inheritdoc />
      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnDeployRunInfo> PostGetMeta_CtnDeployCreateStack(string imageId, string composeContent) {
         var store = DeployStore;
         var row = await RequireTargetAsync(store, imageId);
         if (string.IsNullOrWhiteSpace(composeContent)) throw new ActionException("The compose file is empty.", 400);

         var target = await store.ToTargetAsync(row);
         var repository = await store.RepositoryAsync(imageId);
         var latest = await store.LatestDigestAsync(imageId);
         var log = new CtnDeployLog(target.Secrets);
         var started = DateTime.UtcNow;
         var result = CtnDeployResult.Success;
         await DeployRunner.ExclusiveAsync(row.cCtnDeployId, async ct => {
            try {
               var created = await DeployRunner.Executor.CreateStackAsync(target, repository, latest, composeContent, log, ct);
               await store.SaveCreatedAsync(row, created, CancellationToken.None);
            }
            catch (Exception x) when (x is not ActionException) {
               log.Line("Failed: " + x.Message);
               result = CtnDeployResult.Failed;
            }

            return 0;
         });

         // Not a deploy, so it is not stored in the history; the output goes back to the dialog.
         return new CtnDeployRunInfo {
            ImageId = imageId, Trigger = CtnDeployTrigger.Manual, Digest = latest ?? "", Result = result, Output = log.ToString(),
            Started = started, Finished = DateTime.UtcNow
         };
      }

      /// <inheritdoc />
      [GetAction(claim: ICtnServices.CtnClaim)]
      public async Task<string> GetMeta_CtnDeployStackTemplate(string imageId) {
         var store = DeployStore;
         var repository = await store.RepositoryAsync(imageId, AbortToken);
         var row = await store.FindAsync(imageId, AbortToken);
         var name = ComposeImageRewriter.SafeName(repository.Split('/')[1]);
         var variable = row?.cCtnDeployImageVar ?? ComposeImageRewriter.DefaultVariable(name);
         return $$"""
                  services:
                    {{name}}:
                      image: ${{{variable}}}
                      container_name: {{name}}
                      restart: unless-stopped

                  """;
      }

      /// <inheritdoc />
      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnDeployRunInfo> PostGetMeta_CtnDeployAfterPush(CtnDeployPushRequest request) {
         var store = DeployStore;
         var resolution = await store.ResolvePushAsync(request ?? throw new ActionException("The request is empty.", 400));
         if (resolution.Row is not { } row) return CtnDeployStore.Skipped(resolution, request.Digest);

         var target = await store.ToTargetAsync(row);
         return await DeployRunner.RunAsync(Db, row, target, resolution.Repository, CtnDeployTrigger.AfterPush, request.Digest,
            resolution.Tag, CallerUserId, await CallerNameAsync());
      }

      /// <inheritdoc />
      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnDeployRunInfo> PostGetMeta_CtnDeployRun(string imageId, string digest, string? tag) {
         var store = DeployStore;
         var row = await RequireTargetAsync(store, imageId);
         if (!CtnNames.IsValidDigest(digest)) throw new ActionException("Digest must be sha256:<64 hex>.", 400);
         await store.RequireManifestAsync(imageId, digest);

         var target = await store.ToTargetAsync(row);
         return await DeployRunner.RunAsync(Db, row, target, await store.RepositoryAsync(imageId), CtnDeployTrigger.Manual, digest,
            string.IsNullOrWhiteSpace(tag) ? null : tag.Trim(), CallerUserId, await CallerNameAsync());
      }

      /// <inheritdoc />
      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnDeployRunInfo> PostGetMeta_CtnDeployRollback(string imageId, string runId) {
         var store = DeployStore;
         var row = await RequireTargetAsync(store, imageId);
         var previous = await store.FindRunAsync(row, runId) ?? throw new ActionException("Deploy run was not found.", 404);
         if (previous.cCtnDeployRunResult != (int)CtnDeployResult.Success) {
            throw new ActionException("Only a successful deploy can be rolled back to.", 400);
         }

         await store.RequireManifestAsync(imageId, previous.cCtnDeployRunDigest);
         var target = await store.ToTargetAsync(row);
         return await DeployRunner.RunAsync(Db, row, target, await store.RepositoryAsync(imageId), CtnDeployTrigger.Rollback,
            previous.cCtnDeployRunDigest, previous.cCtnDeployRunTag, CallerUserId, await CallerNameAsync());
      }

      /// <inheritdoc />
      [GetAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnDeployRunInfo[]> GetMeta_CtnDeployRuns(string imageId, int take) {
         if (take is < 1 or > ICtnServices.DeployMaxRuns) throw new ActionException($"Take must be 1-{ICtnServices.DeployMaxRuns}.", 400);
         var store = DeployStore;
         var row = await RequireTargetAsync(store, imageId);
         await CtnDeployRunner.CloseAbandonedAsync(Db, row.cCtnDeployId);
         return await store.RunsAsync(row, take, AbortToken);
      }

      #endregion

      private async Task<ta_CtnDeploy> RequireTargetAsync(CtnDeployStore store, string imageId) {
         await RequireImageAsync(Db, imageId);
         return await store.FindAsync(imageId) ?? throw new ActionException("This container has no deploy target.", 404);
      }

      private async Task<string?> CallerNameAsync() =>
         CallerUserId is { } id ? (await UserNamesAsync([id])).GetValueOrDefault(id) : null;

      private async Task<Dictionary<string, string>> UserNamesAsync(IReadOnlyCollection<string> ids) {
         if (ids.Count == 0) return [];
         var users = GetService<ApiCoreContext>()!;
         return await users.ta_Users.Where(u => ids.Contains(u.cUserId)).ToDictionaryAsync(u => u.cUserId, u => u.cUserAccount);
      }
   }
}
