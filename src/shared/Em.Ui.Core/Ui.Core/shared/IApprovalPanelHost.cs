using Em.Api.Core.Models;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// The approval screen as seen from a module's panel: which request is open, what input will be sent
   /// with its decision, and how to ask for the screen to be reloaded.
   /// </summary>
   /// <remarks>
   /// A module panel never holds its own approval screen, only this interface. That is what lets the same
   /// panel be attached on the approval screen, on the approval tab of its document screen, and in the
   /// document-opening mode - without knowing where it is.
   /// <para>
   /// A panel <b>must not</b> change the document being decided: the document is locked while the request
   /// runs. What it may do is show information, run actions on other data, open other screens, fill
   /// <see cref="InputPayload"/>, and ask for its screen to be reloaded through <see cref="RefreshAsync"/>.
   /// </para>
   /// </remarks>
   public interface IApprovalPanelHost
   {
      /// <summary>The request being opened.</summary>
      ApprovalRequestInfo Request { get; }

      /// <summary>
      /// The step being decided, or empty when the panel is not bound to one particular step.
      /// </summary>
      string? StepName { get; }

      /// <summary>
      /// The input that will be sent with the decision, as the module's JSON text. The input panel writes it
      /// every time its content changes; the approval screen sends it as-is, and the server hands it back to
      /// the module to be validated.
      /// </summary>
      string? InputPayload { get; set; }

      /// <summary>
      /// Whether this panel's input is complete so its decision may be sent. Used by the approval screen to
      /// disable the decision buttons while the input is not valid.
      /// </summary>
      bool IsInputValid { get; set; }

      /// <summary>
      /// Asks the approval screen to reload its state: the request details are read again, the "may be
      /// decided now" check is run again, and other panels are reloaded too. Used by a panel that has just
      /// changed something outside its document - for example recording a payment that the step is waiting
      /// for.
      /// </summary>
      Task RefreshAsync();
   }

   /// <summary>
   /// A module's panel attached to the approval screen. Implemented by the panel's view model.
   /// </summary>
   /// <remarks>
   /// The approval screen creates its panel, fills <see cref="Host"/>, then calls
   /// <see cref="LoadAsync"/> - and calls <see cref="LoadAsync"/> again every time its state is reloaded,
   /// so that method must be callable repeatedly.
   /// </remarks>
   public interface IApprovalPanel
   {
      /// <summary>The approval screen that attaches this panel.</summary>
      IApprovalPanelHost Host { get; set; }

      /// <summary>
      /// Loads the panel's content. Called when the panel is attached and every time its screen is reloaded.
      /// </summary>
      Task LoadAsync();
   }
}
