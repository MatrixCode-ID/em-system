using System.Text.Json;
using System.Text.Json.Nodes;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Registry.Deploy
{
   /// <summary>What a successful deploy found out along the way, for the history.</summary>
   internal sealed class CtnDeployOutcome
   {
      /// <summary>Digest that ran before, when it could be read.</summary>
      public string? PrevDigest { get; set; }

      /// <summary>Compose/stack file before its image line was rewritten to the variable.</summary>
      public string? OldFile { get; set; }
   }

   /// <summary>Where Create stack put the new stack, to store in the target.</summary>
   internal sealed record CtnDeployCreated(string Stack, int? StackId, int? EndpointId, string Service, string Variable);

   /// <summary>
   /// The deploy flows for SSH and Portainer, each as Stack or standalone Container, plus Create stack and the
   /// connection test. Knows nothing about the database; <see cref="CtnDeployRunner"/> records the runs.
   /// </summary>
   internal sealed class CtnDeployExecutor(ICtnDeployTransports transports)
   {
      private static readonly string[] ComposeFiles = ["compose.yml", "compose.yaml", "docker-compose.yml", "docker-compose.yaml"];
      private static readonly TimeSpan DockerTimeout = TimeSpan.FromMinutes(14);

      /// <summary>Single-quotes a value for a POSIX shell.</summary>
      public static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

      #region Deploy

      /// <summary>Pulls <paramref name="digest"/> of <paramref name="repository"/> on the target and recreates the container.</summary>
      /// <exception cref="Exception">Any failure; the log holds the steps done so far.</exception>
      public async Task<CtnDeployOutcome> DeployAsync(CtnDeployTarget t, string repository, string digest, CtnDeployLog log, CancellationToken ct) {
         var outcome = new CtnDeployOutcome();
         log.Line($"Deploying {Reference(t, repository, digest)} via {t.Kind}, mode {t.Mode}.");
         switch (t.Kind, t.Mode) {
            case (CtnDeployKind.Ssh, CtnDeployMode.Stack):
               await SshStackAsync(t, repository, digest, outcome, log, ct);
               break;
            case (CtnDeployKind.Ssh, CtnDeployMode.Container):
               await SshContainerAsync(t, repository, digest, outcome, log, ct);
               break;
            case (CtnDeployKind.Portainer, CtnDeployMode.Stack):
               await PortainerAsync(t, (p, pin) => PortainerStackAsync(p, t, repository, digest, outcome, log, ct));
               break;
            case (CtnDeployKind.Portainer, CtnDeployMode.Container):
               await PortainerAsync(t, (p, _) => RecreateAsync(p.Docker(Require(t.EndpointId, "Portainer environment")), t, repository, digest, outcome, log, ct));
               break;
            default:
               throw new InvalidOperationException($"Unsupported deploy target {t.Kind}/{t.Mode}.");
         }

         log.Line("Deploy finished.");
         return outcome;
      }

      private async Task SshStackAsync(CtnDeployTarget t, string repository, string digest, CtnDeployOutcome outcome, CtnDeployLog log,
         CancellationToken ct) {
         var folder = Require(t.Stack, "compose folder");
         var service = Require(t.Service, "compose service");
         var variable = t.Variable;
         var reference = Reference(t, repository, digest);

         using var shell = await ConnectAsync(t, log, ct);
         var loggedIn = await LoginAsync(shell, t, log, ct);
         try {
            var (file, content) = await FindComposeAsync(shell, folder, ct)
               ?? throw new InvalidOperationException($"No compose file ({string.Join(", ", ComposeFiles)}) in '{folder}'. Use Create stack, or fix the folder.");
            var path = Join(folder, file);
            var rewrite = ComposeImageRewriter.Rewrite(content, service, variable);
            if (rewrite.Changed) {
               outcome.OldFile = content;
               await shell.WriteFileAsync(path, rewrite.Content, ct);
               log.Line($"Rewrote the image of service '{service}' in {path}: '{rewrite.OldImage}' -> '${{{variable}}}'. The old file is kept in this run.");
            }
            else if (!ComposeImageRewriter.ReferencesVariable(rewrite.OldImage, variable)) {
               log.Warn($"The image of service '{service}' is '{rewrite.OldImage}', which does not use ${{{variable}}}; the running image may not change.");
            }

            var envPath = Join(folder, ".env");
            var env = await shell.ReadFileAsync(envPath, ct);
            outcome.PrevDigest = ComposeImageRewriter.DigestOf(ComposeImageRewriter.ReadEnv(env, variable));
            await shell.WriteFileAsync(envPath, ComposeImageRewriter.SetEnv(env, variable, reference), ct);
            log.Line($"Set {variable} in {envPath}" + (outcome.PrevDigest is null ? "." : $" (was {outcome.PrevDigest})."));

            var compose = $"cd {Quote(folder)} && docker compose -f {Quote(file)}";
            await RequireAsync(shell, $"{compose} pull {Quote(service)}", "docker compose pull", log, ct);
            await RequireAsync(shell, $"{compose} up -d {Quote(service)}", "docker compose up -d", log, ct);
         }
         finally {
            if (loggedIn) await LogoutAsync(shell, t, log);
         }
      }

      private async Task SshContainerAsync(CtnDeployTarget t, string repository, string digest, CtnDeployOutcome outcome, CtnDeployLog log,
         CancellationToken ct) {
         using var shell = await ConnectAsync(t, log, ct);
         using var http = new HttpClient(shell.CreateDockerHandler()) { BaseAddress = new Uri("http://docker/"), Timeout = DockerTimeout };
         var docker = new DockerEngineClient(http);
         try {
            log.Line("Docker Engine " + await docker.VersionAsync(ct) + " reached through docker system dial-stdio.");
         }
         catch (Exception x) when (x is not OperationCanceledException) {
            throw new InvalidOperationException(
               $"The Docker Engine API could not be reached through 'docker system dial-stdio' ({x.Message}). Use Stack mode for SSH targets, or update Docker on the host.", x);
         }

         await RecreateAsync(docker, t, repository, digest, outcome, log, ct);
      }

      private static async Task PortainerStackAsync(PortainerClient portainer, CtnDeployTarget t, string repository, string digest,
         CtnDeployOutcome outcome, CtnDeployLog log, CancellationToken ct) {
         var stackId = Require(t.StackId, "Portainer stack");
         var service = Require(t.Service, "compose service");
         var variable = t.Variable;
         var reference = Reference(t, repository, digest);

         var stack = await portainer.StackAsync(stackId, ct);
         var endpointId = t.EndpointId ?? stack["EndpointId"]?.GetValue<int>()
            ?? throw new InvalidOperationException("The stack has no environment; set the Portainer environment of the target.");
         outcome.PrevDigest = ComposeImageRewriter.DigestOf(PortainerClient.GetEnv(stack["Env"], variable));
         var env = PortainerClient.SetEnv(stack["Env"], variable, reference);
         var content = await portainer.StackFileAsync(stackId, ct);
         log.Line($"Stack '{stack["Name"]?.GetValue<string>()}' (id {stackId}) on environment {endpointId}.");

         if (stack["GitConfig"] is JsonObject) {
            if (!ComposeImageRewriter.UsesVariable(content, service, variable)) {
               log.Warn($"This stack is deployed from Git and the image of service '{service}' does not use ${{{variable}}}. A Git file cannot be " +
                        "rewritten here, so the stack is only redeployed with a fresh pull and rollback is not available. " +
                        $"Change the image line in Git to image: ${{{variable}}}.");
            }

            await portainer.RedeployGitStackAsync(stackId, endpointId, stack, env, ct);
            log.Line($"Git stack redeployed with {variable}={reference}, pulling images.");
            return;
         }

         var rewrite = ComposeImageRewriter.Rewrite(content, service, variable);
         if (rewrite.Changed) {
            outcome.OldFile = content;
            content = rewrite.Content;
            log.Line($"Rewrote the image of service '{service}': '{rewrite.OldImage}' -> '${{{variable}}}'. The old file is kept in this run.");
         }
         else if (!ComposeImageRewriter.ReferencesVariable(rewrite.OldImage, variable)) {
            log.Warn($"The image of service '{service}' is '{rewrite.OldImage}', which does not use ${{{variable}}}; the running image may not change.");
         }

         await portainer.UpdateStackAsync(stackId, endpointId, content, env, ct);
         log.Line($"Stack updated with {variable}={reference}, pulling images.");
      }

      /// <summary>
      /// Recreates a standalone container with the new image: pull, create <c>&lt;name&gt;-em-new</c> from the old
      /// settings, stop the old one, swap the names, start the new one, remove the old one. A failure after the old
      /// container was stopped removes the new one and brings the old one back.
      /// </summary>
      internal static async Task RecreateAsync(DockerEngineClient docker, CtnDeployTarget t, string repository, string digest,
         CtnDeployOutcome outcome, CtnDeployLog log, CancellationToken ct) {
         var name = Require(t.Container, "container name");
         var imageRepository = $"{t.RegistryHost}/{repository}";
         var reference = imageRepository + "@" + digest;

         using var old = await docker.InspectContainerAsync(name, ct)
            ?? throw new InvalidOperationException($"Container '{name}' was not found on the Docker server.");
         var oldId = old.RootElement.GetProperty("Id").GetString()!;
         JsonElement? oldImageConfig = null;
         if (old.RootElement.TryGetProperty("Image", out var imageId) && imageId.GetString() is { } oldImageId) {
            using var oldImage = await docker.InspectImageAsync(oldImageId, ct);
            if (oldImage is not null) {
               if (oldImage.RootElement.TryGetProperty("Config", out var config) && config.ValueKind == JsonValueKind.Object) {
                  oldImageConfig = config.Clone();
               }

               if (oldImage.RootElement.TryGetProperty("RepoDigests", out var digests) && digests.ValueKind == JsonValueKind.Array) {
                  outcome.PrevDigest = digests.EnumerateArray().Select(d => d.GetString() ?? "")
                     .FirstOrDefault(d => d.StartsWith(imageRepository + "@", StringComparison.Ordinal)) is { Length: > 0 } match
                     ? ComposeImageRewriter.DigestOf(match)
                     : null;
               }
            }
         }

         string? auth = null;
         if (t.HasRegistryLogin) {
            auth = DockerEngineClient.RegistryAuth(t.RegistryUser!, t.RegistrySecret!, t.RegistryHost);
            log.AddSecret(auth);
         }

         log.Line($"Pulling {reference}...");
         await docker.PullAsync(imageRepository, digest, auth, ct);
         log.Line("Pulled.");

         var plan = DockerRecreatePlan.Build(old.RootElement, reference, oldImageConfig);
         var temporary = name + "-em-new";
         using (var leftover = await docker.InspectContainerAsync(temporary, ct)) {
            if (leftover is not null) {
               log.Line($"Removing '{temporary}' left by an earlier failed deploy.");
               await docker.RemoveAsync(leftover.RootElement.GetProperty("Id").GetString()!, true, ct);
            }
         }

         var newId = await docker.CreateAsync(temporary, plan.Body, ct);
         log.Line($"Created '{temporary}' from the settings of '{name}'.");

         var stopped = false;
         string? oldName = null;
         var swapped = false;
         try {
            foreach (var (network, endpoint) in plan.ExtraNetworks) {
               await docker.ConnectNetworkAsync(network, newId, endpoint, ct);
               log.Line($"Connected network '{network}'.");
            }

            log.Line($"Stopping '{name}'...");
            await docker.StopAsync(oldId, ct);
            stopped = true;
            var backupName = $"{name}-em-old-{DateTime.UtcNow:yyyyMMddHHmmss}";
            await docker.RenameAsync(oldId, backupName, ct);
            oldName = backupName;
            await docker.RenameAsync(newId, name, ct);
            swapped = true;
            await docker.StartAsync(newId, ct);
            log.Line($"Started the new '{name}'.");
         }
         catch (Exception x) {
            log.Line($"Recreate failed: {x.Message} Restoring the old container.");
            await RestoreAsync(docker, name, oldId, newId, stopped, oldName, swapped, log);
            throw;
         }

         try {
            await docker.RemoveAsync(oldId, false, ct);
            log.Line($"Removed the old container ({oldName}).");
         }
         catch (Exception x) when (x is not OperationCanceledException) {
            log.Warn($"The old container '{oldName}' could not be removed: {x.Message}");
         }
      }

      // Runs even when the deploy was cancelled: leaving the service down is worse than finishing the restore.
      private static async Task RestoreAsync(DockerEngineClient docker, string name, string oldId, string newId, bool stopped, string? oldName,
         bool swapped, CtnDeployLog log) {
         var none = CancellationToken.None;
         await Attempt($"Removed the new container{(swapped ? $" '{name}'" : "")}.", () => docker.RemoveAsync(newId, true, none));
         if (oldName is not null) await Attempt($"Renamed '{oldName}' back to '{name}'.", () => docker.RenameAsync(oldId, name, none));
         if (stopped) await Attempt($"Started the old '{name}' again.", () => docker.StartAsync(oldId, none));

         async Task Attempt(string done, Func<Task> step) {
            try {
               await step();
               log.Line(done);
            }
            catch (Exception x) {
               log.Warn($"Restore step failed: {x.Message}");
            }
         }
      }

      #endregion

      #region Create stack

      /// <summary>
      /// Creates the compose file and <c>.env</c> in the SSH folder (not started: Deploy starts it), or creates and
      /// starts a Portainer stack. The image variable is set to <paramref name="latestDigest"/>, or <c>:latest</c>
      /// when the container has no manifest yet.
      /// </summary>
      /// <exception cref="ActionException">409 when a compose file already exists in the SSH folder.</exception>
      public async Task<CtnDeployCreated> CreateStackAsync(CtnDeployTarget t, string repository, string? latestDigest, string content,
         CtnDeployLog log, CancellationToken ct) {
         var services = ComposeImageRewriter.Services(content);
         if (services.Length == 0) throw new ActionException("The compose file has no service.", 400);
         var service = t.Service is { } wanted && services.Contains(wanted) ? wanted : services[0];
         var variable = string.IsNullOrWhiteSpace(t.ImageVariable) ? ComposeImageRewriter.DefaultVariable(service) : t.ImageVariable.Trim();
         var reference = latestDigest is null ? $"{t.RegistryHost}/{repository}:latest" : Reference(t, repository, latestDigest);
         if (!ComposeImageRewriter.UsesVariable(content, service, variable)) {
            log.Warn($"The image of service '{service}' does not use ${{{variable}}}; the first deploy rewrites it.");
         }

         if (t.Kind == CtnDeployKind.Ssh) {
            var folder = Require(t.Stack, "compose folder");
            using var shell = await ConnectAsync(t, log, ct);
            if (await FindComposeAsync(shell, folder, ct) is { } existing) {
               throw new ActionException($"'{Join(folder, existing.File)}' already exists; it is not overwritten. Point the target at it instead.", 409);
            }

            await RequireAsync(shell, $"mkdir -p {Quote(folder)}", "mkdir", log, ct);
            await shell.WriteFileAsync(Join(folder, "compose.yml"), content, ct);
            var envPath = Join(folder, ".env");
            await shell.WriteFileAsync(envPath, ComposeImageRewriter.SetEnv(await shell.ReadFileAsync(envPath, ct), variable, reference), ct);
            log.Line($"Created {Join(folder, "compose.yml")} and set {variable} in .env. Press Deploy to start it.");
            return new CtnDeployCreated(folder, null, null, service, variable);
         }

         var endpointId = Require(t.EndpointId, "Portainer environment");
         var name = Require(t.Stack, "stack name");
         var stackId = await PortainerAsync(t, (portainer, _) =>
            portainer.CreateStackAsync(endpointId, name, content, PortainerClient.SetEnv(null, variable, reference), ct));
         log.Line($"Created and started Portainer stack '{name}' (id {stackId}) on environment {endpointId} with {variable}={reference}.");
         return new CtnDeployCreated(name, stackId, endpointId, service, variable);
      }

      #endregion

      #region Test

      /// <summary>Checks the connection and reads what the target dialog offers to pick. Changes nothing on the server.</summary>
      public async Task<CtnDeployTestResult> TestAsync(CtnDeployTarget t, CancellationToken ct) {
         var result = new CtnDeployTestResult { Success = true };
         var messages = new List<string>();
         var mask = new CtnDeployLog(t.Secrets);
         try {
            if (t.Kind == CtnDeployKind.Ssh) await TestSshAsync(t, result, messages, ct);
            else await TestPortainerAsync(t, result, messages, ct);
         }
         catch (CtnDeployFingerprintException x) {
            result.Success = false;
            result.OfferedFingerprint = x.Offered;
            messages.Add(x.Message);
         }
         catch (Exception x) when (x is not OperationCanceledException and not ActionException) {
            result.Success = false;
            messages.Add("Failed: " + x.Message);
         }

         result.Messages = [.. messages.Select(mask.Mask)];
         return result;
      }

      private async Task TestSshAsync(CtnDeployTarget t, CtnDeployTestResult result, List<string> messages, CancellationToken ct) {
         using var shell = await transports.ConnectSshAsync(t, ct);
         messages.Add($"Connected to {t.User}@{t.Host}:{t.Port ?? 22}; host key matches the pinned fingerprint.");

         var version = await shell.RunAsync("docker version --format '{{.Server.Version}}'", null, ct);
         if (version.ExitCode == 0) {
            messages.Add("Docker " + version.Output.Trim() + ".");
         }
         else {
            result.Success = false;
            messages.Add($"docker version failed (exit {version.ExitCode}): {First(version.Error, version.Output)} The SSH user may need to be in the docker group.");
         }

         if (t.HasRegistryLogin) {
            var log = new CtnDeployLog(t.Secrets);
            try {
               await LoginAsync(shell, t, log, ct);
               await LogoutAsync(shell, t, log);
               result.RegistryLoginOk = true;
               messages.Add($"docker login {t.RegistryHost} succeeded.");
            }
            catch (InvalidOperationException x) {
               result.Success = false;
               messages.Add(x.Message);
            }
         }
         else {
            messages.Add($"No registry login set: the host must already be able to pull from {t.RegistryHost}.");
         }

         if (t.Mode == CtnDeployMode.Stack) {
            var compose = await shell.RunAsync("docker compose version --short", null, ct);
            if (compose.ExitCode == 0) {
               messages.Add("Docker Compose " + compose.Output.Trim() + ".");
            }
            else {
               result.Success = false;
               messages.Add("docker compose is not available on the host; install Docker Compose v2.");
            }

            if (!string.IsNullOrWhiteSpace(t.Stack)) {
               if (await FindComposeAsync(shell, t.Stack, ct) is { } found) {
                  ReadServices(found.Content, t, result, messages, Join(t.Stack, found.File));
               }
               else {
                  messages.Add($"No compose file in '{t.Stack}' yet; use Create stack.");
               }
            }
         }
         else {
            var names = await shell.RunAsync("docker ps -a --format '{{.Names}}'", null, ct);
            if (names.ExitCode == 0) {
               result.Containers = [.. names.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal)];
               messages.Add($"{result.Containers.Length} container(s) on the host.");
               if (!string.IsNullOrWhiteSpace(t.Container) && !result.Containers.Contains(t.Container)) {
                  result.Success = false;
                  messages.Add($"Container '{t.Container}' does not exist on the host.");
               }
            }

            using var http = new HttpClient(shell.CreateDockerHandler()) { BaseAddress = new Uri("http://docker/"), Timeout = TimeSpan.FromSeconds(20) };
            try {
               messages.Add("Docker Engine API " + await new DockerEngineClient(http).VersionAsync(ct) + " reachable through docker system dial-stdio.");
            }
            catch (Exception x) when (x is not OperationCanceledException || !ct.IsCancellationRequested) {
               result.Success = false;
               messages.Add($"docker system dial-stdio did not work ({x.Message}); use Stack mode for this host.");
            }
         }
      }

      private async Task TestPortainerAsync(CtnDeployTarget t, CtnDeployTestResult result, List<string> messages, CancellationToken ct) {
         await PortainerAsync(t, async (portainer, pin) => {
            result.Endpoints = await portainer.EndpointsAsync(ct);
            messages.Add($"Portainer reachable{(pin.Pinned is null ? "" : " (pinned certificate)")}; {result.Endpoints.Length} environment(s).");
            var stacks = await portainer.StacksAsync(ct);
            result.Stacks = t.EndpointId is { } endpoint ? [.. stacks.Where(s => s.EndpointId == endpoint)] : stacks;

            if (t.Mode == CtnDeployMode.Stack && t.StackId is { } stackId) {
               var stack = stacks.FirstOrDefault(s => s.Id == stackId);
               if (stack is null) {
                  result.Success = false;
                  messages.Add($"Stack {stackId} was not found.");
               }
               else {
                  ReadServices(await portainer.StackFileAsync(stackId, ct), t, result, messages, $"stack '{stack.Name}'");
                  if (stack.IsGit && !result.ImageVariablePresent) {
                     messages.Add("The stack comes from Git: its file cannot be rewritten, so rollback needs the image variable in Git.");
                  }
               }
            }

            if (t.Mode == CtnDeployMode.Container && t.EndpointId is { } containerEndpoint) {
               result.Containers = await portainer.Docker(containerEndpoint).ContainerNamesAsync(ct);
               messages.Add($"{result.Containers.Length} container(s) on environment {containerEndpoint}.");
               if (!string.IsNullOrWhiteSpace(t.Container) && !result.Containers.Contains(t.Container)) {
                  result.Success = false;
                  messages.Add($"Container '{t.Container}' does not exist on that environment.");
               }
            }

            var registries = await portainer.RegistryUrlsAsync(ct);
            result.PortainerRegistryMissing = !registries.Any(r => PortainerClient.SameRegistry(r, t.RegistryHost));
            messages.Add(result.PortainerRegistryMissing
               ? $"Portainer has no registry for {t.RegistryHost}; pulls may be refused. Use Register in Portainer."
               : $"Portainer knows the registry {t.RegistryHost}.");
            return 0;
         });
      }

      private static void ReadServices(string content, CtnDeployTarget t, CtnDeployTestResult result, List<string> messages, string where) {
         try {
            result.Services = ComposeImageRewriter.Services(content);
         }
         catch (InvalidDataException x) {
            result.Success = false;
            messages.Add($"{where}: {x.Message}");
            return;
         }

         messages.Add($"{where}: services {string.Join(", ", result.Services)}.");
         if (string.IsNullOrWhiteSpace(t.Service)) return;

         if (!result.Services.Contains(t.Service)) {
            result.Success = false;
            messages.Add($"Service '{t.Service}' is not in {where}.");
            return;
         }

         result.ImageVariablePresent = ComposeImageRewriter.UsesVariable(content, t.Service, t.Variable);
         messages.Add(result.ImageVariablePresent
            ? $"Service '{t.Service}' uses ${{{t.Variable}}}."
            : $"Service '{t.Service}' does not use ${{{t.Variable}}} yet; the first deploy rewrites its image line.");
      }

      #endregion

      /// <summary>Adds the target's registry to Portainer as a custom registry with the target's login.</summary>
      /// <exception cref="ActionException">400 without a registry login.</exception>
      public async Task RegisterPortainerRegistryAsync(CtnDeployTarget t, CancellationToken ct) {
         if (!t.HasRegistryLogin) throw new ActionException("Set the registry username and password of the target first.", 400);
         await PortainerAsync(t, async (portainer, _) => {
            await portainer.CreateRegistryAsync("em-" + t.RegistryHost, t.RegistryHost, t.RegistryUser!, t.RegistrySecret!, ct);
            return 0;
         });
      }

      #region Helpers

      private static string Reference(CtnDeployTarget t, string repository, string digest) => $"{t.RegistryHost}/{repository}@{digest}";

      private static string Join(string folder, string name) => folder.TrimEnd('/') + "/" + name;

      private static string Require(string? value, string what) =>
         string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"Set the {what} of the deploy target first.") : value.Trim();

      private static int Require(int? value, string what) =>
         value ?? throw new InvalidOperationException($"Set the {what} of the deploy target first.");

      private static string First(params string[] texts) =>
         texts.Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) is { } text ? text.Split('\n')[0].TrimEnd('\r') + "." : "";

      private async Task<ICtnDeployShell> ConnectAsync(CtnDeployTarget t, CtnDeployLog log, CancellationToken ct) {
         var shell = await transports.ConnectSshAsync(t, ct);
         log.Line($"Connected to {t.User}@{t.Host}:{t.Port ?? 22}.");
         return shell;
      }

      // Runs work against Portainer; a refused certificate becomes the fingerprint to confirm.
      private async Task<T> PortainerAsync<T>(CtnDeployTarget t, Func<PortainerClient, CtnTlsPin, Task<T>> work) {
         if (string.IsNullOrEmpty(t.Secret)) throw new InvalidOperationException("Set the Portainer access token of the deploy target first.");
         var pin = new CtnTlsPin(t.Fingerprint);
         using var portainer = new PortainerClient(transports.CreateHttpHandler(pin), t.Host, t.Secret);
         try {
            return await work(portainer, pin);
         }
         catch (HttpRequestException) when (pin.Offered is not null) {
            throw new CtnDeployFingerprintException(pin.Offered, pin.Pinned is not null);
         }
      }

      private Task PortainerAsync(CtnDeployTarget t, Func<PortainerClient, CtnTlsPin, Task> work) =>
         PortainerAsync(t, async (p, pin) => {
            await work(p, pin);
            return 0;
         });

      private static async Task<(string File, string Content)?> FindComposeAsync(ICtnDeployShell shell, string folder, CancellationToken ct) {
         foreach (var file in ComposeFiles) {
            if (await shell.ReadFileAsync(Join(folder, file), ct) is { } content) return (file, content);
         }

         return null;
      }

      private static async Task RequireAsync(ICtnDeployShell shell, string command, string what, CtnDeployLog log, CancellationToken ct) {
         log.Line("$ " + command);
         var result = await shell.RunAsync(command, null, ct);
         log.Output(result.Output);
         log.Output(result.Error);
         if (result.ExitCode != 0) throw new InvalidOperationException($"{what} failed with exit code {result.ExitCode}.");
      }

      private static async Task<bool> LoginAsync(ICtnDeployShell shell, CtnDeployTarget t, CtnDeployLog log, CancellationToken ct) {
         if (!t.HasRegistryLogin) {
            log.Line("No registry login set; pulling with the host's own Docker login.");
            return false;
         }

         var result = await shell.RunAsync(
            $"docker login {Quote(t.RegistryHost)} --username {Quote(t.RegistryUser!)} --password-stdin", t.RegistrySecret + "\n", ct);
         if (result.ExitCode != 0) {
            log.Output(result.Error);
            throw new InvalidOperationException($"docker login {t.RegistryHost} failed (exit {result.ExitCode}): {log.Mask(First(result.Error, result.Output))}");
         }

         log.Line($"Logged in to {t.RegistryHost} as {t.RegistryUser}.");
         return true;
      }

      private static async Task LogoutAsync(ICtnDeployShell shell, CtnDeployTarget t, CtnDeployLog log) {
         try {
            await shell.RunAsync($"docker logout {Quote(t.RegistryHost)}", null, CancellationToken.None);
            log.Line($"Logged out of {t.RegistryHost}.");
         }
         catch (Exception x) {
            log.Warn($"docker logout {t.RegistryHost} failed: {x.Message}");
         }
      }

      #endregion
   }
}
