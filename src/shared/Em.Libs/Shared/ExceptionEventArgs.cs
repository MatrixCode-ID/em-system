namespace Em.Shared
{
   /// <summary>
   /// Event arguments carrying the <see cref="System.Exception"/> that occurred, used by error handling
   /// events (e.g. the global exception handler in the UI).
   /// </summary>
   public class ExceptionEventArgs : EventArgs
   {
      /// <summary>
      /// Creates new event arguments from an exception.
      /// </summary>
      /// <param name="exception">The exception that occurred. Must not be <c>null</c>.</param>
      /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is <c>null</c>.</exception>
      public ExceptionEventArgs(Exception exception) {
         RaisedException = exception ?? throw new ArgumentNullException(nameof(exception));
      }

      /// <summary>
      /// The original exception that raised this event.
      /// </summary>
      public Exception RaisedException { get; }

      /// <summary>
      /// Short message of <see cref="RaisedException"/>, for convenient display to the user or the log.
      /// </summary>
      public string ExceptionMessage => RaisedException.Message;
   }
}
