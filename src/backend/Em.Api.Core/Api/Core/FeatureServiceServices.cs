using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core;

/// <summary>Claim-protected service status and administrator-only switches.</summary>
[Module(Defaults.AdministrativeToolsModuleName)]
public sealed class FeatureServiceServices : ServicesBase, IFeatureServiceServices {
   private FeatureServiceRegistry Registry => GetService<FeatureServiceRegistry>()!;
   private async Task<FeatureServiceState> Read(string feature) => new(await Registry.Get(feature).ReadAsync(), CallerIsAdmin);
   private async Task<FeatureServiceState> Save(string feature, bool enabled) {
      var state = Registry.Get(feature);
      Request.RequireAdmin();
      await state.SaveAsync(enabled);
      return new(enabled, CallerIsAdmin);
   }
   /// <inheritdoc />
   [GetAction(claim: ICdnServices.CdnClaim)]
   public Task<FeatureServiceState> GetMeta_CdnServiceState() => Read("Cdn");
   /// <inheritdoc />
   [PostAction(claim: ICdnServices.CdnClaim)]
   public Task<FeatureServiceState> PostMeta_CdnServiceState(bool enabled) => Save("Cdn", enabled);
   /// <inheritdoc />
   [GetAction(claim: INuPakServices.ManagerClaim)]
   public Task<FeatureServiceState> GetMeta_NuPakServiceState() => Read("NuPak");
   /// <inheritdoc />
   [PostAction(claim: INuPakServices.ManagerClaim)]
   public Task<FeatureServiceState> PostMeta_NuPakServiceState(bool enabled) => Save("NuPak", enabled);
   /// <inheritdoc />
   [GetAction(claim: IObjectStorageServices.ManagerClaim)]
   public Task<FeatureServiceState> GetMeta_ObjectStorageServiceState() => Read("ObjectStorage");
   /// <inheritdoc />
   [PostAction(claim: IObjectStorageServices.ManagerClaim)]
   public Task<FeatureServiceState> PostMeta_ObjectStorageServiceState(bool enabled) => Save("ObjectStorage", enabled);
   /// <inheritdoc />
   [GetAction(claim: ICtnServices.CtnClaim)]
   public Task<FeatureServiceState> GetMeta_CtnServiceState() => Read("Ctn");
   /// <inheritdoc />
   [PostAction(claim: ICtnServices.CtnClaim)]
   public Task<FeatureServiceState> PostMeta_CtnServiceState(bool enabled) => Save("Ctn", enabled);
}
