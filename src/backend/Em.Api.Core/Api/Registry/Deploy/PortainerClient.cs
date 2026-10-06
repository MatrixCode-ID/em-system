using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Em.Api.Core.Models;

namespace Em.Api.Core.Registry.Deploy
{
   /// <summary>
   /// The Portainer API calls a deploy needs (Portainer CE and BE), authenticated with an access token in
   /// <c>X-API-Key</c>. Standalone containers are recreated through the environment's Docker proxy.
   /// </summary>
   internal sealed class PortainerClient : IDisposable
   {
      private readonly HttpMessageHandler _handler;
      private readonly HttpClient _http;
      private readonly string _baseUrl;
      private readonly string _token;

      /// <param name="handler">Transport; owned and disposed by this client.</param>
      /// <param name="baseUrl">Portainer address, e.g. <c>https://portainer.example.com:9443</c>.</param>
      /// <param name="token">Access token.</param>
      public PortainerClient(HttpMessageHandler handler, string baseUrl, string token) {
         _handler = handler;
         _baseUrl = baseUrl.TrimEnd('/') + "/";
         _token = token;
         _http = Create("api/");
      }

      /// <summary>A client on the Docker proxy of environment <paramref name="endpointId"/>, sharing this connection.</summary>
      public DockerEngineClient Docker(int endpointId) => new(Create($"api/endpoints/{endpointId}/docker/"));

      private HttpClient Create(string path) {
         var http = new HttpClient(_handler, false) { BaseAddress = new Uri(_baseUrl + path), Timeout = TimeSpan.FromMinutes(14) };
         http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", _token);
         return http;
      }

      public async Task<CtnDeployEndpoint[]> EndpointsAsync(CancellationToken ct) {
         using var json = await GetAsync("endpoints", ct);
         return [.. json.RootElement.EnumerateArray().Select(e => new CtnDeployEndpoint {
            Id = e.GetProperty("Id").GetInt32(), Name = e.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : ""
         })];
      }

      public async Task<CtnDeployStack[]> StacksAsync(CancellationToken ct) {
         using var json = await GetAsync("stacks", ct);
         return [.. json.RootElement.EnumerateArray().Select(ToStack)];
      }

      /// <summary>The raw stack object (<c>Id</c>, <c>EndpointId</c>, <c>Env</c>, <c>GitConfig</c>...).</summary>
      public async Task<JsonObject> StackAsync(int id, CancellationToken ct) {
         using var json = await GetAsync($"stacks/{id}", ct);
         return JsonNode.Parse(json.RootElement.GetRawText())!.AsObject();
      }

      public async Task<string> StackFileAsync(int id, CancellationToken ct) {
         using var json = await GetAsync($"stacks/{id}/file", ct);
         return json.RootElement.GetProperty("StackFileContent").GetString() ?? "";
      }

      /// <summary>Redeploys a stack with a new file and environment, pulling its images.</summary>
      public Task UpdateStackAsync(int id, int endpointId, string content, JsonArray env, CancellationToken ct) =>
         SendAsync(HttpMethod.Put, $"stacks/{id}?endpointId={endpointId}", new JsonObject {
            ["stackFileContent"] = content, ["env"] = env.DeepClone(), ["prune"] = false, ["pullImage"] = true
         }, "update the stack", ct);

      /// <summary>
      /// Redeploys a Git stack with a new environment, pulling its images. The stored Git credentials are reused by
      /// sending the stored user name with an empty password.
      /// </summary>
      public Task RedeployGitStackAsync(int id, int endpointId, JsonObject stack, JsonArray env, CancellationToken ct) {
         var git = stack["GitConfig"] as JsonObject;
         var auth = git?["Authentication"] as JsonObject;
         return SendAsync(HttpMethod.Put, $"stacks/{id}/git/redeploy?endpointId={endpointId}", new JsonObject {
            ["env"] = env.DeepClone(), ["prune"] = false, ["pullImage"] = true, ["repullImageAndRedeploy"] = true,
            ["repositoryReferenceName"] = git?["ReferenceName"]?.DeepClone(),
            ["repositoryAuthentication"] = auth is not null,
            ["repositoryUsername"] = auth?["Username"]?.DeepClone(),
            ["repositoryPassword"] = ""
         }, "redeploy the Git stack", ct);
      }

      /// <summary>Creates and starts a standalone compose stack; returns its id.</summary>
      public async Task<int> CreateStackAsync(int endpointId, string name, string content, JsonArray env, CancellationToken ct) {
         using var json = await SendAsync(HttpMethod.Post, $"stacks/create/standalone/string?endpointId={endpointId}", new JsonObject {
            ["name"] = name, ["stackFileContent"] = content, ["env"] = env.DeepClone()
         }, "create the stack", ct, notFoundHint: true);
         return json.RootElement.GetProperty("Id").GetInt32();
      }

