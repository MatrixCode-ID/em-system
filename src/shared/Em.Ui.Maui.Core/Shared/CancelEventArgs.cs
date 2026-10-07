namespace Em.Ui.Maui.Shared
{
   /// <summary>
   /// Event arguments that can cancel an action in progress, used e.g. on the
   /// <c>UiCommandBase.CommandExecuting</c> event to cancel a command's execution.
   /// </summary>
   public class CancelEventArgs : EventArgs
   {
      /// <summary>
      /// An empty instance with default values (not cancelled), for ease of use.
      /// </summary>
      public static new CancelEventArgs Empty => new();

      /// <summary>
      /// Set to <c>true</c> by a listener to cancel the action being processed.
      /// </summary>
      public bool Cancel { get; set; }

      /// <summary>
      /// A description of the reason for cancelling, used as the default message when
      /// <see cref="ThrowException"/> is on without an explicit <see cref="ExceptionToThrow"/>.
      /// </summary>
      public string Description { get; set; } = "";

      /// <summary>
      /// Free additional data that a listener may leave for the event's processor.
      /// </summary>
      public object? Tag { get; set; }

      /// <summary>
      /// When <c>true</c>, the event's processor throws an exception (see <see cref="ExceptionToThrow"/>)
      /// instead of just cancelling the action silently.
      /// </summary>
      public bool ThrowException { get; set; }

      /// <summary>
      /// The specific exception to throw when <see cref="ThrowException"/> is <c>true</c>. When <c>null</c>,
      /// an <see cref="OperationCanceledException"/> with the message from <see cref="Description"/> is used.
      /// </summary>
      public Exception? ExceptionToThrow { get; set; }
   }
}
