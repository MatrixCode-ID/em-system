using Em.Shared;
namespace Em.Api.Core.Models;

/// <summary>Columns of the user list that can be sorted on the server.</summary>
public enum UserListSortField
{
   /// <summary>No column chosen: the stable key order of the list.</summary>
   None = 0,
   /// <summary>Account name.</summary>
   Account,
   /// <summary>Full name of the contact.</summary>
   FullName,
   /// <summary>Default communication value, usually the email address.</summary>
   Email,
   /// <summary>Location of the default address.</summary>
   Address,
   /// <summary>Zip of the default address.</summary>
   Zip,
   /// <summary>Account state.</summary>
   State,
}

/// <summary>Conditions applied to the user list before it is sorted and paged.</summary>
public sealed class UserListFilter
{
   /// <summary>Fragment of the account, full name or email; empty accepts every user.</summary>
   public string? Search { get; set; }
   /// <summary>
   /// Account states to show. Empty accepts every state except <see cref="UserState.Deleted"/>, which only
   /// <see cref="IncludeDeleted"/> brings back.
   /// </summary>
   public UserState[] States { get; set; } = [];
   /// <summary>
   /// Roles of which a user must hold at least one. Only assignments in effect now count: an active role
   /// whose period has started and not yet expired - the same rule the server uses to grant permissions.
   /// </summary>
   public string[] RoleIds { get; set; } = [];
   /// <summary>Show only accounts with administrator privileges.</summary>
   public bool AdminOnly { get; set; }
   /// <summary>Show deleted accounts as well.</summary>
   public bool IncludeDeleted { get; set; }
}

/// <summary>One page of the user list, with its conditions and its order.</summary>
public sealed class UserListQuery
{
   /// <summary>One-based page number.</summary>
   public int Page { get; set; } = 1;
   /// <summary>Rows per page.</summary>
   public int PageSize { get; set; } = 200;
   /// <summary>Conditions applied before sorting and paging.</summary>
   public UserListFilter Filter { get; set; } = new();
   /// <summary>Column the page is sorted on; ties always fall back to the stable key order.</summary>
   public UserListSortField SortField { get; set; }
   /// <summary>Sort from the highest value down.</summary>
   public bool SortDescending { get; set; }
}

/// <summary>A role a user holds in effect now, as shown beside that user in the list.</summary>
public sealed class UserListRole
{
   /// <summary>The user holding the role.</summary>
   public string cUserId { get; set; } = string.Empty;
   /// <summary>The role.</summary>
   public string cRoleId { get; set; } = string.Empty;
   /// <summary>Name of the role.</summary>
   public string cRoleName { get; set; } = string.Empty;
}

/// <summary>One page of the user list together with the number of users that match the conditions.</summary>
public sealed class UserListPage
{
   /// <summary>The users on this page, in the same shape as anywhere else a user is read.</summary>
   public vi_User[] Rows { get; set; } = [];
   /// <summary>Roles in effect for the users on this page, ordered by role name.</summary>
   public UserListRole[] Roles { get; set; } = [];
   /// <summary>Number of users matching the conditions across every page.</summary>
   public int TotalRecords { get; set; }
}