      /// <summary>URLs of the registries Portainer knows.</summary>
      public async Task<string[]> RegistryUrlsAsync(CancellationToken ct) {
         using var json = await GetAsync("registries", ct);
         return [.. json.RootElement.EnumerateArray().Select(r => r.TryGetProperty("URL", out var url) ? url.GetString() ?? "" : "")];
      }

      /// <summary>Adds a custom registry with a login.</summary>
      public async Task CreateRegistryAsync(string name, string host, string user, string password, CancellationToken ct) {
         using var _ = await SendAsync(HttpMethod.Post, "registries", new JsonObject {
            ["name"] = name, ["type"] = 3, ["url"] = host, ["authentication"] = true, ["username"] = user, ["password"] = password
         }, "add the registry", ct);
      }

      /// <summary><c>true</c> when <paramref name="url"/> (with or without scheme or trailing slash) is <paramref name="host"/>.</summary>
      public static bool SameRegistry(string url, string host) {
         static string Bare(string value) {
            var text = value.Trim();
            var scheme = text.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) text = text[(scheme + 3)..];
            return text.TrimEnd('/').ToLowerInvariant();
         }

         return Bare(url) == Bare(host);
      }

      /// <summary>Sets <paramref name="name"/> in a Portainer <c>Env</c> array (<c>[{name, value}]</c>), keeping the others.</summary>
      public static JsonArray SetEnv(JsonNode? env, string name, string value) {
         var result = new JsonArray();
         var found = false;
         if (env is JsonArray items) {
            foreach (var item in items) {
               if (item?["name"]?.GetValue<string>() == name) {
                  result.Add(new JsonObject { ["name"] = name, ["value"] = value });
                  found = true;
               }
               else {
                  result.Add(item?.DeepClone());
               }
            }
         }

         if (!found) result.Add(new JsonObject { ["name"] = name, ["value"] = value });
         return result;
      }

      /// <summary>Value of <paramref name="name"/> in a Portainer <c>Env</c> array; <c>null</c> when absent.</summary>
      public static string? GetEnv(JsonNode? env, string name) =>
         env is JsonArray items ? items.FirstOrDefault(i => i?["name"]?.GetValue<string>() == name)?["value"]?.GetValue<string>() : null;

      private static CtnDeployStack ToStack(JsonElement s) => new() {
         Id = s.GetProperty("Id").GetInt32(),
         Name = s.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
         EndpointId = s.TryGetProperty("EndpointId", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0,
         IsGit = s.TryGetProperty("GitConfig", out var g) && g.ValueKind == JsonValueKind.Object
      };

      private async Task<JsonDocument> GetAsync(string path, CancellationToken ct) {
         using var response = await _http.GetAsync(path, ct);
         await EnsureAsync(response, "read " + path, ct, false);
         return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
      }

      private async Task<JsonDocument> SendAsync(HttpMethod method, string path, JsonObject body, string what, CancellationToken ct,
         bool notFoundHint = false) {
         using var request = new HttpRequestMessage(method, path) {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
         };
         using var response = await _http.SendAsync(request, ct);
         await EnsureAsync(response, what, ct, notFoundHint);
         var text = await response.Content.ReadAsStringAsync(ct);
         return JsonDocument.Parse(text.Length == 0 ? "{}" : text);
      }

      private static async Task EnsureAsync(HttpResponseMessage response, string what, CancellationToken ct, bool notFoundHint) {
         if (response.IsSuccessStatusCode) return;

         var text = await response.Content.ReadAsStringAsync(ct);
         string? message = null;
         try {
            using var json = JsonDocument.Parse(text);
            message = json.RootElement.TryGetProperty("details", out var d) ? d.GetString() : null;
            if (string.IsNullOrEmpty(message) && json.RootElement.TryGetProperty("message", out var m)) message = m.GetString();
         }
         catch (JsonException) {
         }

         message ??= text.Length > 300 ? text[..300] : text;
         var hint = response.StatusCode switch {
            HttpStatusCode.Unauthorized => " Check the access token.",
            HttpStatusCode.Forbidden => " The access token's user may not manage this resource.",
            HttpStatusCode.NotFound when notFoundHint => " This Portainer version may be too old for this API (Portainer 2.19 or later is needed).",
            _ => ""
         };
         throw new InvalidOperationException($"Portainer could not {what} (HTTP {(int)response.StatusCode}): {message}.{hint}");
      }

      public void Dispose() {
         _http.Dispose();
         _handler.Dispose();
      }
   }
}
