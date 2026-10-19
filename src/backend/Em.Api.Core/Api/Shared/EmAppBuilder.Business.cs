using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Shared;

public partial class EmAppBuilder
{
   private bool _businessModuleManagerAdded;

   /// <summary>
   /// Turns on the Business Module Manager: the tenant actions of <see cref="ITenantServices"/> and its single
   /// claim <see cref="ITenantServices.ManagerClaim"/> in module <see cref="Defaults.BusinessModuleName"/>.
   /// </summary>
   /// <remarks>
   /// Requires <c>doc/sqlscript/mssql/tables/080-tenant.sql</c> and <c>views/vi_Tenant.sql</c> on the core
   /// database (SQL Server). There is no startup schema check: without the table the first call fails.
   /// </remarks>
   public void AddBusinessModuleManager() {
      if (_businessModuleManagerAdded) throw new InvalidOperationException("The Business Module Manager is already registered.");
      _businessModuleManagerAdded = true;
      AddService<ITenantServices, TenantServices>();
      AddClaims(ClaimAction.Create<TenantServices>(ITenantServices.ManagerClaim));
   }
}
