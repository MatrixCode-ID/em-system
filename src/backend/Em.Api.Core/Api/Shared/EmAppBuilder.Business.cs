using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Shared;

public partial class EmAppBuilder
{
   private bool _businessModuleManagerAdded;

   /// <summary>
   /// Turns on the Business Module Manager: tenant, department, position, employee, contact attachment and attendance setup actions,
   /// sharing the single
   /// claim <see cref="ITenantServices.ManagerClaim"/> in module <see cref="Defaults.BusinessModuleName"/>.
   /// </summary>
   /// <remarks>
   /// Requires the tenant and organization installation scripts documented in the Business Module Manager guide on the core
   /// database (SQL Server). There is no startup schema check: without the table the first call fails.
   /// </remarks>
   public void AddBusinessModuleManager() {
      if (_businessModuleManagerAdded) throw new InvalidOperationException("The Business Module Manager is already registered.");
      _businessModuleManagerAdded = true;
      AddService<ITenantServices, TenantServices>();
      AddService<ITenantDeptServices, TenantDeptServices>();
      AddService<ITenantEmpServices, TenantEmpServices>();
      AddService<ITenantPositionServices, TenantPositionServices>();
      AddService<ITenantSchedTemplateServices, TenantSchedTemplateServices>();
      AddService<ITenantSchedSettingServices, TenantSchedSettingServices>();
      AddService<ITenantAttCodeServices, TenantAttCodeServices>();
      AddService<IPublicHolidayServices, PublicHolidayServices>();
      AddService<IContactAttachmentServices, ContactAttachmentServices>();
      AddManagedBucket(IContactAttachmentServices.Bucket, "Business Module Manager",
         "Files attached to contacts, such as photos, identity cards and diplomas, written by the Business Module Manager. " +
         "Moving it to another volume does not move existing files; a smaller quota stops new attachments.",
         o => o.MaxObjectBytes = 50L * 1024 * 1024);
      AddClaims(ClaimAction.Create<TenantServices>(ITenantServices.ManagerClaim));
   }
}
