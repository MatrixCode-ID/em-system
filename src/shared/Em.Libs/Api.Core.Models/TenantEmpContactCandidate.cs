using Em.Shared;
namespace Em.Api.Core.Models;
/// <summary>A person available for employment in the selected tenant.</summary>
public sealed class TenantEmpContactCandidate
{
   /// <summary>Person ID.</summary>
   public string ContactId { get; set; } = "";
   /// <summary>Full name.</summary>
   public string FullName { get; set; } = "";
   /// <summary>Contact lifecycle state, displayed separately from employment membership.</summary>
   public ContactState ContactState { get; set; }
   /// <summary>Person type.</summary>
   public ContactType ContactType { get; set; }
   /// <summary>Linked user accounts of this person; the legacy candidate action includes active accounts only.</summary>
   public string[] UserAccounts { get; set; } = [];
   /// <summary>Existing employee ID in this tenant; null when available.</summary>
   public string? TenantEmpId { get; set; }
}
