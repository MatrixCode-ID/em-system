using Em.Shared;
namespace Em.Api.Core.Models;

/// <summary>Caller-owned filters applied by the contact service before paging.</summary>
public sealed class ContactSearchFilter
{
   /// <summary>Exclude organizations, while accepting both person types.</summary>
   public bool PeopleOnly { get; set; }
   /// <summary>Optional exact contact type.</summary>
   public ContactType? Type { get; set; }
   /// <summary>Optional exact lifecycle state; null accepts all states.</summary>
   public ContactState? State { get; set; }
   /// <summary>Whether a linked user must exist or be absent; null accepts both.</summary>
   public bool? HasUser { get; set; }
   /// <summary>Restrict username searches, display and user-presence filtering to active accounts.</summary>
   public bool ActiveUsersOnly { get; set; }
   /// <summary>Contact identities excluded by the calling module.</summary>
   public string[] ExcludedContactIds { get; set; } = [];
}

/// <summary>A bounded contact search with structured filters supplied by its caller.</summary>
public sealed class ContactSearchQuery
{
   /// <summary>Contact name or linked username fragment; empty lists all matching contacts.</summary>
   public string? Search { get; set; }
   /// <summary>One-based page number; each page contains at most fifty contacts.</summary>
   public int Page { get; set; } = 1;
   /// <summary>Filters applied before pagination.</summary>
   public ContactSearchFilter Filter { get; set; } = new();
}
