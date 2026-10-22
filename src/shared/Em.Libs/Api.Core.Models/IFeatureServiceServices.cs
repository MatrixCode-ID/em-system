namespace Em.Api.Core.Models;

/// <summary>Runtime status of an enabled host feature's service.</summary>
/// <param name="Enabled">Whether public and internal use is enabled.</param>
/// <param name="CanManage">Whether the current caller is an administrator.</param>
public sealed record FeatureServiceState(bool Enabled, bool CanManage);

/// <summary>Uniform runtime service switches; management remains available while disabled.</summary>
public interface IFeatureServiceServices : Em.Shared.IServices {
   /// <summary>Reads the CDN service status.</summary>
   Task<FeatureServiceState> GetMeta_CdnServiceState();
   /// <summary>Saves the CDN service status, administrators only.</summary>
   Task<FeatureServiceState> PostMeta_CdnServiceState(bool enabled);
   /// <summary>Reads the NuGet feed service status.</summary>
   Task<FeatureServiceState> GetMeta_NuPakServiceState();
   /// <summary>Saves the NuGet feed service status, administrators only.</summary>
   Task<FeatureServiceState> PostMeta_NuPakServiceState(bool enabled);
   /// <summary>Reads the Object Storage service status.</summary>
   Task<FeatureServiceState> GetMeta_ObjectStorageServiceState();
   /// <summary>Saves the Object Storage service status, administrators only.</summary>
   Task<FeatureServiceState> PostMeta_ObjectStorageServiceState(bool enabled);
   /// <summary>Reads the Container Registry service status.</summary>
   Task<FeatureServiceState> GetMeta_CtnServiceState();
   /// <summary>Saves the Container Registry service status, administrators only.</summary>
   Task<FeatureServiceState> PostMeta_CtnServiceState(bool enabled);
}
