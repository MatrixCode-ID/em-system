namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// The command parameter of <see cref="DropTarget"/> for a drop place that names its target through
   /// <c>DropTarget.Target</c>: the payload together with where it was dropped. Used when one command
   /// serves many drop places - for example every folder row in a list - and needs to know which row was
   /// aimed at.
   /// </summary>
   public sealed class DropRequest
   {
      /// <summary>
      /// Composes a drop request.
      /// </summary>
      /// <param name="payload">The payload that was dropped.</param>
      /// <param name="target">The drop target, that is, the <c>DropTarget.Target</c> value on its element.</param>
      public DropRequest(object payload, object target) {
         Payload = payload;
         Target = target;
      }

      /// <summary>
      /// The payload that was dropped: the <see cref="DragSource"/> payload, or <see cref="DroppedFiles"/> for
      /// files from outside the application.
      /// </summary>
      public object Payload { get; }

      /// <summary>The drop target, that is, the <c>DropTarget.Target</c> value on the receiving element.</summary>
      public object Target { get; }
   }
}
