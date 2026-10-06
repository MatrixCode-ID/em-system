using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Em.Api.Core.Registry.Deploy
{
   /// <summary>
   /// The Docker Engine API calls a deploy needs, over any transport: <c>docker system dial-stdio</c> through SSH,
   /// or the Docker proxy of a Portainer environment. Paths are unversioned, so the engine answers with its own
   /// API version.
   /// </summary>
   /// <param name="http">Client whose base address ends with <c>/</c> (for Portainer: <c>.../api/endpoints/{id}/docker/</c>).</param>
   internal sealed class DockerEngineClient(HttpClient http)
   {
      /// <summary>Inspect of a container by name or id; <c>null</c> when it does not exist.</summary>
      public async Task<JsonDocument?> InspectContainerAsync(string nameOrId, CancellationToken ct) {
         using var response = await http.GetAsync("containers/" + Uri.EscapeDataString(nameOrId) + "/json", ct);
         if (response.StatusCode == HttpStatusCode.NotFound) return null;
         await EnsureAsync(response, "inspect container " + nameOrId, ct);
         return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
      }

      /// <summary>Inspect of an image; <c>null</c> when it does not exist.</summary>
      public async Task<JsonDocument?> InspectImageAsync(string nameOrId, CancellationToken ct) {
         using var response = await http.GetAsync("images/" + Uri.EscapeDataString(nameOrId) + "/json", ct);
         if (response.StatusCode == HttpStatusCode.NotFound) return null;
         await EnsureAsync(response, "inspect image " + nameOrId, ct);
         return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
      }

      /// <summary>Names of every container, without the leading <c>/</c>.</summary>
      public async Task<string[]> ContainerNamesAsync(CancellationToken ct) {
         using var response = await http.GetAsync("containers/json?all=1", ct);
         await EnsureAsync(response, "list containers", ct);
         using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
         return [.. json.RootElement.EnumerateArray()
            .SelectMany(c => c.TryGetProperty("Names", out var names) ? names.EnumerateArray().Select(n => n.GetString() ?? "") : [])
            .Select(n => n.TrimStart('/')).Where(n => n.Length > 0).OrderBy(n => n, StringComparer.Ordinal)];
      }

      /// <summary>Server version, for the connection test.</summary>
      public async Task<string> VersionAsync(CancellationToken ct) {
         using var response = await http.GetAsync("version", ct);
         await EnsureAsync(response, "read the Docker version", ct);
         using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
         return json.RootElement.TryGetProperty("Version", out var version) ? version.GetString() ?? "?" : "?";
      }

      /// <summary>
      /// Pulls <paramref name="repository"/> at <paramref name="digest"/> and reads the progress stream to the end;
      /// an error reported inside the stream fails the pull.
      /// </summary>
      /// <param name="registryAuth">Value for <c>X-Registry-Auth</c>, or <c>null</c> for an anonymous pull.</param>
      public async Task PullAsync(string repository, string digest, string? registryAuth, CancellationToken ct) {
         using var request = new HttpRequestMessage(HttpMethod.Post,
            "images/create?fromImage=" + Uri.EscapeDataString(repository) + "&tag=" + Uri.EscapeDataString(digest));
         if (registryAuth is not null) request.Headers.TryAddWithoutValidation("X-Registry-Auth", registryAuth);
         using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
         await EnsureAsync(response, "pull " + repository + "@" + digest, ct);

         using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
         while (await reader.ReadLineAsync(ct) is { } line) {
            if (line.Length == 0) continue;
            try {
               using var progress = JsonDocument.Parse(line);
               if (progress.RootElement.TryGetProperty("error", out var error)) {
                  throw new InvalidOperationException("Pull failed: " + error.GetString());
               }
            }
            catch (JsonException) {
               // Progress lines that are not JSON carry nothing to check.
            }
         }
      }

      /// <summary>Creates a container and returns its id.</summary>
      public async Task<string> CreateAsync(string name, JsonObject body, CancellationToken ct) {
         using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
         using var response = await http.PostAsync("containers/create?name=" + Uri.EscapeDataString(name), content, ct);
         await EnsureAsync(response, "create container " + name, ct);
         var created = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
         return created.GetProperty("Id").GetString() ?? throw new InvalidOperationException("Docker did not return the new container id.");
      }

      /// <summary>Connects a container to one more network.</summary>
      public async Task ConnectNetworkAsync(string network, string container, JsonObject endpoint, CancellationToken ct) {
         var body = new JsonObject { ["Container"] = container, ["EndpointConfig"] = endpoint.DeepClone() };
         using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
         using var response = await http.PostAsync("networks/" + Uri.EscapeDataString(network) + "/connect", content, ct);
         await EnsureAsync(response, "connect network " + network, ct);
      }

      /// <summary>Stops a container; one that is already stopped is fine.</summary>
      public async Task StopAsync(string id, CancellationToken ct) {
         using var response = await http.PostAsync("containers/" + id + "/stop", null, ct);
         if (response.StatusCode == HttpStatusCode.NotModified) return;
         await EnsureAsync(response, "stop container", ct);
      }

      /// <summary>Starts a container; one that already runs is fine.</summary>
      public async Task StartAsync(string id, CancellationToken ct) {
         using var response = await http.PostAsync("containers/" + id + "/start", null, ct);
         if (response.StatusCode == HttpStatusCode.NotModified) return;
         await EnsureAsync(response, "start container", ct);
      }

      /// <summary>Renames a container.</summary>
      public async Task RenameAsync(string id, string name, CancellationToken ct) {
         using var response = await http.PostAsync("containers/" + id + "/rename?name=" + Uri.EscapeDataString(name), null, ct);
         await EnsureAsync(response, "rename container to " + name, ct);
      }

      /// <summary>Removes a container. Its volumes are kept.</summary>
      public async Task RemoveAsync(string id, bool force, CancellationToken ct) {
         using var response = await http.DeleteAsync("containers/" + id + "?v=0&force=" + (force ? "1" : "0"), ct);
         if (response.StatusCode == HttpStatusCode.NotFound) return;
         await EnsureAsync(response, "remove container", ct);
      }

      private static async Task EnsureAsync(HttpResponseMessage response, string what, CancellationToken ct) {
         if (response.IsSuccessStatusCode) return;

         var text = await response.Content.ReadAsStringAsync(ct);
         string? message = null;
         try {
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.TryGetProperty("message", out var m)) message = m.GetString();
         }
         catch (JsonException) {
         }

         message ??= text.Length > 300 ? text[..300] : text;
         throw new InvalidOperationException($"Docker could not {what} (HTTP {(int)response.StatusCode}): {message}".TrimEnd(' ', ':'));
      }

      /// <summary><c>X-Registry-Auth</c> value: base64url of the auth JSON, as the Docker CLI sends it.</summary>
      public static string RegistryAuth(string username, string password, string server) {
         var json = JsonSerializer.Serialize(new Dictionary<string, string> {
            ["username"] = username, ["password"] = password, ["serveraddress"] = server
         });
         return Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).Replace('+', '-').Replace('/', '_');
      }
   }
}
