using Microsoft.Maui.Controls;
using Em.Api.Core.Models;
using Em.Ui.Core.Shared;

namespace Em.Ui.Maui.Shared
{
   /// <summary>System approval editor registration for MAUI modules.</summary>
   public partial class EmAppBuilder
   {
      internal ApprovalEditorRegistry ApprovalEditors { get; } = new();

      /// <summary>
      /// Registers the module-owned view of a System approval. The engine provides the surrounding mobile
      /// workflow: the request list, signer steps, decisions, comments and the PDF preview hand-off.
      /// </summary>
      /// <typeparam name="TDefinition">The shared approval definition.</typeparam>
      /// <typeparam name="TEditor">The module view; it must be a MAUI <see cref="View"/>.</typeparam>
      public void AddSystemApproval<TDefinition, TEditor>()
         where TDefinition : IEmApprovalSystem where TEditor : View, IEmApprovalSystemEditor =>
         ApprovalEditors.Add<TDefinition, TEditor>();
   }
}
