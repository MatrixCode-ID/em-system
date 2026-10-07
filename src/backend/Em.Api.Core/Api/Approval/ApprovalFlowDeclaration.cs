using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Approval flow of one document type, as declared by its owning module: which steps there are, who may
   /// decide, and what happens along the way.
   /// </summary>
   /// <remarks>
   /// The declaration is created once when the application is built and never changes while it runs. The
   /// engine reads it for the things that are the same for every document type; the parts only the module
   /// understands - loading the document, determining signers, applying changes - stay as delegates in the
   /// typed derivatives of this declaration.
   /// </remarks>
   public abstract class ApprovalFlowDeclaration
   {
      /// <summary>The document type whose flow is declared here.</summary>
      public required string DocType { get; init; }

      /// <summary>Name of the module that owns the document, which also owns the flow's claims.</summary>
      public required string ModuleName { get; init; }

      /// <summary>
      /// Claim that grants the right to view requests of this document type without taking part in deciding
      /// them - for readers who need to monitor without signing.
      /// </summary>
      public required ClaimAction ViewClaim { get; init; }

      /// <summary>Whether the proposed data rides on the request or already exists in the document.</summary>
      public abstract ApprovalKind Kind { get; }

      /// <summary>Type of the service of the module that owns the document.</summary>
      public abstract Type ServicesType { get; }

      private IReadOnlyList<Type>? _moduleDbContextTypes;

      /// <summary>
      /// Database contexts that the owning module's service asks for through its constructor - where the
      /// module's handlers write. These are the contexts the engine includes in the decision transaction, so
      /// the module does not need to name them again. The core context is not counted: it always takes part.
      /// </summary>
      internal IReadOnlyList<Type> ModuleDbContextTypes => _moduleDbContextTypes ??=
         [.. ServicesType.GetConstructors()
            .OrderByDescending(r => r.GetParameters().Length)
            .FirstOrDefault()?.GetParameters()
            .Select(r => r.ParameterType)
            .Where(r => typeof(Microsoft.EntityFrameworkCore.DbContext).IsAssignableFrom(r) && r != typeof(ApiCoreContext))
            .Distinct() ?? []];

      /// <summary>Type of the document key record, or empty when this type does not use a typed key.</summary>
      public virtual Type? KeyType => null;

      /// <summary>
      /// Every claim this flow uses, including <see cref="ViewClaim"/>. Used by the engine when checking who
      /// may view a request.
      /// </summary>
      public abstract IReadOnlyList<ClaimAction> Claims { get; }

      /// <summary>
      /// <c>true</c> when decisions on this document type may only be taken after the document is opened, so
      /// they cannot be approved en masse from the list. Meaningful only for document flows; data change
      /// proposal flows are always <c>false</c>.
      /// </summary>
      public bool RequireOpen { get; set; }

      /// <summary>
      /// <c>true</c> when the PDF gets an extra page containing a table of all steps with their
      /// signatures.
      /// </summary>
      public bool ApprovalSheet { get; set; }

      /// <summary>Whether a step asks for input before it can be decided.</summary>
      /// <param name="stepName">Name of the step.</param>
      public virtual bool StepRequiresInput(string stepName) => false;

      // The member below is how the engine reaches the delegates of a flow without knowing the
      // module's service type or key type: the typed subclass builds the typed context and calls the
      // delegate, and the engine only sees the result. Internal because only the engine has a scope to
      // hand in.

      /// <summary>
      /// Runs a step's blocking check in the owning module.
      /// </summary>
      /// <returns>The result of the check, or <c>null</c> when the step has no check.</returns>
      internal virtual Task<ApprovalGuard?> EvaluateGuardAsync(ApprovalRunScope scope, string stepName,
         string? signerId) => Task.FromResult<ApprovalGuard?>(null);

      // Submitting, advancing and finishing only make sense for a document flow, so the base class
      // refuses them; the document flow overrides every one. Data flows get their own bridge when
      // their submission is written.

      /// <summary>Whether this document type has a PDF that is frozen at submission.</summary>
      internal virtual bool HasPdf => false;

      /// <summary>
      /// Builds the step plan for a submission: every step and whether it applies to this document. The
      /// signers are not determined here yet.
      /// </summary>
      internal virtual Task<IReadOnlyList<ApprovalPlannedStep>> PlanStepsAsync(ApprovalRunScope scope) =>
         throw NotADocumentFlow();

      /// <summary>
      /// Determines the signers of a step from the content of its document.
      /// </summary>
      /// <returns>A list of user ids, or <c>null</c> when the step does not fix its signers.</returns>
      internal virtual Task<IReadOnlyList<string>?> ResolveSignersAsync(ApprovalRunScope scope, string stepName) =>
         throw NotADocumentFlow();

      /// <summary>Computes the module's summary columns for a submission.</summary>
      internal virtual Task<IReadOnlyDictionary<string, string?>?> SummaryAsync(ApprovalRunScope scope) =>
         throw NotADocumentFlow();

      /// <summary>Fetches the document's PDF, or <c>null</c> when this type has no PDF.</summary>
      internal virtual Task<Stream?> OpenPdfAsync(ApprovalRunScope scope) => throw NotADocumentFlow();

      /// <summary>
      /// Shifts the positions of the plan boxes to where they actually are in this document's base PDF, when
      /// the module states that its layout changes with the document content. Without that statement the
      /// positions are left as declared.
      /// </summary>
      internal virtual Task LocateSlotsAsync(ApprovalRunScope scope, IReadOnlyList<ApprovalPlannedStep> plan, Stream pdf) =>
         throw NotADocumentFlow();

      /// <summary>Runs the hook after the submission is saved.</summary>
      internal virtual Task RunSubmittedAsync(ApprovalRunScope scope) => throw NotADocumentFlow();

      /// <summary>Runs the level completion hook, inside the transaction.</summary>
      internal virtual Task RunLevelCompletedAsync(ApprovalRunScope scope, int level) => throw NotADocumentFlow();

      /// <summary>Runs the request completion hook, inside the transaction.</summary>
      internal virtual Task RunFinishingAsync(ApprovalRunScope scope) => throw NotADocumentFlow();

      /// <summary>Runs the request completion hook, after the transaction is saved.</summary>
      internal virtual Task RunFinishedAsync(ApprovalRunScope scope) => throw NotADocumentFlow();

      /// <summary>
      /// The rule of every step, used when that step is signed: its claim, whether only its own signer may
      /// sign, and which step must have a different signer.
      /// </summary>
      internal virtual IReadOnlyList<ApprovalStepRule> StepRules => throw NotADocumentFlow();

      /// <summary>
      /// Validates a step's input in the owning module, then reads the values of its boxes.
      /// </summary>
      /// <returns>The value of each input box, or <c>null</c> when the step asks for no input.</returns>
      internal virtual Task<IReadOnlyList<ApprovalInputValue>?> ValidateInputAsync(ApprovalRunScope scope,
         string stepName, string? signerId, string? payloadJson) => throw NotADocumentFlow();

      /// <summary>Writes the effect of a step's input to its document, inside the transaction.</summary>
      internal virtual Task RunInputSignedAsync(ApprovalRunScope scope, string stepName, string? signerId,
         string? payloadJson) => throw NotADocumentFlow();

      /// <summary>Runs the decision check hook, before the decision is written.</summary>
      internal virtual Task RunSigningAsync(ApprovalRunScope scope, string stepName, string? signerId,
         ApprovalDecision decision) => throw NotADocumentFlow();

      /// <summary>Runs the decision effect hook, inside the same transaction as the decision.</summary>
      internal virtual Task RunSignedAsync(ApprovalRunScope scope, string stepName, string? signerId,
         ApprovalDecision decision) => throw NotADocumentFlow();

      /// <summary>Runs the request rejection hook, inside the transaction.</summary>
      internal virtual Task RunRejectingAsync(ApprovalRunScope scope) => throw NotADocumentFlow();

      /// <summary>Runs the request rejection hook, after the transaction is saved.</summary>
      internal virtual Task RunRejectedAsync(ApprovalRunScope scope) => throw NotADocumentFlow();

      /// <summary>
      /// Runs the request withdrawal hook, inside the transaction. <paramref name="stage"/> is the stage of
      /// the request before it was withdrawn: still waiting, or already completed in full.
      /// </summary>
      internal virtual Task RunReinstatingAsync(ApprovalRunScope scope, ApprovalStage stage) =>
         throw NotADocumentFlow();

      private InvalidOperationException NotADocumentFlow() =>
         new($"Approval flow for '{DocType}' is not a document flow, so it has no steps to run.");
   }

   /// <summary>
   /// What the engine needs to hand a request to its module's handler: who is running the engine (the
   /// source of the request info), the service provider of this call, and the request being handled.
   /// </summary>
   /// <param name="Engine">The running engine service.</param>
   /// <param name="Provider">The service provider of this call.</param>
   /// <param name="Request">The request row being handled.</param>
   /// <param name="Items">
   /// The proposed changes of the request being handled, for data change proposal flows; empty for
   /// document flows. Filled by the engine before it hands over to the module, so the module's hook can
   /// see what was proposed.
   /// </param>
   internal sealed record ApprovalRunScope(ServicesBase Engine, IServiceProvider Provider,
      ta_ApprovalRequest Request, IReadOnlyList<ApprovalDataItem>? Items = null);

   /// <summary>
   /// Rule of a step that the engine checks when the step is signed.
   /// </summary>
   /// <param name="Name">Name of the step.</param>
   /// <param name="Claim">Claim that must be held to decide it.</param>
   /// <param name="Strict">Whether a substitute is refused.</param>
   /// <param name="DistinctFrom">Step whose signer must be different, or <c>null</c>.</param>
   internal sealed record ApprovalStepRule(string Name, ClaimAction Claim, bool Strict, string? DistinctFrom);

   /// <summary>
   /// Document flow: several levels that are walked in order, each containing one step or several steps
   /// that run concurrently.
   /// </summary>
   /// <typeparam name="TServices">Service of the module that owns the document.</typeparam>
   /// <typeparam name="TKey">Key record of the document.</typeparam>
   public partial class ApprovalDocumentFlowDeclaration<TServices, TKey> : ApprovalFlowDeclaration
      where TServices : ServicesBase, IServices
      where TKey : notnull
   {
      /// <inheritdoc />
      public override ApprovalKind Kind => ApprovalKind.Document;

      /// <inheritdoc />
      public override Type ServicesType => typeof(TServices);

      /// <inheritdoc />
      public override Type KeyType => typeof(TKey);

      /// <summary>The levels of the flow, in order.</summary>
      public List<ApprovalLevelDeclaration<TServices, TKey>> Levels { get; } = [];

      /// <summary>All steps of the flow, ordered by level and then by order within the level.</summary>
      public IEnumerable<ApprovalStepDeclaration<TServices, TKey>> Steps =>
         Levels.OrderBy(r => r.Level).SelectMany(r => r.Steps.OrderBy(q => q.Order));

      /// <summary>
      /// How to fetch the document's PDF, or empty when this document type has no PDF. Called at submission,
      /// and the result is frozen as that request's base PDF.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Task<Stream>>? Pdf { get; set; }

      /// <summary>
      /// How to find the positions of the boxes in the PDF of a document whose layout changes with its
      /// content. Called at submission with the freshly created base PDF, before the box positions are
      /// frozen.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Stream, Task<Func<ApprovalSlot, ApprovalSlot?>>>? PdfLayout { get; set; }

      /// <summary>
      /// How to compute the module's own summary columns, snapshotted at submission. Used by the request list
      /// to filter, sort, and show information only the module understands.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Task<IReadOnlyDictionary<string, string?>>>? Summary { get; set; }

      /// <summary>Runs after the submission is saved. Its failure does not cancel the submission.</summary>
      public Func<IApprovalContext<TServices, TKey>, Task>? OnSubmitted { get; set; }

      /// <summary>
      /// Checks a decision before it is written, inside its transaction. This is where the per-step input is
      /// validated again on the server. Its failure fails that decision.
      /// </summary>
      public Func<IApprovalStepContext<TServices, TKey>, ApprovalDecision, Task>? OnSigning { get; set; }

      /// <summary>
      /// Writes the effect of a signature to its document, inside the same transaction as the signature. Its
      /// failure cancels the signature too.
      /// </summary>
      public Func<IApprovalStepContext<TServices, TKey>, ApprovalDecision, Task>? OnSigned { get; set; }

      /// <summary>
      /// Runs inside the transaction, when the last step is approved and the request is about to complete. Its
      /// failure restores everything to how it was before that decision was taken.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Task>? OnFinishing { get; set; }

      /// <summary>Runs after the request is saved as completed. Its failure does not corrupt data.</summary>
      public Func<IApprovalContext<TServices, TKey>, Task>? OnFinished { get; set; }

      /// <summary>
      /// Runs inside the transaction, when a step is rejected and the request is about to stop. Its failure
      /// restores everything to how it was before the rejection.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Task>? OnRejecting { get; set; }

      /// <summary>Runs after the rejection is saved. Its failure does not corrupt data.</summary>
      public Func<IApprovalContext<TServices, TKey>, Task>? OnRejected { get; set; }

      /// <summary>
      /// Runs inside the transaction when a request is withdrawn. This is where the module revokes the status
      /// that was written because of the request, and where it may refuse the withdrawal - by throwing - when
      /// the document has been processed further.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, ApprovalStage, Task>? OnReinstating { get; set; }

      /// <inheritdoc />
      public override IReadOnlyList<ClaimAction> Claims =>
         [ViewClaim, .. Steps.Select(r => r.Claim)];

      /// <inheritdoc />
      public override bool StepRequiresInput(string stepName) =>
         Steps.Any(r => string.Equals(r.Name, stepName, StringComparison.OrdinalIgnoreCase) && r.Input is not null);

      internal override async Task<ApprovalGuard?> EvaluateGuardAsync(ApprovalRunScope scope, string stepName,
         string? signerId) {
         var step = Steps.FirstOrDefault(r => string.Equals(r.Name, stepName, StringComparison.OrdinalIgnoreCase));
         if (step?.Guard is null) return null;

         var context = new ApprovalStepContext<TServices, TKey> {
            Services = ApprovalModuleService.Resolve<TServices>(scope.Engine, scope.Provider),
            DocKey = ApprovalKey.FromCanonical<TKey>(scope.Request.cApprovalRequestDocKey),
            DocVersion = scope.Request.cApprovalRequestDocVersion,
            ApprovalRequestId = scope.Request.cApprovalRequestId,
            RequesterId = scope.Request.cApprovalRequestRequesterId,
            StepName = step.Name,
            SignerId = signerId
         };

         return await step.Guard(context);
      }
   }

   /// <summary>
   /// One level of a document flow: the steps that run concurrently, and what happens as soon as all of
   /// them are approved.
   /// </summary>
   /// <typeparam name="TServices">Service of the module that owns the document.</typeparam>
   /// <typeparam name="TKey">Key record of the document.</typeparam>
   public class ApprovalLevelDeclaration<TServices, TKey> where TKey : notnull
   {
      /// <summary>Number of the level. Levels are walked from the smallest.</summary>
      public required int Level { get; init; }

      /// <summary>The steps in this level. All must be approved before the next level starts.</summary>
      public List<ApprovalStepDeclaration<TServices, TKey>> Steps { get; } = [];

      /// <summary>
      /// Runs inside the transaction as soon as all steps of this level are approved. Used for milestones
      /// in the middle of the flow - a document status that changes before the last step. Its failure
      /// restores the decision that triggered it.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Task>? OnCompleted { get; set; }
   }

   /// <summary>One step of a document flow, as declared by the module.</summary>
   /// <typeparam name="TServices">Service of the module that owns the document.</typeparam>
   /// <typeparam name="TKey">Key record of the document.</typeparam>
   public class ApprovalStepDeclaration<TServices, TKey> where TKey : notnull
   {
      /// <summary>Name of the step. Used on screen, in the PDF, and as the name of its claim.</summary>
      public required string Name { get; init; }

      /// <summary>Claim that must be held to decide this step.</summary>
      public required ClaimAction Claim { get; init; }

      /// <summary>Level this step belongs to.</summary>
      public required int Level { get; init; }

      /// <summary>Order of this step within its level, for a stable display.</summary>
      public required int Order { get; init; }

      /// <summary>
      /// Where the signature is drawn on the PDF, or empty for document types that have no PDF.
      /// </summary>
      public ApprovalSlot? Slot { get; init; }

      /// <summary>
      /// <c>true</c> when this step may only be signed by its own signer, so a substitute is
      /// refused.
      /// </summary>
      public bool Strict { get; init; }

      /// <summary>
      /// Name of the step whose signer must not be the same as this step's signer, or empty when there is no
      /// such condition.
      /// </summary>
      public string? DistinctFrom { get; init; }

      /// <summary>
      /// How to determine who the signers of this step are, read from the document content at submission.
      /// Empty means this step is open to everyone holding its claim.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Task<IReadOnlyList<string>>>? Signers { get; init; }

      /// <summary>
      /// How to decide whether this step applies or is skipped, evaluated at submission. Empty means this
      /// step always applies.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Task<bool>>? When { get; init; }

      /// <summary>
      /// How to decide whether this step may be decided now. Re-evaluated every time the screen is opened
      /// and once more on the server before the decision is written, so it reads the state at that moment -
      /// not the state at submission.
      /// </summary>
      public Func<IApprovalStepContext<TServices, TKey>, Task<ApprovalGuard>>? Guard { get; init; }

      /// <summary>Input this step asks for before it can be decided, or empty when there is none.</summary>
      public IApprovalStepInput<TServices, TKey>? Input { get; init; }
   }

   /// <summary>
   /// Data change proposal flow: one approval claim, and the list of entities that may be proposed for
   /// change together with how to load and apply them.
   /// </summary>
   /// <typeparam name="TServices">Service of the module that owns the data.</typeparam>
   /// <remarks>
   /// This kind has no levels and no PDF: a request is approved or rejected as a whole by a holder of
   /// one claim, and what the approver sees is a list of changes from old to new.
   /// </remarks>
   public partial class ApprovalDataFlowDeclaration<TServices> : ApprovalFlowDeclaration, IApprovalDataFlow
      where TServices : ServicesBase, IServices
   {
      /// <inheritdoc />
      public override ApprovalKind Kind => ApprovalKind.Data;

      /// <inheritdoc />
      public override Type ServicesType => typeof(TServices);

      /// <summary>
      /// Claim that grants the right to approve change proposals of this document type. Its holder also
      /// saves changes directly without waiting for anyone's approval.
      /// </summary>
      public required ClaimAction ApproveClaim { get; init; }

      /// <summary>Entities that may be proposed for change through this flow.</summary>
      public List<ApprovalEntityDeclaration> Entities { get; } = [];

      /// <summary>
      /// How to compute the module's own summary columns, snapshotted at submission.
      /// </summary>
      public Func<IApprovalDataContext<TServices>, Task<IReadOnlyDictionary<string, string?>>>? Summary { get; set; }

      /// <summary>Runs after the submission is saved. Its failure does not cancel the submission.</summary>
      public Func<IApprovalDataContext<TServices>, Task>? OnSubmitted { get; set; }

      /// <summary>
      /// Runs inside the transaction, after all proposals have been applied and before the decision is
      /// saved. Its failure restores everything to how it was before that decision.
      /// </summary>
      public Func<IApprovalDataContext<TServices>, Task>? OnFinishing { get; set; }

      /// <summary>Runs after the decision is saved. Its failure does not corrupt data.</summary>
      public Func<IApprovalDataContext<TServices>, Task>? OnFinished { get; set; }

      /// <summary>Runs inside the transaction when the proposal is rejected.</summary>
      public Func<IApprovalDataContext<TServices>, Task>? OnRejecting { get; set; }

      /// <summary>Runs after the rejection is saved.</summary>
      public Func<IApprovalDataContext<TServices>, Task>? OnRejected { get; set; }

      /// <inheritdoc />
      public override IReadOnlyList<ClaimAction> Claims => [ViewClaim, ApproveClaim];
   }

   /// <summary>One entity that may be proposed for change, seen without its key type.</summary>
   public abstract class ApprovalEntityDeclaration
   {
      /// <summary>
      /// Name of the entity, written with its module name as a prefix so it does not collide across
      /// modules.
      /// </summary>
      public required string Name { get; init; }

      /// <summary>Order of application, for entities that must be applied after other entities.</summary>
      public int Order { get; init; }

      /// <summary>Type of this entity's key record.</summary>
      public abstract Type KeyType { get; }

      // The two members below are how the engine reaches the handlers of an entity without knowing the
      // service type or the key type of the module: the typed subclass translates the key and builds the
      // typed context, and the engine only sees plain values. Internal because only the engine has a
      // scope to hand in.

      /// <summary>Loads the current column values of this entity, or <c>null</c> when the entity no longer exists.</summary>
      internal abstract Task<IReadOnlyDictionary<string, string?>?> LoadAsync(ApprovalRunScope scope, string canonicalKey);

      /// <summary>Applies the proposal to this entity, then returns its canonical key after it was applied.</summary>
      internal abstract Task<string> ApplyAsync(ApprovalRunScope scope, string canonicalKey,
         ApprovalItemOperation operation, IReadOnlyList<ApprovalDataField> fields);
   }

   /// <summary>One entity that may be proposed for change, together with how to load and apply it.</summary>
   /// <typeparam name="TServices">Service of the module that owns the data.</typeparam>
   /// <typeparam name="TKey">Key record of this entity.</typeparam>
   /// <remarks>
   /// Column values are handed over as text, not as typed rows, on purpose: the engine only compares the
   /// old value, the current value, and the proposed value, and it can do that without knowing the shape
   /// of the table at all - including legacy tables whose key is a composite of several columns.
   /// Translating to the real types stays in the module's handler.
   /// </remarks>
   public class ApprovalEntityDeclaration<TServices, TKey> : ApprovalEntityDeclaration
      where TServices : ServicesBase, IServices
      where TKey : notnull
   {
      /// <inheritdoc />
      public override Type KeyType => typeof(TKey);

      /// <summary>
      /// Loads the column values of this entity as they are right now, or empty when the entity no longer
      /// exists. Called when the decision is taken, to compare with the values recorded at submission.
      /// </summary>
      public Func<IApprovalDataContext<TServices>, TKey, Task<IReadOnlyDictionary<string, string?>?>>? Load { get; init; }

      /// <summary>
      /// Applies the proposal to this entity, inside the decision transaction. The return value is the
      /// entity's key after it was applied - the same as the one requested for a change, and the newly
      /// formed key for a new entity.
      /// </summary>
      public Func<IApprovalDataContext<TServices>, ApprovalApplyRequest<TKey>, Task<TKey>>? Apply { get; init; }

      internal override async Task<IReadOnlyDictionary<string, string?>?> LoadAsync(ApprovalRunScope scope,
         string canonicalKey) {
         if (Load is null) {
            throw new InvalidOperationException($"Entity '{Name}' declares no way to load its current values.");
         }

         return await Load(ApprovalDataContext<TServices>.From(scope), ApprovalKey.FromCanonical<TKey>(canonicalKey));
      }

      internal override async Task<string> ApplyAsync(ApprovalRunScope scope, string canonicalKey,
         ApprovalItemOperation operation, IReadOnlyList<ApprovalDataField> fields) {
         if (Apply is null) {
            throw new InvalidOperationException($"Entity '{Name}' declares no way to apply a proposal.");
         }

         var applied = await Apply(ApprovalDataContext<TServices>.From(scope),
            new ApprovalApplyRequest<TKey>(ApprovalKey.FromCanonical<TKey>(canonicalKey), operation, fields));
         return ApprovalKey.ToCanonical(applied);
      }
   }

   /// <summary>Request to apply a proposal to one entity.</summary>
   /// <typeparam name="TKey">Key record of the entity.</typeparam>
   /// <param name="Key">Key of the entity being applied.</param>
   /// <param name="Operation">What is proposed for that entity.</param>
   /// <param name="Fields">
   /// The columns proposed to change with their values. Empty for deletion and reactivation.
   /// </param>
   public record ApprovalApplyRequest<TKey>(TKey Key, ApprovalItemOperation Operation,
      IReadOnlyList<ApprovalDataField> Fields) where TKey : notnull;

   /// <summary>
   /// Info available when the engine hands over to the module in a data change proposal flow.
   /// </summary>
   /// <typeparam name="TServices">Service of the module that owns the data.</typeparam>
   public interface IApprovalDataContext<out TServices>
   {
      /// <summary>The service of the module that owns the data, ready to use.</summary>
      TServices Services { get; }

      /// <summary>The document type being processed.</summary>
      string DocType { get; }

      /// <summary>The key of the document being processed, in its canonical form.</summary>
      string DocKey { get; }

      /// <summary>Id of the request being processed.</summary>
      string ApprovalRequestId { get; }

      /// <summary>The submitter of this request.</summary>
      string RequesterId { get; }


      /// <summary>
      /// The change proposals carried by this request, per entity. After the proposals are applied, the key
      /// of a new entity already holds the actual key.
      /// </summary>
      IReadOnlyList<ApprovalDataItem> Items { get; }
   }

   /// <summary>
   /// Every approval flow registered in the application, frozen since the application was built.
   /// </summary>
   /// <remarks>
   /// The only place the engine looks to find out what a document type declares. A document type that is
   /// not here has no flow, and requests for it are refused.
   /// </remarks>
   public class ApprovalRegistry
   {
      private readonly Dictionary<string, ApprovalFlowDeclaration> _byDocType;

      internal ApprovalRegistry(IEnumerable<ApprovalFlowDeclaration> flows) {
         _byDocType = flows.ToDictionary(r => r.DocType, StringComparer.OrdinalIgnoreCase);
      }

      /// <summary>Every registered flow.</summary>
      public IReadOnlyCollection<ApprovalFlowDeclaration> Flows => _byDocType.Values;

      /// <summary>Document types that have an approval flow.</summary>
      public IReadOnlyCollection<string> DocTypes => _byDocType.Keys;

      /// <summary>The flow of a document type, or empty when that type has no flow.</summary>
      /// <param name="docType">The document type to look up.</param>
      public ApprovalFlowDeclaration? Find(string docType) =>
         _byDocType.GetValueOrDefault(docType);

      /// <summary>The flow of a document type.</summary>
      /// <param name="docType">The document type to look up.</param>
      /// <exception cref="ActionException">
      /// Thrown when that document type has no approval flow - usually because its module is not installed,
      /// or the document type is misspelled.
      /// </exception>
      public ApprovalFlowDeclaration Get(string docType) =>
         Find(docType) ?? throw new ActionException(
            $"Document type '{docType}' has no approval flow registered.", 404);
   }
}
