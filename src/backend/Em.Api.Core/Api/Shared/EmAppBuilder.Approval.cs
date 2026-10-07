using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Em.Api.Core;
using Em.Api.Core.Approval;
using Em.Api.Core.Hub;
using Em.Api.Core.Storage;
using Em.Shared;

namespace Em.Api.Shared
{
   /// <summary>Approval registration for the application builder.</summary>
   public partial class EmAppBuilder
   {
      internal List<ApprovalFlowDeclaration> ApprovalFlows { get; } = [];

      // null = no binary storage configured. Kept as written in the application's startup code;
      // turning it into an absolute path waits for BuildApp, where the content root is known.
      internal string? BinaryStorageRootPath { get; private set; }

      /// <summary>
      /// Name of the claim every document type gives to its readers - the right to view requests without
      /// taking part in deciding them. The document type name is included, so a module that owns several
      /// document types still has one reader claim per type.
      /// </summary>
      /// <param name="docType">The document type.</param>
      public static string ApprovalViewClaimName(string docType) => $"View {docType}";

      /// <summary>
      /// Registers the approval flow of a document type: the document already exists, and requests become
      /// the gate for its status. Used by transaction documents that need to be signed by several parties.
      /// </summary>
      /// <typeparam name="TServices">
      /// Service of the module that owns the document - its implementation class, marked <c>[Module]</c>,
      /// because the module name of the flow's claims is taken from there.
      /// </typeparam>
      /// <typeparam name="TKey">
      /// Key record of the document, each part marked with <c>KeyPart</c>. The module's handlers receive this
      /// record as-is; the engine translates it to the stored form and back.
      /// </typeparam>
      /// <param name="docType">
      /// The document type, according to the application's list of document types. A type not registered
      /// there is refused by the database when the first request is created.
      /// </param>
      /// <param name="flow">Callback that writes the flow.</param>
      /// <remarks>
      /// The claim of each step and the reader claim are registered automatically here, on the module
      /// <typeparamref name="TServices"/>, so their names are uniform and need not be registered twice.
      /// The <typeparamref name="TServices"/> type itself is also registered in DI, because the engine must
      /// hand it to the module's handlers when this flow runs.
      /// </remarks>
      /// <exception cref="ArgumentException">
      /// Thrown when <paramref name="docType"/> is empty or already has a flow.
      /// </exception>
      public void AddDocumentApproval<TServices, TKey>(string docType,
         Action<ApprovalFlowBuilder<TServices, TKey>> flow)
         where TServices : ServicesBase, IServices
         where TKey : notnull {
         ArgumentNullException.ThrowIfNull(flow);
         EnsureDocTypeIsFree(docType);

         var declaration = new ApprovalDocumentFlowDeclaration<TServices, TKey> {
            DocType = docType,
            ModuleName = ModuleAttribute.ResolveName(typeof(TServices)),
            ViewClaim = ClaimAction.Create<TServices>(ApprovalViewClaimName(docType))
         };

         flow(new ApprovalFlowBuilder<TServices, TKey>(declaration, ClaimAction.Create<TServices>));

         if (declaration.Levels.Count == 0) {
            throw new ArgumentException(
               $"Approval flow for '{docType}' declares no level, so nothing could ever be decided.",
               nameof(flow));
         }

         // A step without a place to print its signature on a document that has a PDF would mean a
         // signature nobody can see, and that is a mistake of the declaration - so it is refused here,
         // while the application is being built, rather than at the first submit.
         if (declaration.Pdf is not null) {
            var slotless = declaration.Steps.FirstOrDefault(r => r.Slot is null);
            if (slotless is not null) {
               throw new ArgumentException(
                  $"Step '{slotless.Name}' of the approval flow for '{docType}' has no slot, but the flow has a PDF. " +
                  "Give the step a slot, or turn the approval sheet on so its signature has a place to go.",
                  nameof(flow));
            }
         }

         // The requester signs the first step automatically, and that signature has no payload to carry
         // input, so a step that asks for input cannot be the one that comes first.
         if (declaration.Steps.FirstOrDefault() is { Input: not null } inputFirst) {
            throw new ArgumentException(
               $"Step '{inputFirst.Name}' of the approval flow for '{docType}' comes first and is signed by the requester at submission, so it cannot ask for input.",
               nameof(flow));
         }

         // Both key translation and the flow's own validity are checked now, so a key type that
         // declares no part fails while the application is being built.
         ApprovalKey.GetPartNames(typeof(TKey));
         EnsureDistinctFromNamesExist(declaration);

         Register<TServices>(declaration);
      }

