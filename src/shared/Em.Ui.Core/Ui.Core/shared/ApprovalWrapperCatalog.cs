using Em.Api.Core.Models;
namespace Em.Ui.Core.Shared;
/// <summary>Initial filter passed to a module-owned approval screen.</summary>
public sealed class ApprovalWrapperNavigationPayload : NavigationPayloadBase {
   /// <summary>Creates the wrapper filter.</summary>
   public ApprovalWrapperNavigationPayload(ApprovalKind kind, string name, bool waitingForMeOnly = true) : base(null) {
      Kind = kind; Name = name; WaitingForMeOnly = waitingForMeOnly;
   }
   /// <summary>Approval kind.</summary>
   public ApprovalKind Kind { get; }
   /// <summary>Registered approval name.</summary>
   public string Name { get; }
   /// <summary>Whether only the caller's waiting decisions are requested.</summary>
   public bool WaitingForMeOnly { get; }
}
/// <summary>Client registrations that route approval hub groups to module navigations.</summary>
public sealed class ApprovalWrapperCatalog {
   private readonly Dictionary<(ApprovalKind Kind, string Name), string> _items = new();
   /// <summary>Registers one wrapper; duplicate definitions are rejected.</summary>
   public void Add(ApprovalKind kind, string name, string navigationName) {
      ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentException.ThrowIfNullOrWhiteSpace(navigationName);
      if (!_items.TryAdd((kind, name.ToUpperInvariant()), navigationName)) throw new InvalidOperationException("An approval wrapper is already registered.");
   }
   /// <summary>Finds a wrapper by its case-insensitive approval name.</summary>
   public string? Find(ApprovalKind kind, string name) => _items.GetValueOrDefault((kind, name.ToUpperInvariant()));
   /// <summary>Resolves hub work without coupling it to the full manager's administrator access.</summary>
   public ApprovalHubNavigation? Resolve(HubTaskInfo task) {
      if (string.IsNullOrWhiteSpace(task.NavigationTarget)) return null;
      if (task.NavigationTarget == ApprovalManagerNavigationPayload.NavigationName && task.NavigationParameter is { } text &&
          text.IndexOf(':') is > 0 and var split && Enum.TryParse<ApprovalKind>(text[..split], out var kind) && Enum.IsDefined(kind) && text.Length > split + 1) {
         var name = text[(split + 1)..];
         if (Find(kind, name) is { } wrapper) return new(wrapper, new ApprovalWrapperNavigationPayload(kind, name));
         return new("em.approval.editor", new ApprovalManagerNavigationPayload { Kind = kind, Name = name, WaitingForMeOnly = true, SystemViewer = true });
      }
      return new(task.NavigationTarget, task.NavigationParameter);
   }
}
/// <summary>A resolved navigation target and its typed parameter.</summary>
public sealed record ApprovalHubNavigation(string NavigationName, object? Payload);

/// <summary>Optional wrapper hook that reapplies a hub filter when its navigation is already open.</summary>
public interface IApprovalWrapperNavigationTarget {
   /// <summary>Applies the MY TASKS filter and refreshes the module-owned list.</summary>
   Task ApplyApprovalFilterAsync(ApprovalWrapperNavigationPayload payload);
}
