using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Em.Api.Core.Models;
using Em.Api.Core.Registry.Deploy;
using Em.Shared;

namespace Em.Api.Core.Tests.Deploy
{
   /// <summary>
   /// The deploy flows against a fake SSH host, a fake Docker Engine and a fake Portainer: what is read, written, run and
   /// called, in which order, and how a failed recreate is undone. No network is used.
   /// </summary>
   public class CtnDeployExecutorTests
   {
      private const string Pin = "SHA256:good";
      private const string Repository = "acme/api";
      private const string Registry = "registry.example.com";
      private static readonly string DigestA = "sha256:" + new string('a', 64);
      private static readonly string DigestB = "sha256:" + new string('b', 64);

      private const string Compose = "services:\n  api:\n    image: registry.example.com/acme/api:1.0.0\n    restart: always\n";

      private static CancellationToken Ct => TestContext.Current.CancellationToken;

      private static CtnDeployTarget Ssh(CtnDeployMode mode, string? registryUser = null, string? fingerprint = Pin) => new() {
         Kind = CtnDeployKind.Ssh, Mode = mode, Host = "docker.example.com", Port = 22, User = "deploy", Auth = CtnDeployAuth.SshPassword,
         Secret = "ssh-password-1", Fingerprint = fingerprint, Stack = "/opt/app", Service = "api", Container = "api",
         RegistryHost = Registry, RegistryUser = registryUser, RegistrySecret = registryUser is null ? null : "registry-token-1"
      };

      private static CtnDeployTarget Portainer(CtnDeployMode mode, int? stackId = 7) => new() {
         Kind = CtnDeployKind.Portainer, Mode = mode, Host = "https://portainer.example.com:9443", Auth = CtnDeployAuth.PortainerToken,
         Secret = "ptr_token_1", EndpointId = 2, Stack = "app", StackId = stackId, Service = "api", Container = "api", RegistryHost = Registry
      };

      #region SSH stack

      [Fact]
      public async Task SshStack_RewritesImage_SetsEnv_PullsAndStarts() {
         var host = new FakeShell();
         host.Files["/opt/app/compose.yml"] = Compose;
         host.Files["/opt/app/.env"] = "TZ=UTC\n";
         var log = new CtnDeployLog();

         var outcome = await new CtnDeployExecutor(new FakeTransports(host)).DeployAsync(Ssh(CtnDeployMode.Stack), Repository, DigestA, log, Ct);

         Assert.Equal(Compose, outcome.OldFile);
         Assert.Null(outcome.PrevDigest);
         Assert.Contains("image: ${EM_IMAGE_API}", host.Files["/opt/app/compose.yml"]);
         Assert.Equal($"TZ=UTC\nEM_IMAGE_API={Registry}/{Repository}@{DigestA}\n", host.Files["/opt/app/.env"]);
         Assert.Equal([
            "cd '/opt/app' && docker compose -f 'compose.yml' pull 'api'",
            "cd '/opt/app' && docker compose -f 'compose.yml' up -d 'api'"
         ], host.Commands);
      }

      [Fact]
      public async Task SshStack_SecondDeploy_ReportsPreviousDigest_LeavesFileAlone() {
         var host = new FakeShell();
         host.Files["/opt/app/compose.yml"] = Compose;
         var executor = new CtnDeployExecutor(new FakeTransports(host));
         await executor.DeployAsync(Ssh(CtnDeployMode.Stack), Repository, DigestA, new CtnDeployLog(), Ct);
         var rewritten = host.Files["/opt/app/compose.yml"];

         // A rollback is the same call with an older digest.
         var outcome = await executor.DeployAsync(Ssh(CtnDeployMode.Stack), Repository, DigestB, new CtnDeployLog(), Ct);

         Assert.Equal(DigestA, outcome.PrevDigest);
         Assert.Null(outcome.OldFile);
         Assert.Equal(rewritten, host.Files["/opt/app/compose.yml"]);
         Assert.Contains($"@{DigestB}", host.Files["/opt/app/.env"]);
      }

      [Fact]
      public async Task SshStack_RegistryLogin_LogsInAndOut_WithoutLeakingSecrets() {
         var host = new FakeShell();
         host.Files["/opt/app/docker-compose.yml"] = Compose;
         var target = Ssh(CtnDeployMode.Stack, registryUser: "robot-pull");
         var log = new CtnDeployLog(target.Secrets);

         await new CtnDeployExecutor(new FakeTransports(host)).DeployAsync(target, Repository, DigestA, log, Ct);

         Assert.Equal($"docker login '{Registry}' --username 'robot-pull' --password-stdin", host.Commands[0]);
         Assert.Equal("registry-token-1\n", host.Stdin[0]);
         Assert.Equal($"docker logout '{Registry}'", host.Commands[^1]);
         Assert.Contains("-f 'docker-compose.yml'", host.Commands[1]);
         Assert.DoesNotContain("registry-token-1", log.ToString());
         Assert.DoesNotContain("ssh-password-1", log.ToString());
      }

