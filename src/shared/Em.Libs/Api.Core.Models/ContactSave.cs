using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>A contact with the communication and address entries to create or edit in one transaction.</summary>
public sealed class ContactSave
{
   /// <summary>Contact ULID. Empty lets the server issue one; an unknown ID with a default stamp creates the contact with that ID.</summary>
   public string? ContactId { get; set; }
   /// <summary>Last-read contact update stamp; default for a new contact.</summary>
   public DateTime ContactUstamp { get; set; }
   /// <summary>Full name; required.</summary>
   public string FullName { get; set; } = "";
   /// <summary>Contact type.</summary>
   public ContactType ContactType { get; set; }
   /// <summary>Optional note.</summary>
   public string? Note { get; set; }
   /// <summary>Communication entries to add or edit, each with its last-read stamp. Entries not sent stay as they are.</summary>
   public ta_Comm[]? Comms { get; set; }
   /// <summary>Address entries to add or edit, each with its last-read stamp. Entries not sent stay as they are.</summary>
   public ta_Address[]? Addresses { get; set; }
   /// <summary>Optional default communication: an entry sent here or already owned by this contact.</summary>
   public string? DefaultCommId { get; set; }
   /// <summary>Optional default address: an entry sent here or already owned by this contact.</summary>
   public string? DefaultAddressId { get; set; }
}
