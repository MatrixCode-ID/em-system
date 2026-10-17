using System.Reflection;
using System.Text.Json;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Em.Api.Core.Approval;

internal sealed record ApprovalDataContext(DbContext DbContext, ActionRequest Caller,
   ApprovalItemOperation Operation) : IEmApprovalDataContext;

internal sealed class DataRegistration(Type definitionType, Type entityType, Type? parentType) {
   public Type DefinitionType { get; } = definitionType;
   public Type EntityType { get; } = entityType;
   public Type? ParentType { get; } = parentType;
   public Type ContextType { get; set; } = null!;
   public string EntityName => EntityType.FullName!;
   public DataTable Resolve(IServiceProvider services) => (DataTable)Activator.CreateInstance(
      typeof(DataTable<,>).MakeGenericType(DefinitionType, EntityType), this, services)!;
}

internal abstract class DataTable {
   public abstract DataRegistration Registration { get; }
   public abstract DbContext Context { get; }
   public abstract string[] Keys { get; }
   public abstract string[] Tracked { get; }
   public abstract string[] ForeignKeys { get; }
   public abstract string[] ApproverOnly { get; }
   public abstract ApprovalDeleteMode DeleteMode { get; }
   public abstract string? StateColumn { get; }
   public abstract string? StampColumn { get; }
   public abstract object? DeletedState { get; }
   public abstract object? ActiveState { get; }
   public abstract string Title { get; }
   public abstract bool CanView(ActionRequest caller);
   public abstract bool CanDecide(ActionRequest caller);
   public abstract bool CanWithdraw(ActionRequest caller);
   public abstract Task ValidateAsync(object entity, IEmApprovalDataContext context, CancellationToken ct);
   public abstract Task CreateKeyAsync(object entity, IEmApprovalDataContext context, CancellationToken ct);
   public abstract Task<object?> FindAsync(string key, CancellationToken ct);
   public PropertyInfo Property(string column) => Registration.EntityType.GetProperty(column) ??
      throw new InvalidOperationException($"Unknown approval column '{column}' on {Registration.EntityName}.");
   public string KeyOf(object entity) => ApprovalSubjectKey.Serialize(Keys.Select(k => Property(k).GetValue(entity)).ToArray());
}

internal sealed class DataTable<TDefinition, TEntity>(DataRegistration registration, IServiceProvider services) : DataTable
   where TDefinition : class, IEmApprovalDataTable<TEntity> where TEntity : class {
   private readonly TDefinition _definition = services.GetRequiredService<TDefinition>();
   public override DataRegistration Registration => registration;
   public override DbContext Context => (DbContext)services.GetRequiredService(registration.ContextType);
   public override string[] Keys => _definition.KeyColumns;
   public override string[] Tracked => _definition.TrackedColumns;
   public override string[] ForeignKeys => registration.ParentType is null ? [] :
      (string[])typeof(IEmApprovalDataChild<,>).MakeGenericType(typeof(TEntity), registration.ParentType)
         .GetProperty(nameof(IEmApprovalDataChild<object, object>.ForeignKeyColumns))!.GetValue(_definition)!;
   public override string[] ApproverOnly => _definition.ApproverOnlyColumns;
   public override ApprovalDeleteMode DeleteMode => _definition.DeleteMode;
   public override string? StateColumn => _definition.DeleteStateColumn;
   public override string? StampColumn => _definition.DeleteStampColumn;
   public override object? DeletedState => _definition.DeletedStateValue;
   public override object? ActiveState => _definition.ActiveStateValue;
   private IEmApprovalData<TEntity>? Root => _definition as IEmApprovalData<TEntity>;
   public override string Title => Root?.Title ?? string.Empty;
   public override bool CanView(ActionRequest caller) => Root?.CanView(caller) == true;
   public override bool CanDecide(ActionRequest caller) => Root?.CanDecide(caller) == true;
   public override bool CanWithdraw(ActionRequest caller) => Root?.CanWithdraw(caller) == true;
   public override Task ValidateAsync(object entity, IEmApprovalDataContext context, CancellationToken ct) =>
      _definition.ValidateAsync((TEntity)entity, context, ct);
   public override Task CreateKeyAsync(object entity, IEmApprovalDataContext context, CancellationToken ct) =>
      _definition.CreateKeyAsync((TEntity)entity, context, ct);
   public override async Task<object?> FindAsync(string key, CancellationToken ct) {
      var values = JsonSerializer.Deserialize<JsonElement[]>(key) ?? throw new ActionException("Invalid Data subject key.", 409);
      if (values.Length != Keys.Length) throw new ActionException("The stored key no longer matches the Data definition.", 409);
      var parts = values.Select((v, i) => v.Deserialize(Property(Keys[i]).PropertyType, Defaults.ResponseJsonOptions)).ToArray();
      var row = await Context.FindAsync(typeof(TEntity), parts, ct);
      if (row is not null && Context.Entry(row).State == EntityState.Detached) Context.Attach(row);
      return row;
   }
}

