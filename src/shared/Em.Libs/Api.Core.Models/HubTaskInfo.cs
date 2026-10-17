namespace Em.Api.Core.Models;

/// <summary>One computed row in the active user's work list, grouped by its owning source.</summary>
public class HubTaskInfo {
   /// <summary>Source used for grouping.</summary>
   public string Source { get; set; } = string.Empty;
   /// <summary>Identifier within the source.</summary>
   public string Id { get; set; } = string.Empty;
   /// <summary>Displayed title.</summary>
   public string Title { get; set; } = string.Empty;
   /// <summary>Optional detail below the title.</summary>
   public string? Description { get; set; }
   /// <summary>Number of represented work items.</summary>
   public int Count { get; set; }
   /// <summary>Age of the oldest represented item.</summary>
   public TimeSpan? OldestAge { get; set; }
   /// <summary>Optional progress from zero to one hundred.</summary>
   public int? Progress { get; set; }
   /// <summary>Caption of the available action.</summary>
   public string? ActionName { get; set; }
   /// <summary>Target screen for the action.</summary>
   public string? NavigationTarget { get; set; }
   /// <summary>Filter passed to the target screen.</summary>
   public string? NavigationParameter { get; set; }
}
