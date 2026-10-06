using System.Security.Cryptography;
using System.Text.Json;
using Em.Api.Core.Registry.Deploy;
using Em.Shared;

namespace Em.Api.Core.Tests.Deploy
{
   public class CtnDeployTagFilterTests
   {
      [Theory]
      [InlineData(null, "anything", true)]
      [InlineData("", "1.0.0", true)]
      [InlineData("latest", "latest", true)]
      [InlineData("latest", "Latest", false)]
      [InlineData("1.*", "1.4.2", true)]
      [InlineData("1.*", "2.0.0", false)]
      [InlineData("1.?.0", "1.5.0", true)]
      [InlineData("1.?.0", "1.15.0", false)]
      [InlineData("*-rc.*, latest", "latest", true)]
      [InlineData("*-rc.*, latest", "2.0.0-rc.3", true)]
      [InlineData("*-rc.*, latest", "2.0.0-beta.1", false)]
      public void IsMatch(string? filter, string tag, bool expected) =>
         Assert.Equal(expected, CtnDeployTagFilter.IsMatch(filter, tag));

      [Fact]
      public void FirstMatch_PicksFloatingTagWhenOnlyItMatches() =>
         Assert.Equal("latest", CtnDeployTagFilter.FirstMatch("latest", ["1.2.0", "1.2", "latest"]));

      [Fact]
      public void Normalize_TrimsAndJoins() {
         Assert.Equal("1.*,latest", CtnDeployTagFilter.Normalize(" 1.* , latest ,"));
         Assert.Null(CtnDeployTagFilter.Normalize("  "));
      }

      [Fact]
      public void Normalize_RejectsCharactersATagCannotHave() =>
         Assert.Equal(400, Assert.Throws<ActionException>(() => CtnDeployTagFilter.Normalize("v1/2")).StatusCode);
   }

   public class ComposeImageRewriterTests
   {
      private const string Compose = """
         # production stack
         services:
           api:
             image: registry.example.com/acme/api:1.0.0   # pinned
             ports:
               - "8080:8080"
           worker:
             image: "registry.example.com/acme/worker:2.0"
         volumes:
           data: {}

         """;

      [Fact]
      public void Rewrite_ReplacesOnlyTheServiceImage_KeepingComments() {
         var (content, changed, old) = ComposeImageRewriter.Rewrite(Compose, "api", "EM_IMAGE_API");

         Assert.True(changed);
         Assert.Equal("registry.example.com/acme/api:1.0.0", old);
         Assert.Contains("    image: ${EM_IMAGE_API}   # pinned", content);
         Assert.Contains("# production stack", content);
         Assert.Contains("image: \"registry.example.com/acme/worker:2.0\"", content);
         Assert.Equal(Compose.Replace("registry.example.com/acme/api:1.0.0", "${EM_IMAGE_API}"), content);
      }

      [Fact]
      public void Rewrite_QuotedValue_ProducesValidYaml() {
         var (content, changed, _) = ComposeImageRewriter.Rewrite(Compose, "worker", "EM_IMAGE_WORKER");

         Assert.True(changed);
         Assert.True(ComposeImageRewriter.UsesVariable(content, "worker", "EM_IMAGE_WORKER"));
         Assert.Equal(["api", "worker"], ComposeImageRewriter.Services(content));
      }

      [Fact]
      public void Rewrite_ValueAlreadyAVariable_IsUnchanged() {
         var (content, _, _) = ComposeImageRewriter.Rewrite(Compose, "api", "EM_IMAGE_API");
         var (again, changed, old) = ComposeImageRewriter.Rewrite(content, "api", "EM_IMAGE_API");

         Assert.False(changed);
         Assert.Equal(content, again);
         Assert.Equal("${EM_IMAGE_API}", old);
      }

      [Fact]
      public void Rewrite_UnknownService_Throws() =>
         Assert.Throws<InvalidDataException>(() => ComposeImageRewriter.Rewrite(Compose, "web", "EM_IMAGE_WEB"));

      [Fact]
      public void Rewrite_ServiceWithoutImage_Throws() =>
         Assert.Throws<InvalidDataException>(() =>
            ComposeImageRewriter.Rewrite("services:\n  web:\n    build: .\n", "web", "EM_IMAGE_WEB"));

      [Theory]
      [InlineData("api", "EM_IMAGE_API")]
      [InlineData("my-api.v2", "EM_IMAGE_MY_API_V2")]
      public void DefaultVariable(string service, string expected) =>
         Assert.Equal(expected, ComposeImageRewriter.DefaultVariable(service));

