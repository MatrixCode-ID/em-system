namespace Em.Api.Core.Models;

/// <summary>The shared contact's type, communication and address entries, defaults and last-read stamps.</summary>
public sealed class TenantEmpPerson
{
   /// <summary>Whether to save the complete editable collections instead of the legacy default fields.</summary>
   public bool EditCollections { get; set; }
   /// <summary>Contact type to save; null preserves the type for older callers.</summary>
   public Em.Shared.ContactType? ContactType { get; set; }
   /// <summary>Communication rows to add or edit; null uses the legacy default-only input.</summary>
   public ta_Comm[]? Comms { get; set; }
   /// <summary>Address rows to add or edit; null uses the legacy default-only input.</summary>
   public ta_Address[]? Addresses { get; set; }
   /// <summary>Selected default communication ID when Comms is supplied.</summary>
   public string? DefaultCommId { get; set; }
   /// <summary>Selected default address ID when Addresses is supplied.</summary>
   public string? DefaultAddressId { get; set; }
   /// <summary>Person owning both defaults; empty when creating a new person.</summary>
   public string ContactId { get; set; } = "";
   /// <summary>Last-read person update stamp, protecting the default links.</summary>
   public DateTime ContactUstamp { get; set; }
   /// <summary>Default communication details; null when no default exists.</summary>
   public ta_Comm? Comm { get; set; }
   /// <summary>Default address details; null when no default exists.</summary>
   public ta_Address? Address { get; set; }
}
