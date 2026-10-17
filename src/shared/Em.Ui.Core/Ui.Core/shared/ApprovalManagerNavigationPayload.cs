using Em.Api.Core.Models;
namespace Em.Ui.Core.Shared;
/// <summary>Filters the shared approval screen, including its embedded Compact mode.</summary>
public sealed class ApprovalManagerNavigationPayload : NavigationPayloadBase {
   /// <summary>Built-in manager navigation name.</summary>
   public const string NavigationName = "em.approval.manager";
   /// <summary>Creates an unfiltered manager parameter.</summary>
   public ApprovalManagerNavigationPayload() : base(null) { }
   /// <summary>Approval kind filter.</summary>
   public ApprovalKind? Kind { get; set; }
   /// <summary>Stable approval name filter.</summary>
   public string? Name { get; set; }
   /// <summary>Opaque subject key filter.</summary>
   public string? SubjectKey { get; set; }
   /// <summary>Directly opened request, if specified.</summary>
   public string? ApprovalRequestId { get; set; }
   /// <summary>Limits the initial list to the caller's decisions.</summary>
   public bool WaitingForMeOnly { get; set; } = true;
   /// <summary>Embeds one subject in a narrow module-owned flyout.</summary>
   public bool Compact { get; set; }
   /// <summary>Opens a System request in the full-width viewer without the manager's list.</summary>
   public bool SystemViewer { get; set; }
   /// <summary>Navigation title includes the approval identity.</summary>
   public override string? Title => Name is null ? null : $"Approval - {Name}";
}
