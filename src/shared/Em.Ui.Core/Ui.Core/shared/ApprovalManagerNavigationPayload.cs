namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Parameter for opening the approval screen: which mode is shown, and what it is filtered to.
   /// </summary>
   /// <remarks>
   /// The same screen is used in three ways - opened from the tools menu without a filter, opened from the
   /// task list already filtered to one document type, and embedded in the screen of a document with a
   /// filter to that document only - so what tells them apart is this parameter.
   /// </remarks>
   public class ApprovalManagerNavigationPayload : NavigationPayloadBase
   {
      /// <summary>
      /// Navigation name of the application's built-in approval screen. Opened with this parameter; opening it
      /// without a parameter is the same as opening the "needs my action" mode without a filter.
      /// </summary>
      public const string NavigationName = "em.approval.manager";

      /// <summary>Creates the parameter of the approval screen.</summary>
      public ApprovalManagerNavigationPayload() : base(null) { }

      /// <summary>
      /// <c>true</c> to show only requests waiting for the active user's action, <c>false</c> for all requests
      /// they may see.
      /// </summary>
      public bool WaitingForMeOnly { get; set; } = true;
      /// <summary>Opens the list of requests that can be signed as a substitute.</summary>
      public bool CanSignAsSubstituteOnly { get; set; }
      /// <summary>The search that is kept when a document is opened in its own tab.</summary>
      public string? Search { get; set; }
      /// <summary>The stage that is kept when a document is opened in its own tab.</summary>
      public Em.Api.Core.Models.ApprovalStage? Stage { get; set; }
      /// <summary>Sort column of the originating list.</summary>
      public string? SortBy { get; set; }
      /// <summary>Sort direction of the originating list.</summary>
      public bool SortDescending { get; set; }
      /// <summary>Page of the originating list.</summary>
      public int Page { get; set; } = 1;
      /// <summary>Page size of the originating list.</summary>
      public int PageSize { get; set; } = 50;

      /// <summary>The document type shown, or empty for all types.</summary>
      public string? DocType { get; set; }

      /// <summary>
      /// One specific document whose requests are shown, in its canonical key form. Used when the screen is
      /// embedded in a document screen. Requires <see cref="DocType"/> to be stated too.
      /// </summary>
      public string? DocKey { get; set; }

      /// <summary>
      /// Version of the document whose requests are shown, or empty for all versions of that document.
      /// </summary>
      public string? DocVersion { get; set; }

      /// <summary>
      /// The request that is opened directly as soon as the screen appears, or empty to open just its list.
      /// Used when the screen is opened from a notification or from a link to a single request.
      /// </summary>
      public string? ApprovalRequestId { get; set; }

      /// <summary>
      /// Compact display for an approval screen embedded in a narrow space, e.g. a flyout on a module's
      /// screen: a single column holding the change proposals and the decision. The request list only appears
      /// as a picker when there is more than one, links to other screens are hidden, and the history stays
      /// closed until opened.
      /// </summary>
      public bool Compact { get; set; }

      /// <summary>
      /// Title of the entry opened with this parameter. The document type is included so a list filtered to
      /// two different document types becomes two entries, not one entry whose content keeps changing.
      /// </summary>
      public override string? Title => DocType is null ? null : $"Approval - {DocType}";
   }
}
