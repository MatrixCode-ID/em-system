using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Where a module writes the approval flow of a document type: its levels, its PDF, its summary
   /// columns, and the things that happen along the way.
   /// </summary>
   /// <typeparam name="TServices">Service of the module that owns the document.</typeparam>
   /// <typeparam name="TKey">Key record of the document, each part marked with <c>KeyPart</c>.</typeparam>
   /// <remarks>
   /// Every method returns its own builder, so the declaration may be written chained or line by line.
   /// </remarks>
   /// <example>
   /// <code>
   /// builder.AddDocumentApproval&lt;MyServices, MyKey&gt;(MyApproval.DocType, flow => {
   ///    flow.Level(1, l => l.Step(MyApproval.PreparedBy, ApprovalSlot.At(15, 245),
   ///       signers: ctx => Task.FromResult&lt;IReadOnlyList&lt;string&gt;&gt;([ctx.RequesterId])));
   ///    flow.Level(2, l => l.Step(MyApproval.CheckedBy, ApprovalSlot.At(52, 245),
   ///       distinctFrom: MyApproval.PreparedBy));
   ///    flow.Pdf(ctx => ctx.Services.GetReport_MyDoc(ctx.DocKey.Number, ctx.DocVersion));
   /// });
   /// </code>
   /// </example>
   public class ApprovalFlowBuilder<TServices, TKey>
      where TServices : ServicesBase, IServices
      where TKey : notnull
   {
      private readonly ApprovalDocumentFlowDeclaration<TServices, TKey> _declaration;
      private readonly Func<string, ClaimAction> _claimFactory;

      internal ApprovalFlowBuilder(ApprovalDocumentFlowDeclaration<TServices, TKey> declaration,
         Func<string, ClaimAction> claimFactory) {
         _declaration = declaration;
         _claimFactory = claimFactory;
      }

      /// <summary>
      /// Adds one level to the flow. Levels are walked from the smallest number, and the next level only
      /// starts after all steps of this level are approved.
      /// </summary>
      /// <param name="level">Number of the level. Must not already be used by another level.</param>
      /// <param name="steps">Callback that writes the steps of this level.</param>
      /// <param name="onCompleted">
      /// Runs inside the transaction as soon as all steps of this level are approved, for milestones in the
      /// middle of the flow - a document status that changes before the last step. Its failure restores the
      /// decision that triggered it.
      /// </param>
      /// <exception cref="ArgumentException">Thrown when the level number is already used.</exception>
      public ApprovalFlowBuilder<TServices, TKey> Level(int level,
         Action<ApprovalLevelBuilder<TServices, TKey>> steps,
         Func<IApprovalContext<TServices, TKey>, Task>? onCompleted = null) {
         ArgumentNullException.ThrowIfNull(steps);

         if (_declaration.Levels.Any(r => r.Level == level)) {
            throw new ArgumentException(
               $"Approval flow for '{_declaration.DocType}' already declares level {level}. " +
               "Several steps running at once belong in one Level call, not in two.", nameof(level));
         }

         var declaration = new ApprovalLevelDeclaration<TServices, TKey> {
            Level = level,
            OnCompleted = onCompleted
         };

         steps(new ApprovalLevelBuilder<TServices, TKey>(declaration, _declaration.DocType, _claimFactory));
         _declaration.Levels.Add(declaration);
         return this;
      }

      /// <summary>
      /// States that this document type has a PDF, together with how to fetch it. The PDF is fetched once at
      /// submission and then frozen, so signers always see the same content.
      /// </summary>
      /// <param name="render">
      /// How to fetch the PDF content of the document. Usually the module's own report action.
      /// </param>
      public ApprovalFlowBuilder<TServices, TKey> Pdf(Func<IApprovalContext<TServices, TKey>, Task<Stream>> render) {
         ArgumentNullException.ThrowIfNull(render);
         _declaration.Pdf = render;
         return this;
      }

      /// <summary>
      /// States that the PDF layout of this document changes with its content - number of rows, sections that
      /// appear, pages that split - so the declared box positions only apply to one shape of document and
      /// must be located again on each request's base PDF.
      /// </summary>
      /// <param name="locate">
      /// Receives a copy of the freshly created base PDF, and returns a mapping from the declared position
      /// to the actual position on that PDF. A mapping that returns <c>null</c> means the box does not exist
      /// on this document and is not drawn. The copy may be read to the end.
      /// </param>
      public ApprovalFlowBuilder<TServices, TKey> PdfLayout(
         Func<IApprovalContext<TServices, TKey>, Stream, Task<Func<ApprovalSlot, ApprovalSlot?>>> locate) {
         ArgumentNullException.ThrowIfNull(locate);
         _declaration.PdfLayout = locate;
         return this;
      }

      /// <summary>
      /// States the module's own summary columns shown in the request list, and how to compute them.
      /// Computed at submission and stored with it, so the request list can filter and sort them without
      /// loading the document.
      /// </summary>
      /// <param name="summary">
      /// How to compute the summary, as pairs of column name and value.
      /// </param>
      public ApprovalFlowBuilder<TServices, TKey> Summary(
         Func<IApprovalContext<TServices, TKey>, Task<IReadOnlyDictionary<string, string?>>> summary) {
         ArgumentNullException.ThrowIfNull(summary);
         _declaration.Summary = summary;
         return this;
      }

      /// <summary>
      /// States that decisions on this document type may only be taken after the document is opened, so it
      /// cannot be approved en masse from the list. Used for documents that must be read before they are
      /// signed.
      /// </summary>
      public ApprovalFlowBuilder<TServices, TKey> RequireOpen() {
         _declaration.RequireOpen = true;
         return this;
      }

      /// <summary>
      /// Turns on an extra page in the PDF, containing a table of all steps with their signatures. Used for
      /// documents whose signature boxes are not enough for the whole flow.
      /// </summary>
      public ApprovalFlowBuilder<TServices, TKey> ApprovalSheet() {
         _declaration.ApprovalSheet = true;
         return this;
      }

      /// <summary>Sets what runs after the submission is saved.</summary>
      /// <param name="hook">What runs. Its failure does not cancel the submission.</param>
      public ApprovalFlowBuilder<TServices, TKey> OnSubmitted(Func<IApprovalContext<TServices, TKey>, Task> hook) {
         _declaration.OnSubmitted = hook;
         return this;
      }

      /// <summary>
      /// Sets the check of a decision before it is written, inside its transaction. This is where the
      /// per-step input is validated again on the server.
      /// </summary>
      /// <param name="hook">What runs. Throwing means that decision fails.</param>
      public ApprovalFlowBuilder<TServices, TKey> OnSigning(
         Func<IApprovalStepContext<TServices, TKey>, ApprovalDecision, Task> hook) {
         _declaration.OnSigning = hook;
         return this;
      }

      /// <summary>
      /// Sets the writing of the effect of a signature to its document, inside the same transaction.
      /// </summary>
      /// <param name="hook">What runs. Throwing means the signature is cancelled too.</param>
      public ApprovalFlowBuilder<TServices, TKey> OnSigned(
         Func<IApprovalStepContext<TServices, TKey>, ApprovalDecision, Task> hook) {
         _declaration.OnSigned = hook;
         return this;
      }

      /// <summary>Sets what runs inside the transaction when the request is about to complete.</summary>
      /// <param name="hook">What runs. Throwing means the final decision is cancelled.</param>
      public ApprovalFlowBuilder<TServices, TKey> OnFinishing(Func<IApprovalContext<TServices, TKey>, Task> hook) {
         _declaration.OnFinishing = hook;
         return this;
      }

      /// <summary>Sets what runs after the completed request is saved.</summary>
      /// <param name="hook">What runs. Its failure does not corrupt data.</param>
      public ApprovalFlowBuilder<TServices, TKey> OnFinished(Func<IApprovalContext<TServices, TKey>, Task> hook) {
         _declaration.OnFinished = hook;
         return this;
      }

      /// <summary>Sets what runs inside the transaction when a step is rejected.</summary>
      /// <param name="hook">What runs. Throwing means the rejection is cancelled.</param>
      public ApprovalFlowBuilder<TServices, TKey> OnRejecting(Func<IApprovalContext<TServices, TKey>, Task> hook) {
         _declaration.OnRejecting = hook;
         return this;
      }

      /// <summary>Sets what runs after the rejection is saved.</summary>
      /// <param name="hook">What runs. Its failure does not corrupt data.</param>
      public ApprovalFlowBuilder<TServices, TKey> OnRejected(Func<IApprovalContext<TServices, TKey>, Task> hook) {
         _declaration.OnRejected = hook;
         return this;
      }

      /// <summary>
      /// Sets what runs inside the transaction when a request is withdrawn - including a request that has
      /// already completed in full. This is where the module revokes the status that was written, and where
      /// it may refuse the withdrawal by throwing if the document has been processed further.
      /// </summary>
      /// <param name="hook">
      /// What runs, receiving the stage of the request before it was withdrawn - from that the module knows
      /// whether the document's status was ever changed.
      /// </param>
      public ApprovalFlowBuilder<TServices, TKey> OnReinstating(
         Func<IApprovalContext<TServices, TKey>, ApprovalStage, Task> hook) {
         _declaration.OnReinstating = hook;
         return this;
      }
   }

   /// <summary>
   /// Where a module writes the steps of one level. Several steps in the same level run concurrently, and
   /// all must be approved before the next level starts.
   /// </summary>
   /// <typeparam name="TServices">Service of the module that owns the document.</typeparam>
   /// <typeparam name="TKey">Key record of the document.</typeparam>
   public class ApprovalLevelBuilder<TServices, TKey> where TKey : notnull
   {
      private readonly ApprovalLevelDeclaration<TServices, TKey> _declaration;
      private readonly string _docType;
      private readonly Func<string, ClaimAction> _claimFactory;

      internal ApprovalLevelBuilder(ApprovalLevelDeclaration<TServices, TKey> declaration, string docType,
         Func<string, ClaimAction> claimFactory) {
         _declaration = declaration;
         _docType = docType;
         _claimFactory = claimFactory;
      }

      /// <summary>
      /// Adds one step to this level.
      /// </summary>
      /// <param name="name">
      /// Name of the step, which is also the name of the claim required to decide it and the name printed on
      /// the PDF. Use a constant, not literal text: the name is stored with every request.
      /// </param>
      /// <param name="slot">
      /// Where the signature is drawn on the PDF. Required for document types that have a PDF.
      /// </param>
      /// <param name="signers">
      /// How to determine who the signers are, read from the document content at submission. Empty means
      /// this step is open to everyone holding its claim.
      /// </param>
      /// <param name="strict">
      /// <c>true</c> when only its own signer may sign, so holders of other claims cannot substitute for
      /// them.
      /// </param>
      /// <param name="distinctFrom">
      /// Name of the step whose signer must not be the same as this step's signer - a separation of duties
      /// checked at submission and once more at signing.
      /// </param>
      /// <param name="when">
      /// How to decide whether this step applies or is skipped, evaluated at submission. Empty means it
      /// always applies.
      /// </param>
      /// <param name="guard">
      /// How to decide whether this step may be decided now. Re-evaluated every time the screen is opened,
      /// so a block whose condition is met opens by itself.
      /// </param>
      /// <param name="input">Input asked for before this step can be decided.</param>
      /// <exception cref="ArgumentException">
      /// Thrown when the step name is empty or already used by another step in the same level.
      /// </exception>
      public ApprovalLevelBuilder<TServices, TKey> Step(string name, ApprovalSlot? slot = null,
         Func<IApprovalContext<TServices, TKey>, Task<IReadOnlyList<string>>>? signers = null,
         bool strict = false,
         string? distinctFrom = null,
         Func<IApprovalContext<TServices, TKey>, Task<bool>>? when = null,
         Func<IApprovalStepContext<TServices, TKey>, Task<ApprovalGuard>>? guard = null,
         IApprovalStepInput<TServices, TKey>? input = null) {
         if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException(
               $"A step of the approval flow for '{_docType}' has an empty name.", nameof(name));
         }

         if (_declaration.Steps.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))) {
            throw new ArgumentException(
               $"Approval flow for '{_docType}' already declares a step named '{name}' at level {_declaration.Level}.",
               nameof(name));
         }

         _declaration.Steps.Add(new ApprovalStepDeclaration<TServices, TKey> {
            Name = name,
            Claim = _claimFactory(name),
            Level = _declaration.Level,
            Order = _declaration.Steps.Count + 1,
            Slot = slot,
            Strict = strict,
            DistinctFrom = distinctFrom,
            Signers = signers,
            When = when,
            Guard = guard,
            Input = input
         });

         return this;
      }
   }

   /// <summary>
   /// Where a module writes the data change proposal flow: its approval claim, the entities that may be
   /// proposed for change, and the things that happen when a proposal is decided.
   /// </summary>
   /// <typeparam name="TServices">Service of the module that owns the data.</typeparam>
   public class ApprovalDataFlowBuilder<TServices>
      where TServices : ServicesBase, IServices
   {
      private readonly ApprovalDataFlowDeclaration<TServices> _declaration;

      internal ApprovalDataFlowBuilder(ApprovalDataFlowDeclaration<TServices> declaration) {
         _declaration = declaration;
      }

      /// <summary>
      /// Registers one entity that may be proposed for change, together with how to load its current values
      /// and how to apply its proposal.
      /// </summary>
      /// <typeparam name="TKey">Key record of this entity, each part marked with <c>KeyPart</c>.</typeparam>
      /// <param name="name">
      /// Name of the entity, written with its module name as a prefix so it does not collide across
      /// modules. The name is stored with every proposal, so use a constant.
      /// </param>
      /// <param name="load">
      /// Loads the column values of this entity as they are right now, or returns empty when the entity no
      /// longer exists. This is what the engine compares with the values recorded at submission.
      /// </param>
      /// <param name="apply">
      /// Applies the proposal, inside the decision transaction. The return value is the entity's key after
      /// it was applied - the newly formed key, for a new entity.
      /// </param>
      /// <param name="order">
      /// Order of application, for entities that must be applied after other entities. A smaller value is
      /// applied first.
      /// </param>
      /// <exception cref="ArgumentException">
      /// Thrown when the entity name is empty or already used by another entity in this flow.
      /// </exception>
      public ApprovalDataFlowBuilder<TServices> Entity<TKey>(string name,
         Func<IApprovalDataContext<TServices>, TKey, Task<IReadOnlyDictionary<string, string?>?>> load,
         Func<IApprovalDataContext<TServices>, ApprovalApplyRequest<TKey>, Task<TKey>> apply,
         int order = 0) where TKey : notnull {
         if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException(
               $"An entity of the data approval flow for '{_declaration.DocType}' has an empty name.", nameof(name));
         }

         ArgumentNullException.ThrowIfNull(load);
         ArgumentNullException.ThrowIfNull(apply);

         if (_declaration.Entities.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))) {
            throw new ArgumentException(
               $"Data approval flow for '{_declaration.DocType}' already declares an entity named '{name}'.",
               nameof(name));
         }

         _declaration.Entities.Add(new ApprovalEntityDeclaration<TServices, TKey> {
            Name = name,
            Order = order,
            Load = load,
            Apply = apply
         });

         return this;
      }

      /// <summary>
      /// States the module's own summary columns shown in the request list, and how to compute them.
      /// </summary>
      /// <param name="summary">How to compute them, as pairs of column name and value.</param>
      public ApprovalDataFlowBuilder<TServices> Summary(
         Func<IApprovalDataContext<TServices>, Task<IReadOnlyDictionary<string, string?>>> summary) {
         ArgumentNullException.ThrowIfNull(summary);
         _declaration.Summary = summary;
         return this;
      }

      /// <summary>Sets what runs after the submission is saved.</summary>
      /// <param name="hook">What runs. Its failure does not cancel the submission.</param>
      public ApprovalDataFlowBuilder<TServices> OnSubmitted(Func<IApprovalDataContext<TServices>, Task> hook) {
         _declaration.OnSubmitted = hook;
         return this;
      }

      /// <summary>
      /// Sets what runs inside the transaction, after all proposals have been applied and before the
      /// decision is saved.
      /// </summary>
      /// <param name="hook">What runs. Throwing means the whole application is cancelled.</param>
      public ApprovalDataFlowBuilder<TServices> OnFinishing(Func<IApprovalDataContext<TServices>, Task> hook) {
         _declaration.OnFinishing = hook;
         return this;
      }

      /// <summary>Sets what runs after the decision is saved.</summary>
      /// <param name="hook">What runs. Its failure does not corrupt data.</param>
      public ApprovalDataFlowBuilder<TServices> OnFinished(Func<IApprovalDataContext<TServices>, Task> hook) {
         _declaration.OnFinished = hook;
         return this;
      }

      /// <summary>Sets what runs inside the transaction when a proposal is rejected.</summary>
      /// <param name="hook">What runs. Throwing means the rejection is cancelled.</param>
      public ApprovalDataFlowBuilder<TServices> OnRejecting(Func<IApprovalDataContext<TServices>, Task> hook) {
         _declaration.OnRejecting = hook;
         return this;
      }

      /// <summary>Sets what runs after the rejection is saved.</summary>
      /// <param name="hook">What runs. Its failure does not corrupt data.</param>
      public ApprovalDataFlowBuilder<TServices> OnRejected(Func<IApprovalDataContext<TServices>, Task> hook) {
         _declaration.OnRejected = hook;
         return this;
      }
   }
}
