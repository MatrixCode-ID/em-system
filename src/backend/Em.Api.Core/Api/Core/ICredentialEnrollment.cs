namespace Em.Api.Core
{
   /// <summary>
   /// Contract for one-step credential enrollment: the user only needs to send one proof and the
   /// credential is immediately ready to use - like a password, which only needs to be typed once.
   /// Kept apart from <see cref="CredentialProviderBase"/> because not every credential type is enrolled
   /// from inside this application.
   /// </summary>
   /// <typeparam name="TProof">The kind of proof asked for at enrollment.</typeparam>
   public interface ICredentialEnrollment<in TProof>
   {
      /// <summary>
      /// Completes the enrollment with the user's proof, then marks the credential ready to use for signing in.
      /// </summary>
      /// <param name="proof">The user's proof, e.g. a new password or the first code from an authenticator app.</param>
      Task ConfirmEnrollAsync(TProof proof);
   }

   /// <summary>
   /// Contract for two-step credential enrollment: the application first prepares what the user must take
   /// elsewhere - for example a secret that an authenticator app must scan through a QR code - and only
   /// then is the enrollment confirmed with proof that the first step succeeded.
   /// </summary>
   /// <typeparam name="TSetup">The kind of data produced by the setup step.</typeparam>
   /// <typeparam name="TProof">The kind of proof asked for to confirm that setup.</typeparam>
   public interface ICredentialEnrollment<TSetup, in TProof> : ICredentialEnrollment<TProof>
   {
      /// <summary>
      /// Starts the enrollment and returns the data that needs to be shown to the user. As long as
      /// <see cref="ICredentialEnrollment{TProof}.ConfirmEnrollAsync"/> has not been called, the credential
      /// cannot be used for signing in yet.
      /// </summary>
      /// <returns>The setup data that must be passed on to the user.</returns>
      Task<TSetup> BeginEnrollAsync();
   }
}
