using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Shared
{
   public partial class EmAppBuilder
   {
      internal ApprovalPanelRegistry ApprovalPanels { get; } = new();

      /// <summary>
      /// Registers the input panel of a step: a module's control that appears on the approval screen when
      /// that step is decided, and whose content is sent together with its decision.
      /// </summary>
      /// <typeparam name="TView">The view of the panel.</typeparam>
      /// <typeparam name="TViewModel">
      /// The view model of the panel. The approval screen fills <see cref="IApprovalPanel.Host"/> and then
      /// calls <see cref="IApprovalPanel.LoadAsync"/>, and calls it again every time its screen is reloaded.
      /// </typeparam>
      /// <param name="docType">The document type.</param>
      /// <param name="stepName">
      /// The step whose panel is attached, the same name as declared by its flow on the server.
      /// </param>
      /// <remarks>
      /// A step that has an input panel is decided one by one through its panel, so it does not take part when
      /// several requests are approved at once from the list.
      /// </remarks>
      /// <exception cref="ArgumentException">
      /// Thrown when the document type or the step name is empty.
      /// </exception>
      /// <exception cref="InvalidOperationException">
      /// Thrown when that step already has an input panel.
      /// </exception>
      public void AddApprovalStepPanel<TView, TViewModel>(string docType, string stepName)
         where TViewModel : IApprovalPanel {
         ArgumentException.ThrowIfNullOrWhiteSpace(docType);
         ArgumentException.ThrowIfNullOrWhiteSpace(stepName);
         ApprovalPanels.Add(new ApprovalStepPanelRegistration(docType, stepName, typeof(TView), typeof(TViewModel)));
      }

      /// <summary>
      /// Registers an info card: a module's control that appears beside the document on the approval screen,
      /// for information the signer needs to see before deciding.
      /// </summary>
      /// <typeparam name="TView">The view of the card.</typeparam>
      /// <param name="docType">The document type where the card appears.</param>
      /// <param name="steps">
      /// The steps where the card appears. Empty means the card appears on all steps of that document type.
      /// </param>
      /// <param name="input">
      /// How to compose the card's initial info from the request being opened. Empty means the card needs
      /// nothing.
      /// </param>
      /// <param name="claim">
      /// The claim the user must hold for the card to appear, or empty when the card is open to anyone who
      /// may view that request.
      /// </param>
      /// <param name="order">The order of the card; a smaller value appears first.</param>
      /// <remarks>
      /// A card is made by the module that owns its data and may be attached by the document type of another
      /// module, so one card can appear on several document types without being written twice.
      /// </remarks>
      /// <exception cref="ArgumentException">Thrown when the document type is empty.</exception>
      public void AddApprovalInfoPanel<TView>(string docType, string[]? steps = null,
         Func<IApprovalPanelHost, object?>? input = null, ClaimAction? claim = null, int order = 0) {
         ArgumentException.ThrowIfNullOrWhiteSpace(docType);
         ApprovalPanels.Add(new ApprovalInfoPanelRegistration(docType, typeof(TView), steps ?? [],
            input, claim, order));
      }

      /// <summary>
      /// Registers how to open the document screen of a document type from the approval screen, so signers
      /// can see the document on its real screen - not only its PDF.
      /// </summary>
      /// <param name="docType">The document type.</param>
      /// <param name="navigationName">The navigation name of its document screen.</param>
      /// <param name="parameter">
      /// How to compose its navigation parameter from the request being opened. Empty means the screen is
      /// opened without a parameter.
      /// </param>
      /// <remarks>
      /// The screen is opened to be read: a document waiting for a decision is locked, so changes are still
      /// refused by the server even though the screen is open.
      /// </remarks>
      /// <exception cref="ArgumentException">
      /// Thrown when the document type or the navigation name is empty.
      /// </exception>
      /// <exception cref="InvalidOperationException">
      /// Thrown when that document type already has a way of opening its document.
      /// </exception>
      public void AddApprovalDocumentOpener(string docType, string navigationName,
         Func<IApprovalPanelHost, object?>? parameter = null) {
         ArgumentException.ThrowIfNullOrWhiteSpace(docType);
         ArgumentException.ThrowIfNullOrWhiteSpace(navigationName);
         ApprovalPanels.Add(new ApprovalDocumentOpenerRegistration(docType, navigationName, parameter));
      }
   }
}
