using Em.Api.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Em.Api.Shared;

public partial class EmAppBuilder
{
   internal List<(string Prefix, RequestDelegate Handler)> PublicEndpoints { get; } = [];

   public void AddRobotAccessManager<T>() where T : class, IRobotAccessManager =>
      Services.AddScoped<IRobotAccessManager, T>();

   /// <summary>Register module infrastructure while keeping the service collection builder-only.</summary>
   public void AddSingleton<T>(Func<IServiceProvider, T> factory) where T : class => Services.AddSingleton(factory);
   public void AddHostedService<T>() where T : class, IHostedService => Services.AddHostedService<T>();

   /// <summary>Public protocol branch, outside action claims, timeouts and rate limits.</summary>
   public void AddPublicEndpoint(string pathPrefix, RequestDelegate handler) {
      ArgumentNullException.ThrowIfNull(handler);
      if (string.IsNullOrWhiteSpace(pathPrefix) || !pathPrefix.StartsWith('/') || pathPrefix == "/" ||
          pathPrefix.Contains('\\') || pathPrefix.Contains('%') || pathPrefix.Any(char.IsWhiteSpace) || pathPrefix.Contains('?') || pathPrefix.Contains('#') || pathPrefix.Contains("//") ||
          pathPrefix.Split('/').Any(s => s is "." or ".."))
         throw new InvalidOperationException("A public endpoint requires a non-root absolute path prefix.");
      pathPrefix = pathPrefix.TrimEnd('/');
      static bool Overlaps(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
         a.StartsWith(b + "/", StringComparison.OrdinalIgnoreCase) || b.StartsWith(a + "/", StringComparison.OrdinalIgnoreCase);
      if (new[] { "/api", "/cdn", "/v2" }.Concat(PublicEndpoints.Select(e => e.Prefix)).Any(p => Overlaps(p, pathPrefix)))
         throw new InvalidOperationException($"Public endpoint '{pathPrefix}' overlaps another endpoint.");
      PublicEndpoints.Add((pathPrefix, handler));
   }
}
