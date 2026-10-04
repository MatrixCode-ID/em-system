namespace Em.Api.Shared;

public partial class EmAppBuilder
{
   /// <summary>
   /// Menerapkan <see cref="EmApiConfig"/>: koneksi database, password awal admin, key token debug,
   /// pengaturan HTTP/proxy/session, dan folder cache business task. Fitur yang memakai
   /// <see cref="EmApiConfig.Storage"/> (binary, CDN, registry) tetap dinyalakan host dengan path dari config.
   /// </summary>
   /// <exception cref="InvalidOperationException">Dilempar kalau config tidak lolos <see cref="EmApiConfig.Validate"/>.</exception>
   public void ApplyConfig(EmApiConfig config) {
      config.Validate();

      SetDbProvider(config.Database.ConnectionString!, config.Database.Provider);
      foreach (var (name, extra) in config.Database.Extra) {
         AddExtraDbConn(name, extra.ConnectionString!, extra.Provider);
      }

      FirstTimeAdminPassword = config.Admin.InitialPassword!;

      foreach (var token in config.DebugTokens.Where(t => !string.IsNullOrWhiteSpace(t.PublicKey))) {
         AddDebugToken(token.Name, token.PublicKey!, token.Days);
      }

      if (config.Http.RequestTimeoutSeconds is { } timeout) {
         HttpRequestTimeout = TimeSpan.FromSeconds(timeout);
      }

      if (config.Http.ActionRateLimit is { } rateLimit) {
         ActionRateLimit = rateLimit;
      }

      var proxy = config.Http.Proxy;
      if (proxy.Trusted.Count > 0) {
         TrustProxy([.. proxy.Trusted]);
      }

      if (proxy.TrustAny) {
         TrustAnyProxy();
      }

      if (proxy.HopLimit is { } hopLimit) {
         ProxyHopLimit = hopLimit;
      }

      if (config.Session.TokenRetentionHours is { } retention) {
         SessionTokenRetentionHour = retention;
      }

      BusinessTaskCachePath = config.Storage.TaskCachePath;
   }
}
