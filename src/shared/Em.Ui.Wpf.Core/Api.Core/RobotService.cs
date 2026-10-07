using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Core;

namespace Em.Api.Core
{
   /// <summary>Client-side implementation of the robot management actions.</summary>
   [Module(Defaults.AdministrativeToolsModuleName)]
   public class RobotService(EmApp emApp) : ServiceWpfBase(emApp), IRobotServices
   {
      /// <inheritdoc />
      public Task<RobotInfo[]> GetMeta_Robots() => GetAsync<RobotInfo[]>(nameof(GetMeta_Robots));
      /// <inheritdoc />
      public Task<RobotAccessManagerInfo[]> GetMeta_RobotManagers() => GetAsync<RobotAccessManagerInfo[]>(nameof(GetMeta_RobotManagers));
      /// <inheritdoc />
      public Task<RobotOwnerInfo[]> GetMeta_RobotOwners() => GetAsync<RobotOwnerInfo[]>(nameof(GetMeta_RobotOwners));
      /// <inheritdoc />
      public Task<RobotToken> PostGetMeta_RobotCreate(string name, string? description, DateTime? tokenExpiry, string? ownerUserId = null) =>
         PostAsync<RobotToken>(nameof(PostGetMeta_RobotCreate), name, description!, tokenExpiry!, ownerUserId!);
      /// <inheritdoc />
      public Task<RobotToken> PostGetMeta_RobotRegenerate(string robotId, DateTime? tokenExpiry) =>
         PostAsync<RobotToken>(nameof(PostGetMeta_RobotRegenerate), robotId, tokenExpiry!);
      /// <inheritdoc />
      public Task PostMeta_RobotUpdate(string robotId, string? description, bool isActive, DateTime? tokenExpiry) =>
         PostAsync(nameof(PostMeta_RobotUpdate), robotId, description!, isActive, tokenExpiry!);
      /// <inheritdoc />
      public Task PostMeta_RobotDelete(string robotId) => PostAsync(nameof(PostMeta_RobotDelete), robotId);
      /// <inheritdoc />
      public Task PostMeta_RobotAccessSet(string robotId, string managerId, string resourceId, string access) =>
         PostAsync(nameof(PostMeta_RobotAccessSet), robotId, managerId, resourceId, access);
   }
}