      [Fact]
      public async Task SshStack_PullFails_Throws_AndStillLogsOut() {
         var host = new FakeShell { Fail = c => c.Contains(" pull ") };
         host.Files["/opt/app/compose.yml"] = Compose;
         var log = new CtnDeployLog();

         var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CtnDeployExecutor(new FakeTransports(host)).DeployAsync(Ssh(CtnDeployMode.Stack, "robot-pull"), Repository, DigestA, log, Ct));

         Assert.Contains("docker compose pull", error.Message);
         Assert.StartsWith("docker logout", host.Commands[^1]);
      }

      [Fact]
      public async Task Ssh_UnpinnedHostKey_IsRefused() {
         var error = await Assert.ThrowsAsync<CtnDeployFingerprintException>(() =>
            new CtnDeployExecutor(new FakeTransports(new FakeShell())).DeployAsync(Ssh(CtnDeployMode.Stack, fingerprint: null), Repository,
               DigestA, new CtnDeployLog(), Ct));

         Assert.Equal(Pin, error.Offered);
      }

      [Fact]
      public async Task Test_UnpinnedHostKey_OffersFingerprint_RunsNothing() {
         var host = new FakeShell();

         var result = await new CtnDeployExecutor(new FakeTransports(host)).TestAsync(Ssh(CtnDeployMode.Stack, fingerprint: null), Ct);

         Assert.False(result.Success);
         Assert.Equal(Pin, result.OfferedFingerprint);
         Assert.Empty(host.Commands);
      }

      #endregion

      #region Recreate

      [Fact]
      public async Task SshContainer_Recreates_InOrder_AndReportsPreviousDigest() {
         var docker = new FakeDocker();
         var host = new FakeShell { Docker = docker };
         var log = new CtnDeployLog();

         var outcome = await new CtnDeployExecutor(new FakeTransports(host)).DeployAsync(Ssh(CtnDeployMode.Container), Repository, DigestB, log, Ct);

         Assert.Equal(DigestA, outcome.PrevDigest);
         Assert.Equal([
            "pull acme/api@" + DigestB, "create api-em-new", "connect frontend", "stop old", "rename old api-em-old", "rename new api",
            "start new", "remove old"
         ], docker.Calls.Select(c => c.StartsWith("rename old", StringComparison.Ordinal) ? "rename old api-em-old" : c).ToArray());
         Assert.Equal($"{Registry}/{Repository}@{DigestB}", (string?)docker.Created!["Image"]);
         Assert.Equal(["new"], docker.Containers.Values.Select(c => c.Id).ToArray());
         Assert.Equal("api", docker.Containers.Keys.Single());
      }

      [Fact]
      public async Task Recreate_StartFails_RestoresOldContainer() {
         var docker = new FakeDocker { FailStartOfNew = true };
         var host = new FakeShell { Docker = docker };
         var log = new CtnDeployLog();

         await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CtnDeployExecutor(new FakeTransports(host)).DeployAsync(Ssh(CtnDeployMode.Container), Repository, DigestB, log, Ct));

