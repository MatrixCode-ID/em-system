namespace Em.Api.Core.Models;
/// <summary>A bounded contact search page with a continuation flag.</summary>
public sealed class ContactSearchPage
{
   /// <summary>Up to fifty contacts in stable name and identity order.</summary>
   public ContactSearchItem[] Rows { get; set; } = [];
   /// <summary>Whether the applied search has a following page.</summary>
   public bool HasMore { get; set; }
}