      [Theory]
      [InlineData("${EM_IMAGE_API}", true)]
      [InlineData("${EM_IMAGE_API:-nginx}", true)]
      [InlineData("$EM_IMAGE_API", true)]
      [InlineData("${EM_IMAGE_API_OLD}", false)]
      [InlineData("nginx:latest", false)]
      public void ReferencesVariable(string image, bool expected) =>
         Assert.Equal(expected, ComposeImageRewriter.ReferencesVariable(image, "EM_IMAGE_API"));

      [Fact]
      public void SetEnv_ReplacesLine_KeepsOthersAndLineEndings() {
         var env = "# settings\r\nTZ=UTC\r\nEM_IMAGE_API=old@sha256:1\r\nDEBUG=0\r\n";

         var result = ComposeImageRewriter.SetEnv(env, "EM_IMAGE_API", "new@sha256:2");

         Assert.Equal("# settings\r\nTZ=UTC\r\nEM_IMAGE_API=new@sha256:2\r\nDEBUG=0\r\n", result);
      }

      [Fact]
      public void SetEnv_AppendsMissingVariable() {
         Assert.Equal("TZ=UTC\nEM_IMAGE_API=x\n", ComposeImageRewriter.SetEnv("TZ=UTC\n", "EM_IMAGE_API", "x"));
         Assert.Equal("EM_IMAGE_API=x\n", ComposeImageRewriter.SetEnv(null, "EM_IMAGE_API", "x"));
      }

      [Fact]
      public void ReadEnv_HandlesQuotesExportAndSimilarNames() {
         var env = "EM_IMAGE_API_OLD=a\nexport EM_IMAGE_API=\"host/acme/api@sha256:abc\"\n";

         Assert.Equal("host/acme/api@sha256:abc", ComposeImageRewriter.ReadEnv(env, "EM_IMAGE_API"));
         Assert.Equal("sha256:abc", ComposeImageRewriter.DigestOf(ComposeImageRewriter.ReadEnv(env, "EM_IMAGE_API")));
         Assert.Null(ComposeImageRewriter.ReadEnv(env, "EM_IMAGE_WEB"));
      }
   }

   public class DockerRecreatePlanTests
   {
      private const string OldId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

      private static readonly string Inspect = $$"""
         {
           "Id": "{{OldId}}",
           "Image": "sha256:old",
           "Config": {
             "Hostname": "0123456789ab",
             "Image": "registry.example.com/acme/api:1.0.0",
             "Env": ["PATH=/usr/bin", "APP_MODE=prod"],
             "Cmd": ["dotnet", "api.dll"],
             "Labels": { "org.opencontainers.image.version": "1.0.0", "team": "core" },
             "ExposedPorts": { "8080/tcp": {} }
           },
           "HostConfig": {
             "Binds": ["/srv/data:/data"],
             "PortBindings": { "8080/tcp": [{ "HostPort": "8080" }] },
             "RestartPolicy": { "Name": "unless-stopped" },
             "NetworkMode": "backend"
           },
           "Mounts": [
             { "Type": "bind", "Source": "/srv/data", "Destination": "/data", "RW": true },
             { "Type": "volume", "Name": "abc123anon", "Destination": "/cache", "RW": true }
           ],
           "NetworkSettings": {
             "Networks": {
               "frontend": { "Aliases": ["web"], "NetworkID": "n1", "EndpointID": "e1", "IPAddress": "172.18.0.2" },
               "backend": { "Aliases": ["api", "0123456789ab"], "NetworkID": "n2", "EndpointID": "e2", "IPAddress": "172.19.0.2" }
             }
           },
           "State": { "Running": true }
         }
         """;

      private static readonly string ImageConfig = """
         {
           "Env": ["PATH=/usr/bin"],
           "Cmd": ["dotnet", "api.dll"],
           "Labels": { "org.opencontainers.image.version": "1.0.0" },
           "ExposedPorts": { "8080/tcp": {} }
         }
         """;

