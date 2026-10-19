using Em.Shared;
namespace Em.Api.Core.Models;
/// <summary>A contact search result with lifecycle state and linked usernames.</summary>
public sealed class ContactSearchItem
{
   /// <summary>Contact identity returned to the calling module.</summary>
   public string ContactId { get; set; } = "";
   /// <summary>Full contact name.</summary>
   public string FullName { get; set; } = "";
   /// <summary>Whether the contact is a person or organization.</summary>
   public ContactType ContactType { get; set; }
   /// <summary>Contact lifecycle state.</summary>
   public ContactState ContactState { get; set; }
   /// <summary>Linked usernames, optionally restricted to active accounts by the search.</summary>
   public string[] UserAccounts { get; set; } = [];
}