internal interface ISystemHandlerAdapter {
   Task<ApprovalDynamicSigner?> ResolveAsync(IServiceProvider provider, ApprovalSystemStep step, string key,
      ActionRequest requester, CancellationToken ct);
   Task FinishAsync(IServiceProvider provider, string id, string key, bool approved, CancellationToken ct);
}

internal sealed class SystemHandlerAdapter<TDefinition, TKey, THandler> : ISystemHandlerAdapter
   where TDefinition : IEmApprovalSystem<TKey> where TKey : notnull
   where THandler : class, IEmApprovalSystemHandler<TDefinition, TKey> {
   private static TKey Parse(string key) => typeof(TKey) == typeof(string) ? (TKey)(object)key :
      JsonSerializer.Deserialize<TKey>(key, Defaults.ResponseJsonOptions) ?? throw new ActionException("Invalid System subject key.", 400);
   public Task<ApprovalDynamicSigner?> ResolveAsync(IServiceProvider provider, ApprovalSystemStep step,
      string key, ActionRequest requester, CancellationToken ct) =>
      provider.GetRequiredService<THandler>().ResolveDynamicSignerAsync(step, Parse(key), requester, ct);
   public Task FinishAsync(IServiceProvider provider, string id, string key, bool approved, CancellationToken ct) =>
      approved ? provider.GetRequiredService<THandler>().OnCompletedAsync(id, Parse(key), ct) :
         provider.GetRequiredService<THandler>().OnRejectedAsync(id, Parse(key), ct);
}

internal sealed record SystemRegistration(Type DefinitionType, IEmApprovalSystem Definition, ISystemHandlerAdapter? Handler);

