using Em.Api.Core.Models;
using Em.Api.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Api.Core.Tests;

public sealed class ContainerManagerRegistrationTests
{
   [Theory]
   [InlineData(true)]
   [InlineData(false)]
   public void HostSwitchControlsServiceActionsAndClaims(bool enabled) {
      var builder = new EmAppBuilder {
         Services = new ServiceCollection(),
         EnableContainerManager = enabled
      };

      EmApp.InitContainerManager(builder);

      Assert.Equal(enabled, builder.ClaimActions.Any(c => c.Name == ICtnServices.CtnClaim));
      Assert.Equal(enabled, builder.Services.Any(s => s.ServiceType == typeof(ICtnServices)));
      Assert.Equal(enabled, builder.ActionDefinitions.Any(a => a.ActionName == "GetMeta_CtnStatus"));
      Assert.Equal(enabled, builder.ActionDefinitions.Any(a => a.RequiredClaim == ICtnServices.CtnClaim));
      if (!enabled) Assert.Empty(builder.ActionDefinitions);
   }
}
