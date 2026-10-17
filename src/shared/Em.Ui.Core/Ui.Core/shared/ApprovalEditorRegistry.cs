using Em.Api.Core.Models;
namespace Em.Ui.Core.Shared;
/// <summary>Explicit platform-specific views for System approval definitions.</summary>
public sealed class ApprovalEditorRegistry {
   private readonly Dictionary<string, Type> _editors = new(StringComparer.OrdinalIgnoreCase);
   /// <summary>Registers one editor type under the shared definition's stable name.</summary>
   public void Add<TDefinition, TEditor>() where TDefinition : IEmApprovalSystem where TEditor : IEmApprovalSystemEditor {
      var name = ApprovalSubjectKey.NameOf(typeof(TDefinition));
      if (!_editors.TryAdd(name, typeof(TEditor))) throw new InvalidOperationException($"System editor '{name}' is already registered.");
   }
   /// <summary>Finds the owning module's editor, if installed in this platform.</summary>
   public Type? Find(string name) => _editors.GetValueOrDefault(name);
}