         Assert.Contains("remove new", docker.Calls);
         Assert.Contains("rename old api", docker.Calls);
         Assert.Equal("start old", docker.Calls[^1]);
         Assert.Equal("old", docker.Containers["api"].Id);
         Assert.True(docker.Containers["api"].Running);
         Assert.Contains("Restoring the old container", log.ToString());
      }

      [Fact]
      public async Task PortainerContainer_UsesDockerProxy_WithRegistryAuthHeader() {
         var docker = new FakeDocker();
         var portainer = new FakePortainer(docker);
         var target = Portainer(CtnDeployMode.Container);
         var withLogin = new CtnDeployTarget {
            Kind = target.Kind, Mode = target.Mode, Host = target.Host, Auth = target.Auth, Secret = target.Secret, EndpointId = 2,
            Container = "api", RegistryHost = Registry, RegistryUser = "robot-pull", RegistrySecret = "registry-token-1"
         };

         await new CtnDeployExecutor(new FakeTransports(portainer: portainer)).DeployAsync(withLogin, Repository, DigestB, new CtnDeployLog(), Ct);

         Assert.Contains("start new", docker.Calls);
         Assert.All(portainer.Paths, p => Assert.StartsWith("/api/endpoints/2/docker/", p));
         Assert.Equal("ptr_token_1", portainer.ApiKey);
         var auth = Encoding.UTF8.GetString(Convert.FromBase64String(docker.RegistryAuth!.Replace('-', '+').Replace('_', '/')));
         Assert.Contains("\"username\":\"robot-pull\"", auth);
      }

      #endregion

      #region Portainer stack

      [Fact]
      public async Task PortainerStack_RewritesFile_MergesEnv_Redeploys() {
         var portainer = new FakePortainer(new FakeDocker());
         var log = new CtnDeployLog();

         var outcome = await new CtnDeployExecutor(new FakeTransports(portainer: portainer)).DeployAsync(Portainer(CtnDeployMode.Stack), Repository,
            DigestB, log, Ct);

         Assert.Equal(Compose, outcome.OldFile);
         Assert.Equal(DigestA, outcome.PrevDigest);
         var update = portainer.LastBody!;
         Assert.Equal("PUT /api/stacks/7?endpointId=2", portainer.LastRequest);
         Assert.Contains("${EM_IMAGE_API}", (string?)update["stackFileContent"]);
         Assert.True((bool?)update["pullImage"]);
         var env = update["env"]!.AsArray().ToDictionary(e => (string)e!["name"]!, e => (string?)e!["value"]);
         Assert.Equal("UTC", env["TZ"]);
         Assert.Equal($"{Registry}/{Repository}@{DigestB}", env["EM_IMAGE_API"]);
      }

      [Fact]
      public async Task PortainerGitStack_RedeploysWithoutRewrite_AndWarns() {
         var portainer = new FakePortainer(new FakeDocker()) { Git = true };
         var log = new CtnDeployLog();

         var outcome = await new CtnDeployExecutor(new FakeTransports(portainer: portainer)).DeployAsync(Portainer(CtnDeployMode.Stack), Repository,
            DigestB, log, Ct);

         Assert.Null(outcome.OldFile);
         Assert.Equal("PUT /api/stacks/7/git/redeploy?endpointId=2", portainer.LastRequest);
         Assert.Equal("git-user", (string?)portainer.LastBody!["repositoryUsername"]);
         Assert.Contains("rollback is not available", log.ToString());
      }

      [Fact]
      public async Task Portainer_UntrustedCertificate_BecomesFingerprintToConfirm() {
         var portainer = new FakePortainer(new FakeDocker()) { RejectCertificate = "SHA256:AA:BB" };

         var result = await new CtnDeployExecutor(new FakeTransports(portainer: portainer)).TestAsync(Portainer(CtnDeployMode.Stack), Ct);

         Assert.False(result.Success);
         Assert.Equal("SHA256:AA:BB", result.OfferedFingerprint);
      }

      [Fact]
      public async Task PortainerTest_ListsEnvironmentsStacksServices_AndMissingRegistry() {
         var portainer = new FakePortainer(new FakeDocker());

         var result = await new CtnDeployExecutor(new FakeTransports(portainer: portainer)).TestAsync(Portainer(CtnDeployMode.Stack), Ct);

         Assert.True(result.Success, string.Join("\n", result.Messages));
         Assert.Equal(2, Assert.Single(result.Endpoints).Id);
         Assert.Equal(7, Assert.Single(result.Stacks).Id);
         Assert.Equal(["api"], result.Services);
         Assert.False(result.ImageVariablePresent);
         Assert.True(result.PortainerRegistryMissing);
      }

      #endregion

      #region Create stack

      [Fact]
      public async Task CreateStack_Ssh_WritesComposeAndEnv_ThenRefusesToOverwrite() {
         var host = new FakeShell();
         var executor = new CtnDeployExecutor(new FakeTransports(host));
         const string content = "services:\n  api:\n    image: ${EM_IMAGE_API}\n";

         var created = await executor.CreateStackAsync(Ssh(CtnDeployMode.Container), Repository, DigestA, content, new CtnDeployLog(), Ct);

         Assert.Equal(new CtnDeployCreated("/opt/app", null, null, "api", "EM_IMAGE_API"), created);
         Assert.Equal(content, host.Files["/opt/app/compose.yml"]);
         Assert.Equal($"EM_IMAGE_API={Registry}/{Repository}@{DigestA}\n", host.Files["/opt/app/.env"]);
         Assert.Contains("mkdir -p '/opt/app'", host.Commands);
         var again = await Assert.ThrowsAsync<ActionException>(() =>
            executor.CreateStackAsync(Ssh(CtnDeployMode.Stack), Repository, DigestA, content, new CtnDeployLog(), Ct));
         Assert.Equal(409, again.StatusCode);
      }

      [Fact]
      public async Task CreateStack_Portainer_WithoutManifest_UsesLatestTag() {
         var portainer = new FakePortainer(new FakeDocker());

         var created = await new CtnDeployExecutor(new FakeTransports(portainer: portainer)).CreateStackAsync(Portainer(CtnDeployMode.Stack, null),
            Repository, null, "services:\n  api:\n    image: ${EM_IMAGE_API}\n", new CtnDeployLog(), Ct);

         Assert.Equal(new CtnDeployCreated("app", 42, 2, "api", "EM_IMAGE_API"), created);
         Assert.Equal("POST /api/stacks/create/standalone/string?endpointId=2", portainer.LastRequest);
         Assert.Equal($"{Registry}/{Repository}:latest", (string?)portainer.LastBody!["env"]![0]!["value"]);
      }

      #endregion

      #region Fakes

      private sealed class FakeTransports(FakeShell? shell = null, FakePortainer? portainer = null) : ICtnDeployTransports
      {
         public Task<ICtnDeployShell> ConnectSshAsync(CtnDeployTarget target, CancellationToken ct) =>
            target.Fingerprint == Pin
               ? Task.FromResult<ICtnDeployShell>(shell!)
               : throw new CtnDeployFingerprintException(Pin, target.Fingerprint is not null);

         public HttpMessageHandler CreateHttpHandler(CtnTlsPin pin) {
            portainer!.Pin = pin;
            return portainer;
         }
      }

      private sealed class FakeShell : ICtnDeployShell
      {
         public Dictionary<string, string> Files { get; } = [];
         public List<string> Commands { get; } = [];
         public List<string?> Stdin { get; } = [];
         public Func<string, bool> Fail { get; init; } = _ => false;
         public FakeDocker? Docker { get; init; }

         public Task<CtnShellResult> RunAsync(string command, string? stdin, CancellationToken ct) {
            Commands.Add(command);
            Stdin.Add(stdin);
            return Task.FromResult(Fail(command) ? new CtnShellResult(1, "", "error: failed") : new CtnShellResult(0, "ok", ""));
         }

         public Task<string?> ReadFileAsync(string path, CancellationToken ct) => Task.FromResult(Files.GetValueOrDefault(path));

         public Task WriteFileAsync(string path, string content, CancellationToken ct) {
            Files[path] = content;
            return Task.CompletedTask;
         }

         public HttpMessageHandler CreateDockerHandler() => Docker ?? throw new InvalidOperationException("dial-stdio is not available");

         public void Dispose() {
         }
      }

      /// <summary>
      /// A Docker Engine with one running container "api" (id "old") on networks backend and frontend, built from an
      /// image whose repo digest is <see cref="DigestA"/>.
      /// </summary>
      private sealed class FakeDocker : HttpMessageHandler
      {
         public sealed class Box(string id) {
            public string Id { get; } = id;
            public bool Running { get; set; }
         }

         public Dictionary<string, Box> Containers { get; } = new() { ["api"] = new Box("old") { Running = true } };
         public List<string> Calls { get; } = [];
         public bool FailStartOfNew { get; init; }
         public JsonObject? Created { get; private set; }
         public string? RegistryAuth { get; private set; }

         private string? NameOf(string id) => Containers.FirstOrDefault(c => c.Value.Id == id || c.Key == id).Key;

         protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            var path = request.RequestUri!.AbsolutePath;
            var marker = path.IndexOf("/docker/", StringComparison.Ordinal);
            path = marker >= 0 ? path[(marker + 8)..] : path.TrimStart('/');
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            var method = request.Method.Method;
            var parts = path.Split('/');

            if (method == "GET" && path == "version") return Json("""{"Version":"27.3.1"}""");
            if (method == "GET" && path == "containers/json") {
               return Json(JsonSerializer.Serialize(Containers.Select(c => new { Names = new[] { "/" + c.Key } })));
            }

            if (method == "GET" && parts[0] == "containers" && parts.Length == 3) {
               var name = NameOf(Uri.UnescapeDataString(parts[1]));
               return name is null ? Status(HttpStatusCode.NotFound) : Json(InspectOf(Containers[name].Id));
            }

            if (method == "GET" && parts[0] == "images") {
               return Json($$"""{"Config":{"Env":["PATH=/usr/bin"]},"RepoDigests":["{{Registry}}/{{Repository}}@{{DigestA}}"]}""");
            }

            if (method == "POST" && path == "images/create") {
               RegistryAuth = request.Headers.TryGetValues("X-Registry-Auth", out var auth) ? auth.Single() : null;
               Calls.Add($"pull {query["fromImage"]![(Registry.Length + 1)..]}@{query["tag"]}");
               return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"Pulling\"}\n{\"status\":\"Done\"}\n") };
            }

            if (method == "POST" && path == "containers/create") {
               Created = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
               Containers[query["name"]!] = new Box("new");
               Calls.Add("create " + query["name"]);
               return Json("""{"Id":"new"}""");
            }

            if (method == "POST" && parts[0] == "networks") {
               Calls.Add("connect " + parts[1]);
               return Status(HttpStatusCode.OK);
            }

            var id = parts.Length > 1 ? parts[1] : "";
            var current = NameOf(id);
            switch (method, parts.Length > 2 ? parts[2] : "") {
               case ("POST", "stop"):
                  Calls.Add("stop " + id);
                  Containers[current!].Running = false;
                  return Status(HttpStatusCode.NoContent);
               case ("POST", "start"):
                  Calls.Add("start " + id);
                  if (FailStartOfNew && id == "new") {
                     return new HttpResponseMessage(HttpStatusCode.InternalServerError) {
                        Content = new StringContent("""{"message":"port is already allocated"}""")
                     };
                  }

                  Containers[current!].Running = true;
                  return Status(HttpStatusCode.NoContent);
               case ("POST", "rename"):
                  var to = query["name"]!;
                  Calls.Add($"rename {id} {to}");
                  var box = Containers[current!];
                  Containers.Remove(current!);
                  Containers[to] = box;
                  return Status(HttpStatusCode.NoContent);
               case ("DELETE", ""):
                  Calls.Add("remove " + id);
                  if (current is not null) Containers.Remove(current);
                  return Status(HttpStatusCode.NoContent);
            }

            return Status(HttpStatusCode.NotImplemented);
         }

         private static string InspectOf(string id) => $$"""
            {
              "Id": "{{id}}", "Image": "sha256:img",
              "Config": { "Image": "{{Registry}}/{{Repository}}:1.0.0", "Env": ["PATH=/usr/bin", "MODE=prod"] },
              "HostConfig": { "NetworkMode": "backend", "RestartPolicy": { "Name": "always" } },
              "NetworkSettings": { "Networks": { "backend": { "Aliases": ["api"] }, "frontend": {} } }
            }
            """;
      }

      /// <summary>A Portainer with environment 2 and stack 7 ("app", Env TZ and the image variable at <see cref="DigestA"/>).</summary>
      private sealed class FakePortainer(FakeDocker docker) : HttpMessageHandler
      {
         public CtnTlsPin? Pin { get; set; }
         public bool Git { get; init; }
         public string? RejectCertificate { get; init; }
         public List<string> Paths { get; } = [];
         public string? LastRequest { get; private set; }
         public JsonObject? LastBody { get; private set; }
         public string? ApiKey { get; private set; }

         protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            if (RejectCertificate is not null) {
               Pin!.Offered = RejectCertificate;
               throw new HttpRequestException("The SSL connection could not be established.");
            }

            ApiKey = request.Headers.GetValues("X-API-Key").Single();
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/endpoints/2/docker/", StringComparison.Ordinal)) {
               Paths.Add(path);
               return await new HttpMessageInvoker(docker, false).SendAsync(request, ct);
            }

            var method = request.Method.Method;
            if (method == "GET") {
               return path switch {
                  "/api/endpoints" => Json("""[{"Id":2,"Name":"production"}]"""),
                  "/api/stacks" => Json($$"""[{"Id":7,"Name":"app","EndpointId":2,"GitConfig":{{(Git ? "{}" : "null")}}}]"""),
                  "/api/stacks/7" => Json($$"""
                     {"Id":7,"Name":"app","EndpointId":2,
                      "GitConfig":{{(Git ? """{"ReferenceName":"refs/heads/main","Authentication":{"Username":"git-user"}}""" : "null")}},
                      "Env":[{"name":"TZ","value":"UTC"},{"name":"EM_IMAGE_API","value":"{{Registry}}/{{Repository}}@{{DigestA}}"}]}
                     """),
                  "/api/stacks/7/file" => Json(JsonSerializer.Serialize(new { StackFileContent = Compose })),
                  "/api/registries" => Json("""[{"Id":1,"URL":"docker.io"}]"""),
                  _ => Status(HttpStatusCode.NotFound)
               };
            }

            LastRequest = $"{method} {path}{request.RequestUri.Query}";
            LastBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            return path == "/api/stacks/create/standalone/string" ? Json("""{"Id":42}""") : Json("{}");
         }
      }

      private static HttpResponseMessage Json(string json) =>
         new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

      private static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new StringContent("") };

      #endregion
   }
}
