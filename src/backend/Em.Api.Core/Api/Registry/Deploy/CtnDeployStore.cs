using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Registry.Deploy
{
   /// <summary>
   /// Deploy targets and their history in the registry tables: validation, the null/empty/value rule for
   /// credentials, conversion to DTOs without credentials, and resolving what a push should deploy.
   /// </summary>
   internal sealed partial class CtnDeployStore(CtnContext db, CtnDeploySecrets secrets)
   {
      /// <summary>Looks up user accounts by id, for the "by" column of the history.</summary>
      public Func<IReadOnlyCollection<string>, Task<Dictionary<string, string>>> UserNames { get; init; } =
         _ => Task.FromResult(new Dictionary<string, string>());

      [GeneratedRegex(@"^[A-Za-z0-9.\-]+(?::\d{1,5})?$|^\[[0-9A-Fa-f:.]+\](?::\d{1,5})?$")]
      private static partial Regex RegistryHostPattern();

      [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.\-]{0,127}$")]
      private static partial Regex DockerNamePattern();

      #region Target

      public Task<ta_CtnDeploy?> FindAsync(string imageId, CancellationToken ct = default) =>
         db.Deploys.SingleOrDefaultAsync(d => d.cCtnImageId == imageId, ct);

      public async Task<CtnDeployTargetInfo?> GetAsync(string imageId, CancellationToken ct = default) {
         var row = await FindAsync(imageId, ct);
         if (row is null) return null;

         await CtnDeployRunner.CloseAbandonedAsync(db, row.cCtnDeployId);
         var last = (await RunsAsync(row, 1, ct)).FirstOrDefault();
         return ToInfo(row, last);
      }

      /// <summary>Creates or updates the target of <see cref="CtnDeployTargetSave.ImageId"/>.</summary>
      /// <exception cref="ActionException">400 for invalid fields, 404 for an unknown container.</exception>
      public async Task<ta_CtnDeploy> SaveAsync(CtnDeployTargetSave request, CancellationToken ct = default) {
         if (!await db.Images.AnyAsync(i => i.cCtnImageId == request.ImageId, ct)) throw new ActionException("Container was not found.", 404);

         var existing = await FindAsync(request.ImageId, ct);
         var now = DateTime.UtcNow;
         var row = existing ?? new ta_CtnDeploy { cCtnDeployId = $"{Ulid.NewUlid()}", cCtnImageId = request.ImageId, datestamp = now };
         Apply(row, request);
         row.cCtnDeploySecret = await MergeSecretAsync(row.cCtnDeploySecret, request.Secret, ct);
         row.cCtnDeployPassphrase = await MergeSecretAsync(row.cCtnDeployPassphrase, request.Passphrase, ct);
         row.cCtnDeployRegistrySecret = await MergeSecretAsync(row.cCtnDeployRegistrySecret, request.RegistrySecret, ct);
         if (row.cCtnDeployAuth != (int)CtnDeployAuth.SshKey) row.cCtnDeployPassphrase = null;
         row.ustamp = now;

         if (existing is null) db.Deploys.Add(row);
         else db.UpdateRow(row);
         try {
            await db.SaveChangesAsync(ct);
         }
         catch (DbUpdateException) {
            throw new ActionException("The deploy target was changed at the same time; reload and try again.", 409);
         }

         return row;
      }

      /// <summary>Stores where Create stack put the stack; the target switches to Stack mode.</summary>
      public async Task SaveCreatedAsync(ta_CtnDeploy row, CtnDeployCreated created, CancellationToken ct = default) {
         row.cCtnDeployMode = (int)CtnDeployMode.Stack;
         row.cCtnDeployStack = created.Stack;
         row.cCtnDeployStackId = created.StackId ?? row.cCtnDeployStackId;
         row.cCtnDeployEndpointId = created.EndpointId ?? row.cCtnDeployEndpointId;
         row.cCtnDeployService = created.Service;
         row.cCtnDeployImageVar = created.Variable == ComposeImageRewriter.DefaultVariable(created.Service) ? null : created.Variable;
         row.ustamp = DateTime.UtcNow;
         db.UpdateRow(row);
         await db.SaveChangesAsync(ct);
      }

      /// <summary>Deletes the target of a container and its history; <c>false</c> when it had none.</summary>
      public async Task<bool> DeleteAsync(string imageId, CancellationToken ct = default) {
         await using var tx = await db.Database.BeginTransactionAsync(ct);
         var removed = await DeleteForImageAsync(db, imageId, ct);
         await tx.CommitAsync(ct);
         return removed;
      }

      /// <summary>
      /// Deletes target and history of a container inside the caller's transaction. Explicit rather than relying on
      /// the cascading foreign keys, like the other deletes of a container.
      /// </summary>
      public static async Task<bool> DeleteForImageAsync(CtnContext db, string imageId, CancellationToken ct = default) {
         await db.DeployRuns.Where(r => db.Deploys.Any(d => d.cCtnDeployId == r.cCtnDeployId && d.cCtnImageId == imageId)).ExecuteDeleteAsync(ct);
         return await db.Deploys.Where(d => d.cCtnImageId == imageId).ExecuteDeleteAsync(ct) > 0;
      }

      /// <summary>The stored target with its credentials decrypted.</summary>
      public async Task<CtnDeployTarget> ToTargetAsync(ta_CtnDeploy row, CancellationToken ct = default) => new() {
         Kind = (CtnDeployKind)row.cCtnDeployKind, Mode = (CtnDeployMode)row.cCtnDeployMode, Host = row.cCtnDeployHost,
         Port = row.cCtnDeployPort, User = row.cCtnDeployUser, Auth = (CtnDeployAuth)row.cCtnDeployAuth,
         Secret = await secrets.UnprotectAsync(row.cCtnDeploySecret, ct), Passphrase = await secrets.UnprotectAsync(row.cCtnDeployPassphrase, ct),
         Fingerprint = row.cCtnDeployFingerprint, EndpointId = row.cCtnDeployEndpointId, Stack = row.cCtnDeployStack,
         StackId = row.cCtnDeployStackId, Service = row.cCtnDeployService, Container = row.cCtnDeployContainer,
         ImageVariable = row.cCtnDeployImageVar, RegistryHost = row.cCtnDeployRegistryHost, RegistryUser = row.cCtnDeployRegistryUser,
         RegistrySecret = await secrets.UnprotectAsync(row.cCtnDeployRegistrySecret, ct), TagFilter = row.cCtnDeployTagFilter
      };

      /// <summary>
      /// The target described by an unsaved request, for a connection test. Secrets left <c>null</c> come from the
      /// stored target of the same container.
      /// </summary>
      public async Task<CtnDeployTarget> ToTargetAsync(CtnDeployTargetSave request, CancellationToken ct = default) {
         var stored = await FindAsync(request.ImageId, ct);
         var row = new ta_CtnDeploy();
         Apply(row, request);
         return new CtnDeployTarget {
            Kind = (CtnDeployKind)row.cCtnDeployKind, Mode = (CtnDeployMode)row.cCtnDeployMode, Host = row.cCtnDeployHost,
            Port = row.cCtnDeployPort, User = row.cCtnDeployUser, Auth = (CtnDeployAuth)row.cCtnDeployAuth,
            Secret = request.Secret ?? await secrets.UnprotectAsync(stored?.cCtnDeploySecret, ct),
            Passphrase = request.Passphrase ?? await secrets.UnprotectAsync(stored?.cCtnDeployPassphrase, ct),
            Fingerprint = row.cCtnDeployFingerprint, EndpointId = row.cCtnDeployEndpointId, Stack = row.cCtnDeployStack,
            StackId = row.cCtnDeployStackId, Service = row.cCtnDeployService, Container = row.cCtnDeployContainer,
            ImageVariable = row.cCtnDeployImageVar, RegistryHost = row.cCtnDeployRegistryHost, RegistryUser = row.cCtnDeployRegistryUser,
            RegistrySecret = request.RegistrySecret ?? await secrets.UnprotectAsync(stored?.cCtnDeployRegistrySecret, ct),
            TagFilter = row.cCtnDeployTagFilter
         };
      }

      private async Task<byte[]?> MergeSecretAsync(byte[]? stored, string? incoming, CancellationToken ct) => incoming switch {
         null => stored,
         "" => null,
         _ => await secrets.ProtectAsync(incoming, ct)
      };

      // Validates and copies the non-secret fields.
      private static void Apply(ta_CtnDeploy row, CtnDeployTargetSave r) {
         if (!Enum.IsDefined(r.Kind) || !Enum.IsDefined(r.Mode) || !Enum.IsDefined(r.Auth)) throw Bad("Unknown kind, mode or credential type.");

         var host = Trim(r.Host) ?? throw Bad(r.Kind == CtnDeployKind.Ssh ? "Enter the SSH host." : "Enter the Portainer address.");
         if (r.Kind == CtnDeployKind.Ssh) {
            if (r.Auth == CtnDeployAuth.PortainerToken) throw Bad("An SSH target signs in with a private key or a password.");
            if (host.Contains('/') || host.Contains(' ')) throw Bad("Enter the SSH host name or address without a scheme or path.");
            if (r.Port is < 1 or > 65535) throw Bad("SSH port must be 1-65535.");
            if (Trim(r.User) is null) throw Bad("Enter the SSH user.");
            if (r.Mode == CtnDeployMode.Stack && Trim(r.Stack) is { } folder && !folder.StartsWith('/')) {
               throw Bad("The compose folder must be an absolute path on the host, e.g. /opt/stacks/app.");
            }
         }
         else {
            if (r.Auth != CtnDeployAuth.PortainerToken) throw Bad("A Portainer target signs in with an access token.");
            if (!Uri.TryCreate(host, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) {
               throw Bad("Enter the Portainer address as http(s)://host[:port], without credentials.");
            }

            host = host.TrimEnd('/');
         }

         var registry = Trim(r.RegistryHost) ?? throw Bad("Enter the registry host the Docker server pulls from.");
         if (!RegistryHostPattern().IsMatch(registry)) throw Bad("Registry host must be host[:port], without http:// or a path.");
         var variable = Trim(r.ImageVariable);
         if (variable is not null && !ComposeImageRewriter.IsValidVariable(variable)) {
            throw Bad("Image variable must be letters, digits and _, not starting with a digit.");
         }

         var container = Trim(r.Container);
         if (container is not null && !DockerNamePattern().IsMatch(container)) throw Bad("Container name is not a valid Docker container name.");

         row.cCtnDeployState = r.IsActive ? CtnNames.StateActive : CtnNames.StateDisabled;
         row.cCtnDeployKind = (int)r.Kind;
         row.cCtnDeployMode = (int)r.Mode;
         row.cCtnDeployTagFilter = CtnDeployTagFilter.Normalize(r.TagFilter);
         row.cCtnDeployRegistryHost = Max(registry, 255, "Registry host")!;
         row.cCtnDeployRegistryUser = Max(Trim(r.RegistryUser), 128, "Registry username");
         row.cCtnDeployHost = Max(host, 255, "Host")!;
         row.cCtnDeployPort = r.Kind == CtnDeployKind.Ssh ? r.Port ?? 22 : null;
         row.cCtnDeployUser = r.Kind == CtnDeployKind.Ssh ? Max(Trim(r.User), 128, "SSH user") : null;
         row.cCtnDeployAuth = (int)r.Auth;
         row.cCtnDeployFingerprint = Max(Trim(r.Fingerprint), 128, "Fingerprint");
         row.cCtnDeployEndpointId = r.Kind == CtnDeployKind.Portainer ? r.EndpointId : null;
         row.cCtnDeployStack = Max(Trim(r.Stack), 255, "Stack");
         row.cCtnDeployStackId = r.Kind == CtnDeployKind.Portainer ? r.StackId : null;
         row.cCtnDeployService = Max(Trim(r.Service), 128, "Service");
         row.cCtnDeployContainer = container;
         row.cCtnDeployImageVar = variable;
      }

      private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

      private static string? Max(string? value, int length, string what) =>
         value is not null && value.Length > length ? throw Bad($"{what} must not be longer than {length} characters.") : value;

      private static ActionException Bad(string message) => new(message, 400);

      #endregion

      #region Runs

      /// <summary>What a push should deploy, or why it is skipped.</summary>
      public sealed record PushResolution(ta_CtnDeploy? Row, string ImageId, string Repository, string? Tag, string? SkipReason);

      /// <summary>
      /// Finds the container of a pushed repository and decides whether its target deploys: active, and the tag filter
      /// matches one of the pushed tags.
      /// </summary>
      /// <exception cref="ActionException">400 for an invalid digest, 404 when the repository or digest is unknown.</exception>
      public async Task<PushResolution> ResolvePushAsync(CtnDeployPushRequest request, CancellationToken ct = default) {
         if (!CtnNames.IsValidDigest(request.Digest)) throw Bad("Digest must be sha256:<64 hex>.");
         var parts = (request.Repository ?? "").Split('/');
         if (parts.Length != 2) throw Bad("Repository must be root/name.");

         var imageId = await db.Images
            .Where(i => i.cCtnImageName == parts[1] && db.Roots.Any(r => r.cCtnRootId == i.cCtnRootId && r.cCtnRootName == parts[0]))
            .Select(i => i.cCtnImageId).SingleOrDefaultAsync(ct) ?? throw new ActionException($"Container '{request.Repository}' was not found.", 404);
         await RequireManifestAsync(imageId, request.Digest, ct);

         var tags = request.Tags ?? [];
         var row = await FindAsync(imageId, ct);
         if (row is null) return new(null, imageId, request.Repository!, tags.FirstOrDefault(), "This container has no deploy target.");
         if (row.cCtnDeployState != CtnNames.StateActive) {
            return new(null, imageId, request.Repository!, tags.FirstOrDefault(), "Automatic deploy is turned off for this container.");
         }

         var tag = CtnDeployTagFilter.FirstMatch(row.cCtnDeployTagFilter, tags);
         if (tag is null && (tags.Length > 0 || row.cCtnDeployTagFilter is not null)) {
            return new(null, imageId, request.Repository!, tags.FirstOrDefault(),
               $"No pushed tag ({string.Join(", ", tags)}) matches the tag filter '{row.cCtnDeployTagFilter}'.");
         }

         return new(row, imageId, request.Repository!, tag, null);
      }

      /// <summary><c>root/name</c> of a container.</summary>
      public async Task<string> RepositoryAsync(string imageId, CancellationToken ct = default) =>
         await db.Images.Where(i => i.cCtnImageId == imageId)
            .Join(db.Roots, i => i.cCtnRootId, r => r.cCtnRootId, (i, r) => r.cCtnRootName + "/" + i.cCtnImageName)
            .SingleOrDefaultAsync(ct) ?? throw new ActionException("Container was not found.", 404);

      /// <summary>Latest manifest digest of a container, or <c>null</c> when it has none.</summary>
      public Task<string?> LatestDigestAsync(string imageId, CancellationToken ct = default) =>
         db.Manifests.Where(m => m.cCtnImageId == imageId).OrderByDescending(m => m.datestamp)
            .Select(m => m.cCtnManifestDigest).FirstOrDefaultAsync(ct);

      /// <exception cref="ActionException">404 when <paramref name="digest"/> is not a manifest of the container.</exception>
      public async Task RequireManifestAsync(string imageId, string digest, CancellationToken ct = default) {
         if (!await db.Manifests.AnyAsync(m => m.cCtnImageId == imageId && m.cCtnManifestDigest == digest, ct)) {
            throw new ActionException($"Manifest {digest} is not in this container.", 404);
         }
      }

      public Task<ta_CtnDeployRun?> FindRunAsync(ta_CtnDeploy row, string runId, CancellationToken ct = default) =>
         db.DeployRuns.SingleOrDefaultAsync(r => r.cCtnDeployRunId == runId && r.cCtnDeployId == row.cCtnDeployId, ct);

      /// <summary>Latest runs first.</summary>
      public async Task<CtnDeployRunInfo[]> RunsAsync(ta_CtnDeploy row, int take, CancellationToken ct = default) {
         var runs = await db.DeployRuns.Where(r => r.cCtnDeployId == row.cCtnDeployId)
            .OrderByDescending(r => r.cCtnDeployRunStarted).ThenByDescending(r => r.cCtnDeployRunId)
            .Take(take).ToListAsync(ct);
         var latestSuccess = await db.DeployRuns
            .Where(r => r.cCtnDeployId == row.cCtnDeployId && r.cCtnDeployRunResult == (int)CtnDeployResult.Success)
            .OrderByDescending(r => r.cCtnDeployRunStarted).ThenByDescending(r => r.cCtnDeployRunId)
            .Select(r => r.cCtnDeployRunId).FirstOrDefaultAsync(ct);
         var users = await UserNames([.. runs.Select(r => r.cCtnDeployRunBy_cUserId).OfType<string>().Distinct()]);
         return [.. runs.Select(r => ToInfo(r, row.cCtnImageId,
            r.cCtnDeployRunBy_cUserId is { } id ? users.GetValueOrDefault(id) : null,
            r.cCtnDeployRunResult == (int)CtnDeployResult.Success && r.cCtnDeployRunId != latestSuccess))];
      }

      internal static CtnDeployRunInfo ToInfo(ta_CtnDeployRun r, string imageId, string? by, bool canRollback) => new() {
         Id = r.cCtnDeployRunId, ImageId = imageId, Trigger = (CtnDeployTrigger)r.cCtnDeployRunTrigger, Tag = r.cCtnDeployRunTag,
         Digest = r.cCtnDeployRunDigest, PrevDigest = r.cCtnDeployRunPrevDigest, Result = (CtnDeployResult)r.cCtnDeployRunResult,
         Output = r.cCtnDeployRunOutput ?? (r.cCtnDeployRunResult == (int)CtnDeployResult.Failed
            ? "Interrupted: the API stopped before this deploy finished. Check the container on the Docker server."
            : null),
         By = by, Started = r.cCtnDeployRunStarted, Finished = r.cCtnDeployRunFinished, CanRollback = canRollback
      };

      /// <summary>A skipped push: answered to the publisher, not stored.</summary>
      internal static CtnDeployRunInfo Skipped(PushResolution resolution, string digest) => new() {
         ImageId = resolution.ImageId, Trigger = CtnDeployTrigger.AfterPush, Tag = resolution.Tag, Digest = digest,
         Result = CtnDeployResult.Skipped, Output = resolution.SkipReason, Started = DateTime.UtcNow, Finished = DateTime.UtcNow
      };

      internal static CtnDeployTargetInfo ToInfo(ta_CtnDeploy d, CtnDeployRunInfo? last) => new() {
         Id = d.cCtnDeployId, ImageId = d.cCtnImageId, IsActive = d.cCtnDeployState == CtnNames.StateActive,
         Kind = (CtnDeployKind)d.cCtnDeployKind, Mode = (CtnDeployMode)d.cCtnDeployMode, TagFilter = d.cCtnDeployTagFilter,
         RegistryHost = d.cCtnDeployRegistryHost, RegistryUser = d.cCtnDeployRegistryUser, HasRegistrySecret = d.cCtnDeployRegistrySecret is { Length: > 0 },
         Host = d.cCtnDeployHost, Port = d.cCtnDeployPort, User = d.cCtnDeployUser, Auth = (CtnDeployAuth)d.cCtnDeployAuth,
         HasSecret = d.cCtnDeploySecret is { Length: > 0 }, HasPassphrase = d.cCtnDeployPassphrase is { Length: > 0 },
         Fingerprint = d.cCtnDeployFingerprint, EndpointId = d.cCtnDeployEndpointId, Stack = d.cCtnDeployStack, StackId = d.cCtnDeployStackId,
         Service = d.cCtnDeployService, Container = d.cCtnDeployContainer, ImageVariable = d.cCtnDeployImageVar, LastRun = last,
         UpdatedAt = d.ustamp
      };

      #endregion
   }
}
