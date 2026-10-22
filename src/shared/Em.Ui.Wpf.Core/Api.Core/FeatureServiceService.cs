using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Core;

namespace Em.Api.Core;

/// <summary>Client of the shared runtime service controls.</summary>
[Module(Defaults.AdministrativeToolsModuleName)]
public sealed class FeatureServiceService(EmApp app) : ServiceWpfBase(app), IFeatureServiceServices {
   /// <inheritdoc />
   public Task<FeatureServiceState> GetMeta_CdnServiceState() => GetAsync<FeatureServiceState>(nameof(GetMeta_CdnServiceState));
   /// <inheritdoc />
   public Task<FeatureServiceState> PostMeta_CdnServiceState(bool enabled) => PostAsync<FeatureServiceState>(nameof(PostMeta_CdnServiceState), enabled);
   /// <inheritdoc />
   public Task<FeatureServiceState> GetMeta_NuPakServiceState() => GetAsync<FeatureServiceState>(nameof(GetMeta_NuPakServiceState));
   /// <inheritdoc />
   public Task<FeatureServiceState> PostMeta_NuPakServiceState(bool enabled) => PostAsync<FeatureServiceState>(nameof(PostMeta_NuPakServiceState), enabled);
   /// <inheritdoc />
   public Task<FeatureServiceState> GetMeta_ObjectStorageServiceState() => GetAsync<FeatureServiceState>(nameof(GetMeta_ObjectStorageServiceState));
   /// <inheritdoc />
   public Task<FeatureServiceState> PostMeta_ObjectStorageServiceState(bool enabled) => PostAsync<FeatureServiceState>(nameof(PostMeta_ObjectStorageServiceState), enabled);
   /// <inheritdoc />
   public Task<FeatureServiceState> GetMeta_CtnServiceState() => GetAsync<FeatureServiceState>(nameof(GetMeta_CtnServiceState));
   /// <inheritdoc />
   public Task<FeatureServiceState> PostMeta_CtnServiceState(bool enabled) => PostAsync<FeatureServiceState>(nameof(PostMeta_CtnServiceState), enabled);
}