internal sealed class ApprovalRegistry(IEnumerable<DataRegistration> data, IEnumerable<SystemRegistration> systems,
   Type[] contextTypes, Action<Type[]> verifyDatabases) {
   public DataRegistration[] Data { get; } = data.ToArray();
   public SystemRegistration[] Systems { get; } = systems.ToArray();
   public DataRegistration DataEntity(Type type) => Data.SingleOrDefault(x => x.EntityType == type) ??
      throw new ActionException($"Data approval for '{type.FullName}' is not registered.", 400);
   public DataRegistration DataEntity(string name) => Data.SingleOrDefault(x => x.EntityName == name) ??
      throw new ActionException($"Data approval entity '{name}' is no longer registered.", 409);
   public DataRegistration DataRoot(string name) => Data.SingleOrDefault(x => x.ParentType is null &&
      ApprovalSubjectKey.NameOf(x.DefinitionType) == name) ?? throw new ActionException($"Unknown Data approval '{name}'.", 404);
   public SystemRegistration System(string name) => Systems.SingleOrDefault(x =>
      ApprovalSubjectKey.NameOf(x.DefinitionType) == name) ?? throw new ActionException($"Unknown System approval '{name}'.", 404);
   public IEnumerable<Type> Contexts => Data.Select(x => x.ContextType).Distinct();

   public void Initialize(IServiceProvider provider) {
      using var scope = provider.CreateScope();
      var services = scope.ServiceProvider;
      var logger = services.GetRequiredService<ILogger<ApprovalRegistry>>();
      var contexts = contextTypes.Select(t => (Type: t, Context: (DbContext)services.GetRequiredService(t))).ToArray();
      var names = new Dictionary<(ApprovalKind, string), Type>();
      foreach (var registration in Data) {
         var attribute = registration.DefinitionType.GetCustomAttribute<ApprovalAttribute>();
         if (registration.ParentType is null) AddName(ApprovalKind.Data, registration.DefinitionType);
         else if (attribute is not null) throw new InvalidOperationException($"Child '{registration.DefinitionType}' must not have [Approval].");
         if (Data.Count(x => x.EntityType == registration.EntityType) != 1)
            throw new InvalidOperationException($"Multiple Data definitions own '{registration.EntityType}'.");
         var owners = contexts.Where(c => c.Context.Model.FindEntityType(registration.EntityType) is not null).ToArray();
         if (owners.Length != 1) throw new InvalidOperationException($"Data entity '{registration.EntityType}' must be mapped in exactly one registered context; found {owners.Length}.");
         registration.ContextType = owners[0].Type;
      }
      verifyDatabases(Data.Select(x => x.ContextType).Distinct().ToArray());
      foreach (var registration in Data) {
         var table = registration.Resolve(services);
         var ef = table.Context.Model.FindEntityType(registration.EntityType)!;
         if (table.Keys.Length == 0 || table.Keys.Distinct().Count() != table.Keys.Length ||
             !table.Keys.SequenceEqual(ef.FindPrimaryKey()?.Properties.Select(p => p.Name) ?? []))
            throw new InvalidOperationException($"Data keys for '{registration.EntityType}' must match its complete EF primary key in order.");
         foreach (var column in table.Keys.Concat(table.Tracked).Concat(table.ApproverOnly)
                     .Concat(table.ForeignKeys).Concat(new[] { table.StateColumn, table.StampColumn }.OfType<string>())) {
            var property = ef.FindProperty(column) ?? throw new InvalidOperationException($"Unmapped approval column '{registration.EntityType}.{column}'.");
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (!(type.IsEnum || type.IsPrimitive || type == typeof(string) || type == typeof(decimal) ||
                  type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) ||
                  type == typeof(TimeOnly) || type == typeof(Guid)))
               throw new InvalidOperationException($"Unsupported approval type '{type}' for '{column}'.");
         }
         if (table.Tracked.Intersect(table.Keys).Any()) throw new InvalidOperationException("Approval keys cannot be tracked update columns.");
         if (table.DeleteMode == ApprovalDeleteMode.Soft) {
            if (table.StateColumn is null) throw new InvalidOperationException("Soft delete requires a state column.");
            var type = Nullable.GetUnderlyingType(table.Property(table.StateColumn).PropertyType) ?? table.Property(table.StateColumn).PropertyType;
            if (type != typeof(bool) && (table.DeletedState is null || table.ActiveState is null))
               throw new InvalidOperationException("Non-boolean soft delete requires explicit deleted and active values.");
            if (table.StampColumn is not null && table.Property(table.StampColumn).PropertyType != typeof(DateTime?))
               throw new InvalidOperationException("The deletion timestamp must be a nullable DateTime.");
         } else if (table.StateColumn is not null || table.StampColumn is not null)
            throw new InvalidOperationException("Deletion columns require Soft deletion mode.");
         if (registration.ParentType is { } parentType) {
            var parent = DataEntity(parentType).Resolve(services);
            if (table.ForeignKeys.Length != parent.Keys.Length) throw new InvalidOperationException("Approval foreign key count does not match the parent.");
            for (var i = 0; i < table.ForeignKeys.Length; i++) {
               if (table.Property(table.ForeignKeys[i]).PropertyType != parent.Property(parent.Keys[i]).PropertyType)
                  throw new InvalidOperationException("Approval foreign key types do not match the parent in order.");
               if (table.ForeignKeys[i] != parent.Keys[i]) logger.LogWarning("Approval foreign-key names differ: {Child} -> {Parent}; verify their order.", table.ForeignKeys[i], parent.Keys[i]);
            }
            var visited = new HashSet<Type> { registration.EntityType };
            for (var p = registration; p.ParentType is not null; p = DataEntity(p.ParentType))
               if (!visited.Add(p.ParentType)) throw new InvalidOperationException("Approval parent relationships contain a cycle.");
         }
      }
      foreach (var registration in Systems) {
         AddName(ApprovalKind.System, registration.DefinitionType);
         var steps = registration.Definition.Steps;
         if (steps.Count == 0 || steps.Any(s => s.Level < 1 || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 100) ||
             steps.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != steps.Count)
            throw new InvalidOperationException($"System approval '{registration.DefinitionType}' needs unique step names and positive levels.");
      }
      void AddName(ApprovalKind kind, Type type) {
         var name = ApprovalSubjectKey.NameOf(type);
         var key = (kind, name.ToUpperInvariant());
         if (!names.TryAdd(key, type)) throw new InvalidOperationException($"Duplicate {kind} approval '{name}': '{names[key]}' and '{type}'.");
      }
   }
}