      [Fact]
      public void Build_KeepsHostConfigAndUserSettings_ReplacesImage() {
         using var inspect = JsonDocument.Parse(Inspect);
         using var image = JsonDocument.Parse(ImageConfig);

         var plan = DockerRecreatePlan.Build(inspect.RootElement, "registry.example.com/acme/api@sha256:new", image.RootElement);
         var body = plan.Body;

         Assert.Equal("registry.example.com/acme/api@sha256:new", (string?)body["Image"]);
         Assert.Null(body["Hostname"]);
         Assert.Equal("unless-stopped", (string?)body["HostConfig"]!["RestartPolicy"]!["Name"]);
         Assert.Equal("8080", (string?)body["HostConfig"]!["PortBindings"]!["8080/tcp"]![0]!["HostPort"]);

         // Image defaults are dropped so the new image's apply; the user's own values stay.
         Assert.Equal(["APP_MODE=prod"], body["Env"]!.AsArray().Select(e => (string)e!).ToArray());
         Assert.Null(body["Cmd"]);
         Assert.Null(body["Labels"]!["org.opencontainers.image.version"]);
         Assert.Equal("core", (string?)body["Labels"]!["team"]);

         // The anonymous volume is carried over by name.
         var binds = body["HostConfig"]!["Binds"]!.AsArray().Select(b => (string)b!).ToArray();
         Assert.Equal(["/srv/data:/data", "abc123anon:/cache"], binds);
      }

      [Fact]
      public void Build_NetworkModeAtCreate_OthersConnectedAfter_WithoutRuntimeFields() {
         using var inspect = JsonDocument.Parse(Inspect);

         var plan = DockerRecreatePlan.Build(inspect.RootElement, "img@sha256:new", null);

         var endpoints = plan.Body["NetworkingConfig"]!["EndpointsConfig"]!.AsObject();
         Assert.Equal(["backend"], endpoints.Select(e => e.Key).ToArray());
         var backend = endpoints["backend"]!;
         Assert.Null(backend["NetworkID"]);
         Assert.Null(backend["IPAddress"]);
         Assert.Equal(["api"], backend["Aliases"]!.AsArray().Select(a => (string)a!).ToArray());
         var extra = Assert.Single(plan.ExtraNetworks);
         Assert.Equal("frontend", extra.Network);
         Assert.Null(extra.Endpoint["EndpointID"]);
      }

      [Fact]
      public void Build_HostNetwork_HasNoEndpoints() {
         using var inspect = JsonDocument.Parse(Inspect.Replace("\"NetworkMode\": \"backend\"", "\"NetworkMode\": \"host\""));

         var plan = DockerRecreatePlan.Build(inspect.RootElement, "img@sha256:new", null);

         Assert.Null(plan.Body["NetworkingConfig"]);
         Assert.Empty(plan.ExtraNetworks);
      }
   }

   public class CtnDeploySecretsTests
   {
      private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

      [Fact]
      public void RoundTrip() =>
         Assert.Equal("p@ss wörd\n-----BEGIN KEY-----", CtnDeploySecrets.Decrypt(Key, CtnDeploySecrets.Encrypt(Key, "p@ss wörd\n-----BEGIN KEY-----")));

      [Fact]
      public void SameValue_EncryptsDifferentlyEachTime() =>
         Assert.NotEqual(CtnDeploySecrets.Encrypt(Key, "token"), CtnDeploySecrets.Encrypt(Key, "token"));

      [Fact]
      public void Tampered_Fails() {
         var data = CtnDeploySecrets.Encrypt(Key, "token");
         data[^1] ^= 1;

         Assert.ThrowsAny<CryptographicException>(() => CtnDeploySecrets.Decrypt(Key, data));
      }

      [Fact]
      public void OtherKey_Fails() =>
         Assert.ThrowsAny<CryptographicException>(() => CtnDeploySecrets.Decrypt(RandomNumberGenerator.GetBytes(32), CtnDeploySecrets.Encrypt(Key, "token")));

      [Fact]
      public async Task Instance_NullAndEmpty_AreNotStored() {
         var secrets = new CtnDeploySecrets(Key);

         Assert.Null(await secrets.ProtectAsync(null, TestContext.Current.CancellationToken));
         Assert.Null(await secrets.ProtectAsync("", TestContext.Current.CancellationToken));
         Assert.Null(await secrets.UnprotectAsync(null, TestContext.Current.CancellationToken));
      }

      [Fact]
      public void Log_MasksSecretsAndTheirBase64() {
         var log = new CtnDeployLog(["s3cret-token"]);
         log.Line("token s3cret-token and " + Convert.ToBase64String("s3cret-token"u8.ToArray()));

         Assert.DoesNotContain("s3cret", log.ToString());
         Assert.DoesNotContain(Convert.ToBase64String("s3cret-token"u8.ToArray()), log.ToString());
      }
   }
}
