namespace Em.Ui.Wpf.Core
{
   /// <summary>
   /// Details of how a session ended. Used by the login screen to tell a session that the user ended apart
   /// from one that died on its own - the second needs a sentence of explanation, the first does not.
   /// </summary>
   /// <param name="reason">
   /// The reason the session ended, or <c>null</c> when the user signed out by themselves. This sentence
   /// is shown as an ordinary note, not as an error message: a session that has simply expired is not the
   /// user's error.
   /// </param>
   public sealed class SessionEndedEventArgs(string? reason) : EventArgs
   {
      /// <inheritdoc cref="SessionEndedEventArgs(string?)" path="/param[@name='reason']" />
      public string? Reason { get; } = reason;
   }
}
