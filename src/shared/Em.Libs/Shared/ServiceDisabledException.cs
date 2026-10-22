namespace Em.Shared;

/// <summary>An internal engine service is temporarily disabled by its administrator (HTTP 503).</summary>
public sealed class ServiceDisabledException : ActionException {
   /// <summary>Creates the failure for the named service.</summary>
   public ServiceDisabledException(string service) : base($"{service} is disabled", 503) { }
}
