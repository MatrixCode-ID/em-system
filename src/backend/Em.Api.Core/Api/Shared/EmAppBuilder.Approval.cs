using Em.Api.Core;
using Em.Api.Core.Approval;
using Em.Api.Core.Hub;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Em.Api.Shared;
/// <summary>Explicit approval, storage and task-source registration.</summary>
public partial class EmAppBuilder {
   internal List<DataRegistration> ApprovalData { get; } = [];
   internal List<SystemRegistration> ApprovalSystems { get; } = [];
   internal string? BinaryStorageRootPath { get; private set; }
   /// <summary>Registers one Data root or child definition. Definitions may use scoped dependencies.</summary>
   public void AddDataApproval<T>() where T : class, IEmApproval {
      var type = typeof(T);
      var roots = type.GetInterfaces().Where(i => i.IsGenericType &&
         (i.GetGenericTypeDefinition() == typeof(IEmApprovalData<>) || i.GetGenericTypeDefinition() == typeof(IEmApprovalDataChild<,>))).ToArray();
      if (roots.Length != 1) throw new InvalidOperationException($"'{type}' must implement exactly one Data root or child interface.");
      var contract = roots[0]; var args = contract.GetGenericArguments();
      if (ApprovalData.Any(x => x.DefinitionType == type)) throw new InvalidOperationException($"Data definition '{type}' is already registered.");
      Services.TryAddScoped<T>();
      ApprovalData.Add(new DataRegistration(type, args[0], args.Length == 2 ? args[1] : null));
   }
   /// <summary>Registers a System definition without server hooks; Dynamic Signers will be rejected.</summary>
   public void AddSystemApproval<TDefinition>() where TDefinition : class, IEmApprovalSystem, new() =>
      RegisterSystem<TDefinition>(null);
   /// <summary>Registers a System definition and its typed, scoped server handler.</summary>
   public void AddSystemApproval<TDefinition, THandler>()
      where TDefinition : class, IEmApprovalSystem, new() where THandler : class {
      var contract = typeof(TDefinition).GetInterfaces().Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEmApprovalSystem<>));
      var adapterType = typeof(SystemHandlerAdapter<,,>).MakeGenericType(typeof(TDefinition), contract.GenericTypeArguments[0], typeof(THandler));
      Services.TryAddScoped<THandler>();
      RegisterSystem<TDefinition>((ISystemHandlerAdapter)Activator.CreateInstance(adapterType)!);
   }
   private void RegisterSystem<T>(ISystemHandlerAdapter? handler) where T : class, IEmApprovalSystem, new() {
      var name = ApprovalSubjectKey.NameOf(typeof(T));
      if (ApprovalSystems.Any(x => string.Equals(ApprovalSubjectKey.NameOf(x.DefinitionType), name, StringComparison.OrdinalIgnoreCase)))
         throw new InvalidOperationException($"System approval '{name}' is registered more than once.");
      ApprovalSystems.Add(new SystemRegistration(typeof(T), new T(), handler));
      AddClaimIfMissing(new ClaimAction { ModuleName = Defaults.ApprovalModuleName, Name = $"View {name}" });
   }
   /// <summary>Physical-document approval is reserved for a later implementation.</summary>
   public void AddDocumentApproval<T>() where T : IEmApprovalDocument =>
      throw new NotImplementedException("Document Approval is not implemented.");
   /// <summary>Enables local binary storage independently of approval.</summary>
   public void AddLocalBinaryStorage(string rootPath) {
      ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
      if (BinaryStorageRootPath is not null) throw new InvalidOperationException("Binary storage is already configured.");
      BinaryStorageRootPath = rootPath;
   }
   /// <summary>Registers one scoped source in the merged user task list.</summary>
   public void AddHubTaskSource<T>() where T : class, IHubTaskSource =>
      Services.TryAddEnumerable(ServiceDescriptor.Scoped<IHubTaskSource, T>());
   internal void AddClaimIfMissingForApproval() {
      AddClaimIfMissing(new ClaimAction { ModuleName = "Administrative Tools", Name = "Approval Signer Settings" });
      AddManagedBucket("approval-attachments", "Approval",
         "Attachments and supporting files of approval requests and comments. Written by the approval engine.");
   }
   private void AddClaimIfMissing(ClaimAction claim) {
      if (!ClaimActions.Any(x => string.Equals(x.Key, claim.Key, StringComparison.OrdinalIgnoreCase))) ClaimActions.Add(claim);
   }
}
