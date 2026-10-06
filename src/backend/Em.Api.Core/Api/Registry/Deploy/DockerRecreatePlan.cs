using System.Text.Json;
using System.Text.Json.Nodes;

namespace Em.Api.Core.Registry.Deploy
{
   /// <summary>
   /// Builds the <c>POST /containers/create</c> body that recreates a container from its
   /// <c>GET /containers/{id}/json</c> answer with another image, the way <c>docker compose</c> and
   /// Watchtower do: the old <c>Config</c>, <c>HostConfig</c> and networks are kept, only the image changes.
   /// </summary>
   internal static class DockerRecreatePlan
   {
      /// <summary>The create body plus the networks that are connected after create (the API accepts one at create on older engines).</summary>
      public sealed record Plan(JsonObject Body, IReadOnlyList<(string Network, JsonObject Endpoint)> ExtraNetworks);

      // Config values the old image supplied are dropped, so the new image's defaults apply.
      private static readonly string[] ImageDefaults = ["Cmd", "Entrypoint", "WorkingDir", "User", "Healthcheck", "StopSignal", "Shell", "OnBuild"];

      // Read-only or per-instance fields of an endpoint that a new container cannot take over.
      private static readonly string[] EndpointRuntime = [
         "NetworkID", "EndpointID", "Gateway", "IPAddress", "IPPrefixLen", "IPv6Gateway", "GlobalIPv6Address",
         "GlobalIPv6PrefixLen", "DNSNames"
      ];

      /// <param name="inspect">The old container, as returned by <c>GET /containers/{id}/json</c>.</param>
      /// <param name="newImage">Image reference for the new container.</param>
      /// <param name="oldImageConfig">
      /// <c>Config</c> of the old container's image, or <c>null</c> when unknown. Values equal to it are left out so the
      /// new image's own defaults (command, environment, labels, ports, volumes) apply.
      /// </param>
      public static Plan Build(JsonElement inspect, string newImage, JsonElement? oldImageConfig) {
         var config = JsonNode.Parse(inspect.GetProperty("Config").GetRawText())!.AsObject();
         var host = inspect.TryGetProperty("HostConfig", out var hostConfig) && hostConfig.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(hostConfig.GetRawText())!.AsObject()
            : new JsonObject();
         var id = inspect.TryGetProperty("Id", out var idValue) ? idValue.GetString() ?? "" : "";
         var shortId = id.Length >= 12 ? id[..12] : id;

         config["Image"] = newImage;

         // Docker uses the short id as host name when none was set; the new container gets its own.
         if (config["Hostname"]?.GetValue<string>() is { } hostname && hostname == shortId) config.Remove("Hostname");

         if (oldImageConfig is { ValueKind: JsonValueKind.Object } image) StripImageDefaults(config, image);

         KeepAnonymousVolumes(inspect, host);

         var body = config;
         body["HostConfig"] = host;

         var extra = new List<(string, JsonObject)>();
         var networkMode = host["NetworkMode"]?.GetValue<string>() ?? "";
         if (inspect.TryGetProperty("NetworkSettings", out var settings) &&
             settings.TryGetProperty("Networks", out var networks) && networks.ValueKind == JsonValueKind.Object &&
             !IsSpecialNetworkMode(networkMode)) {
            var endpoints = new JsonObject();
            foreach (var network in networks.EnumerateObject()) {
               var endpoint = CleanEndpoint(network.Value, shortId);
               if (endpoints.Count == 0 && (network.Name == networkMode || networkMode is "" or "default" || !networks.TryGetProperty(networkMode, out _))) {
                  endpoints[network.Name] = endpoint;
               }
               else {
                  extra.Add((network.Name, endpoint));
               }
            }

            if (endpoints.Count > 0) body["NetworkingConfig"] = new JsonObject { ["EndpointsConfig"] = endpoints };
         }

         return new Plan(body, extra);
      }

