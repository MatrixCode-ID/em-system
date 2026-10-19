namespace Em.Api.Core.Models;

/// <summary>The shared person's default communication and address, with their last-read stamps.</summary>
public sealed class TenantEmpPerson
{
   /// <summary>Person owning both defaults; empty when creating a new person.</summary>
   public string ContactId { get; set; } = "";
   /// <summary>Last-read person update stamp, protecting the default links.</summary>
   public DateTime ContactUstamp { get; set; }
   /// <summary>Default communication details; null when no default exists.</summary>
   public ta_Comm? Comm { get; set; }
   /// <summary>Default address details; null when no default exists.</summary>
   public ta_Address? Address { get; set; }
}
