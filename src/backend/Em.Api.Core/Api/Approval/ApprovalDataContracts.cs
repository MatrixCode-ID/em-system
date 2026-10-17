using Em.Api.Core.Models;
using Microsoft.EntityFrameworkCore;
namespace Em.Api.Core.Approval;
/// <summary>Deletion policy for a declared table.</summary>
public enum ApprovalDeleteMode {
   /// <summary>Deletion is not supported.</summary>
   None,
   /// <summary>Remove the row.</summary>
   Hard,
   /// <summary>Change the declared state column.</summary>
   Soft
}
/// <summary>Database and caller participating in one Data operation.</summary>
public interface IEmApprovalDataContext {
   /// <summary>The enlisted context that owns the entity.</summary>
   DbContext DbContext { get; }
   /// <summary>Proven caller.</summary>
   ActionRequest Caller { get; }
   /// <summary>Operation being validated or applied.</summary>
   ApprovalItemOperation Operation { get; }
}
/// <summary>Structural definition and optional hooks for one table.</summary>
public interface IEmApprovalDataTable<TEntity> : IEmApproval where TEntity : class {
   /// <summary>All primary key properties, in matching order.</summary>
   string[] KeyColumns { get; }
   /// <summary>Properties compared between original and modified objects.</summary>
   string[] TrackedColumns { get; }
   /// <summary>UI hints; these do not restrict server submissions.</summary>
   string[] ApproverOnlyColumns => [];
   /// <summary>Deletion policy.</summary>
   ApprovalDeleteMode DeleteMode => ApprovalDeleteMode.None;
   /// <summary>Soft-delete state property.</summary>
   string? DeleteStateColumn => null;
   /// <summary>Optional nullable deletion timestamp.</summary>
   string? DeleteStampColumn => null;
   /// <summary>Required deletion value for non-boolean state columns.</summary>
   object? DeletedStateValue => null;
   /// <summary>Required active value for non-boolean state columns.</summary>
   object? ActiveStateValue => null;
   /// <summary>Assigns keys in the apply transaction before insertion.</summary>
   Task CreateKeyAsync(TEntity entity, IEmApprovalDataContext context, CancellationToken ct = default) => Task.CompletedTask;
   /// <summary>Optional module validation, called on submission and application.</summary>
   Task ValidateAsync(TEntity entity, IEmApprovalDataContext context, CancellationToken ct = default) => Task.CompletedTask;
}
/// <summary>Named root of an approval tree; access checks belong only to this definition.</summary>
public interface IEmApprovalData<TEntity> : IEmApprovalDataTable<TEntity> where TEntity : class {
   /// <summary>Display title; empty uses the metadata name.</summary>
   string Title => string.Empty;
   /// <summary>Whether this caller may view requests.</summary>
   bool CanView(ActionRequest caller) => false;
   /// <summary>Whether this caller may decide requests and save directly.</summary>
   bool CanDecide(ActionRequest caller) => false;
   /// <summary>Whether this caller may withdraw another user's request.</summary>
   bool CanWithdraw(ActionRequest caller) => false;
}
/// <summary>Child structure whose foreign-key properties match the parent's key by position.</summary>
public interface IEmApprovalDataChild<TEntity, TParent> : IEmApprovalDataTable<TEntity>
   where TEntity : class where TParent : class {
   /// <summary>Child properties receiving the parent's keys.</summary>
   string[] ForeignKeyColumns { get; }
}
/// <summary>A proposed row and its children. Create carries a complete immutable snapshot.</summary>
public sealed class DataChange {
   private DataChange(object entity, object? original, ApprovalItemOperation operation) {
      Entity = entity; Original = original; Operation = operation;
   }
   /// <summary>Proposed entity.</summary>
   public object Entity { get; }
   /// <summary>Original loaded object for Update.</summary>
   public object? Original { get; }
   /// <summary>Row operation.</summary>
   public ApprovalItemOperation Operation { get; }
   /// <summary>Ordered child changes.</summary>
   public List<DataChange> Children { get; } = [];
   /// <summary>Appends child changes to this tree.</summary>
   public DataChange With(params DataChange[] children) { Children.AddRange(children); return this; }
   /// <summary>Proposes insertion without touching the owning table.</summary>
   public static DataChange Create<T>(T entity) where T : class => new(entity, null, ApprovalItemOperation.Create);
   /// <summary>Compares the screen's original and edited objects.</summary>
   public static DataChange Update<T>(T original, T modified) where T : class => new(modified, original, ApprovalItemOperation.Update);
   /// <summary>Proposes declared deletion.</summary>
   public static DataChange Delete<T>(T entity) where T : class => new(entity, null, ApprovalItemOperation.Delete);
   /// <summary>Proposes restoration of a soft-deleted row.</summary>
   public static DataChange Restore<T>(T entity) where T : class => new(entity, null, ApprovalItemOperation.Restore);
}
/// <summary>Module's resolution of a Dynamic Signer.</summary>
public sealed record ApprovalDynamicSigner(string UserId, bool SignImmediately = false);
/// <summary>Optional server hooks for one System definition.</summary>
public interface IEmApprovalSystemHandler<TDefinition, TKey>
   where TDefinition : IEmApprovalSystem<TKey> where TKey : notnull {
   /// <summary>Resolves DS; null is rejected with the approval and step name.</summary>
   Task<ApprovalDynamicSigner?> ResolveDynamicSignerAsync(ApprovalSystemStep step, TKey key,
      ActionRequest requester, CancellationToken ct = default) => Task.FromResult<ApprovalDynamicSigner?>(null);
   /// <summary>Runs after completion is committed; failure is logged.</summary>
   Task OnCompletedAsync(string requestId, TKey key, CancellationToken ct = default) => Task.CompletedTask;
   /// <summary>Runs after rejection is committed; failure is logged.</summary>
   Task OnRejectedAsync(string requestId, TKey key, CancellationToken ct = default) => Task.CompletedTask;
}
