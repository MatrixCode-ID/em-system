using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// A password credential: the proof is text typed by the user, and only its hash is stored, never the
   /// password itself.
   /// </summary>
   public class PasswordCredential : CredentialProviderBase<string>, ICredentialEnrollment<string>
   {
      /// <summary>Name of this credential type.</summary>
      public const string CredentialType = Defaults.PasswordCredentialType;

      #region Initializer

      /// <summary>
      /// Creates the password credential of <paramref name="user"/> together with its stored data. A user who
      /// has never set a password still gets this credential, only its status remains
      /// <see cref="CredentialState.Pending"/> until the password is set through
      /// <see cref="UpdateCredential"/>.
      /// </summary>
      /// <param name="user">The user whose credential is to be used.</param>
      /// <param name="services">The credential data service of the running request.</param>
      /// <returns>A password credential ready to be checked or filled in.</returns>
      public static async Task<PasswordCredential> CreateAsync(ta_User user, CredentialServices services) {
         var result = new PasswordCredential {
            User = user,
            Services = services,
            Hasher = services.GetService<IStringHasher>()
               ?? throw new InvalidOperationException($"No {nameof(IStringHasher)} is registered.")
         };
         await result.InitCredential();
         return result;
      }

      #endregion

      private PasswordCredential() { }

      /// <inheritdoc/>
      public override string Name => CredentialType;

      /// <summary>The hashing component used to store and check passwords.</summary>
      public required IStringHasher Hasher { get; init; }

      /// <summary>
      /// Changes the user's password and marks the credential ready to use for signing in.
      /// </summary>
      /// <param name="password">The new password as plain text; only its hash is stored.</param>
      /// <exception cref="ArgumentException">The given password is empty.</exception>
      public Task UpdateCredential(string password) {
         if (string.IsNullOrEmpty(password)) {
            throw new ArgumentException("Password must not be empty.", nameof(password));
         }

         return SaveCredentialAsync(data => {
            data.cCredentialSecret = Hasher.HashValue(password);
            data.cCredentialState = CredentialState.Active;
         });
      }

      // Setting a password is all it takes to enroll in this credential, so enrollment is the same
      // operation as changing it. Implemented explicitly to keep one public way of doing it.
      Task ICredentialEnrollment<string>.ConfirmEnrollAsync(string proof) => UpdateCredential(proof);

      /// <summary>Loads the stored password credential, or creates a pending one when the user has none.</summary>
      protected override async Task InitCredential() {
         // Creating a user writes its password credential along with it, so normally there is one
         // to read here. What is left is the account that predates that, which would otherwise have
         // no credential to edit and no way to ever get one; it is given the same empty, Pending
         // credential a new account is created with. Opt-in credentials - an authenticator app, say
         // - do the opposite and create nothing until their enrollment finishes.
         if (await LoadCredentialAsync() is null) await CreateCredentialAsync();
      }

      /// <summary>Checks the typed password against the stored hash.</summary>
      protected override Task<bool> ValidateAsync(string payload) =>
         Task.FromResult(Hasher.CompareHashValue(payload, UserCredential?.cCredentialSecret ?? ""));
   }

   /// <summary>
   /// Registers <see cref="PasswordCredential"/> as one of the credential types known to the application.
   /// </summary>
   public class PasswordCredentialFactory : ICredentialProviderFactory
   {
      /// <inheritdoc/>
      public string Name => PasswordCredential.CredentialType;

      /// <inheritdoc/>
      public async Task<CredentialProviderBase> CreateAsync(ta_User user, CredentialServices services) =>
         await PasswordCredential.CreateAsync(user, services);
   }
}
