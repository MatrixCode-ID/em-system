using System.Windows;
using Em.Api.Core.Models;
using Em.Ui.Core.Shared;
namespace Em.Ui.Wpf.Shared;
/// <summary>System approval editor registration for WPF modules.</summary>
public partial class EmAppBuilder {
   internal ApprovalEditorRegistry ApprovalEditors { get; } = new();
   /// <summary>Registers the module-owned main view; the engine provides the surrounding workflow UI.</summary>
   public void AddSystemApproval<TDefinition, TEditor>()
      where TDefinition : IEmApprovalSystem where TEditor : FrameworkElement, IEmApprovalSystemEditor =>
      ApprovalEditors.Add<TDefinition, TEditor>();
}