      /// <summary>
      /// Registers a data change proposal flow: the proposed data stays in the request and is only applied
      /// after approval. Used for master data changes, where the real table must not change while a proposal
      /// is still waiting.
      /// </summary>
      /// <typeparam name="TServices">
      /// Service of the module that owns the data - its implementation class, marked <c>[Module]</c>.
      /// </typeparam>
      /// <param name="docType">The document type, according to the application's list of document types.</param>
      /// <param name="approveClaim">
      /// Name of the claim that grants the right to approve proposals of this type, written without its
      /// module name. Its holder also saves their own changes without waiting for anyone's approval.
      /// </param>
      /// <param name="flow">Callback that writes the entities and the hooks.</param>
      /// <exception cref="ArgumentException">
      /// Thrown when <paramref name="docType"/> is empty or already has a flow, or when the flow does not
      /// register any entity.
      /// </exception>
      public void AddDataApproval<TServices>(string docType, string approveClaim,
         Action<ApprovalDataFlowBuilder<TServices>> flow)
         where TServices : ServicesBase, IServices {
         ArgumentNullException.ThrowIfNull(flow);
         EnsureDocTypeIsFree(docType);

         var declaration = new ApprovalDataFlowDeclaration<TServices> {
            DocType = docType,
            ModuleName = ModuleAttribute.ResolveName(typeof(TServices)),
            ViewClaim = ClaimAction.Create<TServices>(ApprovalViewClaimName(docType)),
            ApproveClaim = ClaimAction.Create<TServices>(approveClaim)
         };

         flow(new ApprovalDataFlowBuilder<TServices>(declaration));

         if (declaration.Entities.Count == 0) {
            throw new ArgumentException(
               $"Data approval flow for '{docType}' declares no entity, so no change could ever be proposed.",
               nameof(flow));
         }

         Register<TServices>(declaration);
      }

      /// <summary>
      /// Turns on storage of file content in a folder on the server machine. There is no public address to
      /// the content: whatever fetches it is the consumer's action, which first checks the caller's rights.
      /// </summary>
      /// <param name="rootPath">
      /// Folder where the content is stored. An absolute path is used as-is; a relative path (including
      /// <c>./...</c>) is resolved from the application content folder. The folder is created automatically
      /// at startup if it does not exist.
      /// </param>
      /// <exception cref="ArgumentException">Thrown when <paramref name="rootPath"/> is empty.</exception>
      /// <exception cref="InvalidOperationException">
      /// Thrown when file storage was already turned on - one application has only one.
      /// </exception>
      public void AddLocalBinaryStorage(string rootPath) {
         if (string.IsNullOrWhiteSpace(rootPath)) {
            throw new ArgumentException("The binary storage root path must not be empty.", nameof(rootPath));
         }

         if (BinaryStorageRootPath is not null) {
            throw new InvalidOperationException(
               $"Binary storage is already enabled for '{BinaryStorageRootPath}'; " +
               $"'{nameof(AddLocalBinaryStorage)}' may only be called once.");
         }

         BinaryStorageRootPath = rootPath;
      }

      /// <summary>
      /// Registers one source of the active user's task list. The task list in the application title bar is
      /// the union of all registered sources, so a new kind of task is added here - not by changing the list.
      /// </summary>
      /// <typeparam name="T">The source.</typeparam>
      /// <remarks>
      /// Safe to call repeatedly for the same type: what is registered stays one.
      /// </remarks>
      public void AddHubTaskSource<T>() where T : class, IHubTaskSource {
         Services.TryAddEnumerable(ServiceDescriptor.Scoped<IHubTaskSource, T>());
      }

      private void Register<TServices>(ApprovalFlowDeclaration declaration)
         where TServices : ServicesBase, IServices {
         // The engine hands a module its own service when the flow runs, so the implementation type
         // has to be resolvable - the interface registration that AddService makes is not enough,
         // because the declaration names the implementation.
         Services.TryAddScoped<TServices>();

         foreach (var claim in declaration.Claims) {
            AddClaimIfMissing(claim);
         }

         ApprovalFlows.Add(declaration);
      }

      private void EnsureDocTypeIsFree(string docType) {
         if (string.IsNullOrWhiteSpace(docType)) {
            throw new ArgumentException("A document type must not be empty.", nameof(docType));
         }

         if (ApprovalFlows.Any(r => string.Equals(r.DocType, docType, StringComparison.OrdinalIgnoreCase))) {
            throw new ArgumentException(
               $"Document type '{docType}' already has an approval flow. One document type has one flow.",
               nameof(docType));
         }
      }

      private static void EnsureDistinctFromNamesExist<TServices, TKey>(
         ApprovalDocumentFlowDeclaration<TServices, TKey> declaration)
         where TServices : ServicesBase, IServices
         where TKey : notnull {
         var names = declaration.Steps.Select(r => r.Name).ToArray();

         foreach (var step in declaration.Steps) {
            if (step.DistinctFrom is null) continue;

            if (!names.Contains(step.DistinctFrom, StringComparer.OrdinalIgnoreCase)) {
               throw new ArgumentException(
                  $"Step '{step.Name}' of the approval flow for '{declaration.DocType}' must differ from " +
                  $"'{step.DistinctFrom}', but that flow has no step by that name.");
            }
         }
      }

      // Registered claims of an approval flow come from the declaration, not from a module author
      // typing them twice, so a name that is already there is the same claim and not a mistake -
      // which is exactly the case when one module owns two document types that share a step name.
      private void AddClaimIfMissing(ClaimAction claim) {
         if (ClaimActions.Any(r => string.Equals(r.Key, claim.Key, StringComparison.OrdinalIgnoreCase))) {
            return;
         }

         ClaimActions.Add(claim);
      }
   }
}