      private static bool IsSpecialNetworkMode(string mode) =>
         mode is "host" or "none" || mode.StartsWith("container:", StringComparison.Ordinal);

      private static JsonObject CleanEndpoint(JsonElement endpoint, string shortId) {
         var result = JsonNode.Parse(endpoint.GetRawText())!.AsObject();
         foreach (var name in EndpointRuntime) result.Remove(name);

         // The old container's short id is an automatic alias; the new one gets its own.
         if (result["Aliases"] is JsonArray aliases) {
            var keep = aliases.Where(a => a?.GetValue<string>() != shortId).Select(a => a?.DeepClone()).ToArray();
            result["Aliases"] = keep.Length == 0 ? null : new JsonArray(keep);
         }

         return result;
      }

      private static void StripImageDefaults(JsonObject config, JsonElement image) {
         foreach (var name in ImageDefaults) {
            if (config[name] is { } value && image.TryGetProperty(name, out var imageValue) &&
                JsonNode.DeepEquals(value, JsonNode.Parse(imageValue.GetRawText()))) {
               config.Remove(name);
            }
         }

         if (config["Env"] is JsonArray env && image.TryGetProperty("Env", out var imageEnv) && imageEnv.ValueKind == JsonValueKind.Array) {
            var defaults = imageEnv.EnumerateArray().Select(e => e.GetString()).ToHashSet(StringComparer.Ordinal);
            config["Env"] = new JsonArray(env.Where(e => !defaults.Contains(e?.GetValue<string>())).Select(e => e?.DeepClone()).ToArray());
         }

         foreach (var name in new[] { "Labels", "ExposedPorts", "Volumes" }) {
            if (config[name] is not JsonObject values || !image.TryGetProperty(name, out var imageValues) ||
                imageValues.ValueKind != JsonValueKind.Object) {
               continue;
            }

            foreach (var item in imageValues.EnumerateObject()) {
               if (values[item.Name] is { } value && (name != "Labels" || JsonNode.DeepEquals(value, JsonNode.Parse(item.Value.GetRawText())))) {
                  values.Remove(item.Name);
               }
            }
         }
      }

      // An anonymous volume is not in HostConfig; without this the new container would get a new, empty one.
      private static void KeepAnonymousVolumes(JsonElement inspect, JsonObject host) {
         if (!inspect.TryGetProperty("Mounts", out var mounts) || mounts.ValueKind != JsonValueKind.Array) return;

         var covered = new HashSet<string>(StringComparer.Ordinal);
         if (host["Binds"] is JsonArray binds) {
            foreach (var bind in binds) {
               var parts = bind?.GetValue<string>().Split(':') ?? [];
               if (parts.Length >= 2) covered.Add(parts[1]);
            }
         }

         if (host["Mounts"] is JsonArray hostMounts) {
            foreach (var mount in hostMounts) {
               if (mount?["Target"]?.GetValue<string>() is { } target) covered.Add(target);
            }
         }

         var added = new List<JsonNode?>();
         foreach (var mount in mounts.EnumerateArray()) {
            if (mount.TryGetProperty("Type", out var type) && type.GetString() == "volume" &&
                mount.TryGetProperty("Name", out var name) && name.GetString() is { Length: > 0 } volume &&
                mount.TryGetProperty("Destination", out var destination) && destination.GetString() is { Length: > 0 } path &&
                !covered.Contains(path)) {
               var readWrite = !mount.TryGetProperty("RW", out var rw) || rw.ValueKind != JsonValueKind.False;
               added.Add(JsonValue.Create($"{volume}:{path}{(readWrite ? "" : ":ro")}"));
            }
         }

         if (added.Count == 0) return;

         var all = host["Binds"] is JsonArray existing ? existing.Select(b => b?.DeepClone()).Concat(added).ToArray() : added.ToArray();
         host["Binds"] = new JsonArray(all);
      }
   }
}
