using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// All panels and document screens that modules register with the approval screen, frozen since the
   /// application was built.
   /// </summary>
   /// <remarks>
   /// The approval screen reads it to know what to attach for the document type being opened. Always
   /// available - even when no panel is registered at all - so the screen does not need to know the
   /// difference.
   /// </remarks>
   public class ApprovalPanelRegistry
   {
      private readonly List<ApprovalStepPanelRegistration> _stepPanels = [];
      private readonly List<ApprovalInfoPanelRegistration> _infoPanels = [];
      private readonly List<ApprovalDocumentOpenerRegistration> _documentOpeners = [];

      /// <summary>Input panels per step.</summary>
      public IReadOnlyList<ApprovalStepPanelRegistration> StepPanels => _stepPanels;

      /// <summary>Info cards, already sorted in the order requested by their modules.</summary>
      public IReadOnlyList<ApprovalInfoPanelRegistration> InfoPanels => _infoPanels;

      /// <summary>How to open the document screen of a document type.</summary>
      public IReadOnlyList<ApprovalDocumentOpenerRegistration> DocumentOpeners => _documentOpeners;

      /// <summary>
      /// The input panel of a step, or empty when that step has no panel.
      /// </summary>
      /// <param name="docType">The document type.</param>
      /// <param name="stepName">Name of the step.</param>
      public ApprovalStepPanelRegistration? FindStepPanel(string docType, string stepName) =>
         _stepPanels.FirstOrDefault(r =>
            string.Equals(r.DocType, docType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.StepName, stepName, StringComparison.OrdinalIgnoreCase));

      /// <summary>
      /// The info cards that apply to a step, in order. A card that names no step applies to all steps of
      /// that document type.
      /// </summary>
      /// <param name="docType">The document type.</param>
      /// <param name="stepName">
      /// The step being opened, or empty to take only the cards that apply to all steps.
      /// </param>
      public IReadOnlyList<ApprovalInfoPanelRegistration> FindInfoPanels(string docType, string? stepName) =>
         [.. _infoPanels
            .Where(r => string.Equals(r.DocType, docType, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.Steps.Count == 0 ||
                        (stepName is not null && r.Steps.Contains(stepName, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(r => r.Order)];

      /// <summary>
      /// How to open the document screen of a document type, or empty when its module does not register one.
      /// </summary>
      /// <param name="docType">The document type.</param>
      public ApprovalDocumentOpenerRegistration? FindDocumentOpener(string docType) =>
         _documentOpeners.FirstOrDefault(r =>
            string.Equals(r.DocType, docType, StringComparison.OrdinalIgnoreCase));

      /// <summary>
      /// Adds the input panel of one step. Called while the application is built, through
      /// <c>EmAppBuilder.AddApprovalStepPanel</c>; after that the catalog does not change.
      /// </summary>
      /// <param name="registration">The panel being registered.</param>
      /// <exception cref="InvalidOperationException">
      /// Thrown when that step already has an input panel.
      /// </exception>
      // Not "internal": its callers live in a different UI assembly, and this repo deliberately does not use
      // InternalsVisibleTo anywhere. The limit is therefore a convention, not compiler enforcement - this
      // catalog is only filled while the application is being built.
      public void Add(ApprovalStepPanelRegistration registration) {
         if (FindStepPanel(registration.DocType, registration.StepName) is not null) {
            throw new InvalidOperationException(
               $"Step '{registration.StepName}' of document type '{registration.DocType}' already has an input panel. " +
               "A step takes one panel; put several controls inside it instead.");
         }

         _stepPanels.Add(registration);
      }

      /// <summary>
      /// Adds one info card. Called while the application is built, through
      /// <c>EmAppBuilder.AddApprovalInfoPanel</c>.
      /// </summary>
      /// <param name="registration">The card being registered.</param>
      public void Add(ApprovalInfoPanelRegistration registration) => _infoPanels.Add(registration);

      /// <summary>
      /// Adds how to open the document screen of a document type. Called while the application is built,
      /// through <c>EmAppBuilder.AddApprovalDocumentOpener</c>.
      /// </summary>
      /// <param name="registration">The way of opening the document being registered.</param>
      /// <exception cref="InvalidOperationException">
      /// Thrown when that document type already has a way of opening its document.
      /// </exception>
      public void Add(ApprovalDocumentOpenerRegistration registration) {
         if (FindDocumentOpener(registration.DocType) is not null) {
            throw new InvalidOperationException(
               $"Document type '{registration.DocType}' already has a way to open its document.");
         }

         _documentOpeners.Add(registration);
      }
   }

   /// <summary>The input panel of one step, as registered by the module.</summary>
   /// <param name="DocType">The document type.</param>
   /// <param name="StepName">The step whose panel is attached.</param>
   /// <param name="ViewType">Type of the panel's view.</param>
   /// <param name="ViewModelType">
   /// Type of the panel's view model, which implements <see cref="IApprovalPanel"/>.
   /// </param>
   public record ApprovalStepPanelRegistration(string DocType, string StepName, Type ViewType, Type ViewModelType);

   /// <summary>An info card, as registered by the module.</summary>
   /// <param name="DocType">The document type where this card appears.</param>
   /// <param name="ViewType">Type of the card's view.</param>
   /// <param name="Steps">
   /// The steps where the card appears. Empty means the card appears on all steps of that document type.
   /// </param>
   /// <param name="Input">
   /// How to compose the card's initial info from the request being opened - for example taking the
   /// identity of the party whose data the card shows. Empty means the card needs nothing.
   /// </param>
   /// <param name="Claim">
   /// The claim the user must hold for the card to appear, or empty when the card is open to anyone who
   /// may view that request. Used by cards that show data belonging to another module.
   /// </param>
   /// <param name="Order">Order of the card; a smaller value appears first.</param>
   public record ApprovalInfoPanelRegistration(string DocType, Type ViewType, IReadOnlyList<string> Steps,
      Func<IApprovalPanelHost, object?>? Input, ClaimAction? Claim, int Order);

   /// <summary>How to open the document screen of a document type from the approval screen.</summary>
   /// <param name="DocType">The document type.</param>
   /// <param name="NavigationName">Navigation name of its document screen.</param>
   /// <param name="Parameter">
   /// How to compose its navigation parameter from the request being opened. Empty means the screen is
   /// opened without a parameter.
   /// </param>
   /// <remarks>
   /// The document screen is opened to be read, not to be changed: a document waiting for a decision is
   /// locked.
   /// </remarks>
   public record ApprovalDocumentOpenerRegistration(string DocType, string NavigationName,
      Func<IApprovalPanelHost, object?>? Parameter);
}
