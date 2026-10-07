using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Base class for every way a user proves themselves - password, authenticator app, and other ways to
   /// come. A derived class only needs to define the credential type name, how its data is loaded when
   /// created, and how to check the proof the user sends; what is the same for every type - reading,
   /// creating, storing, and revoking credential data - is already provided here.
   /// <para>
   /// Do not derive from it directly: use <see cref="CredentialProviderBase{TPayload}"/> so the kind of
   /// proof accepted is also checked by the compiler.
   /// </para>
   /// </summary>
   public abstract class CredentialProviderBase
   {
      /// <summary>The user who owns this credential.</summary>
      public required ta_User User { get; init; }

      /// <summary>
      /// The server's credential data service, where credential rows are read and written. Filled by the
      /// running action, because only it holds the DbContext of that request.
      /// </summary>
      public required CredentialServices Services { get; init; }

      /// <summary>
      /// Name of this credential type, e.g. <c>"PASSWORD"</c>. Used as the marker when looking up a user's
      /// credential data, so its value must be fixed and unique among types.
      /// </summary>
      public abstract string Name { get; }

      // The stored credential this provider works on. Null until InitCredential has run, and for
      // credentials that only come into existence once the user has enrolled in them.
      /// <summary>The stored credential this provider works on. <c>null</c> until it has been loaded or created.</summary>
      protected ta_UserCredential? UserCredential { get; private set; }

      /// <summary>
      /// Status of this credential. Is <see cref="CredentialState.Pending"/> as long as the user has no
      /// credential of this type at all.
      /// </summary>
      public CredentialState State => UserCredential?.cCredentialState ?? CredentialState.Pending;

      /// <summary>
      /// Indicates that this credential has finished being enrolled and may be used to sign in. While still
      /// <c>false</c>, <see cref="IsValidAsync"/> always refuses whatever the proof.
      /// </summary>
      public bool IsEnrolled => UserCredential is not null && State >= CredentialState.Inactive;

      /// <summary>
      /// Checks whether the proof sent by the user matches the stored credential. Used by code that does
      /// not know which credential type it is holding - the login flow, for example, which offers the user's
      /// credentials one by one.
      /// </summary>
      /// <param name="payload">
      /// The proof from the user. Its shape is decided by each credential type: text for a password, a
      /// numeric code for an authenticator app, and so on.
      /// </param>
      /// <returns><c>true</c> if the proof is valid.</returns>
      /// <exception cref="ArgumentException">
      /// The type of <paramref name="payload"/> is not the one this credential asks for.
      /// </exception>
      public abstract Task<bool> IsValidAsync(object payload);

      /// <summary>
      /// Revokes this credential so it can no longer be used to sign in, without deleting its data. Used for
      /// example when the user's authenticator device is lost.
      /// </summary>
      public async Task RevokeAsync() =>
         await SaveCredentialAsync(data => data.cCredentialState = CredentialState.Revoked);

      /// <summary>
      /// Runs once while the provider is being created: this is where a subclass decides whether it
      /// merely reads what is already stored, or also creates a credential for a user who has none.
      /// </summary>
      protected abstract Task InitCredential();

      /// <summary>
      /// Reads the stored credential of this type for the user and makes it the one this provider
      /// works on. Returns <c>null</c> when the user has no credential of this type.
      /// </summary>
      protected async Task<ta_UserCredential?> LoadCredentialAsync() =>
         UserCredential = await Services.GetTa_UserCredential_ByType(User.cUserId, Name);

      /// <summary>
      /// Creates the credential for this user. Left <see cref="CredentialState.Pending"/> by default: a
      /// credential that was only just created still holds nothing to check a login against.
      /// </summary>
      /// <param name="state">The initial state of the credential.</param>
      /// <param name="key">The public part of the credential, if its type has one.</param>
      /// <param name="secret">The secret part of the credential, if its type has one.</param>
      protected async Task<ta_UserCredential> CreateCredentialAsync(
         CredentialState state = CredentialState.Pending, string? key = null, string? secret = null) {
         var stamp = await Services.App.GetDateStampAsync();
         var data = new ta_UserCredential {
            cCredentialId = $"{Ulid.NewUlid()}",
            cUserId = User.cUserId,
            cCredentialType = Name,
            cCredentialState = state,
            cCredentialKey = key,
            cCredentialSecret = secret,
            ustamp = stamp,
            datestamp = stamp,
            json_object = null
         };
         await Services.PostTa_UserCredential_New(data);
         UserCredential = data;
         return data;
      }

      /// <summary>
      /// Applies an edit to the stored credential and writes it back. The edit runs on a copy rather
      /// than on the data being held, so that a write the server rejects leaves the provider showing
      /// what is actually stored instead of a change that never landed.
      /// </summary>
      /// <param name="edit">The change to apply to the copy.</param>
      protected async Task<ta_UserCredential> SaveCredentialAsync(Action<ta_UserCredential> edit) {
         var current = UserCredential
            ?? throw new InvalidOperationException(
               $"Credential '{Name}' has not been created for user '{User.cUserId}'.");

         var draft = new ta_UserCredential {
            cCredentialId = current.cCredentialId,
            cUserId = current.cUserId,
            cCredentialType = current.cCredentialType,
            cCredentialState = current.cCredentialState,
            cCredentialKey = current.cCredentialKey,
            cCredentialSecret = current.cCredentialSecret,
            ustamp = current.ustamp,
            datestamp = current.datestamp,
            json_object = current.json_object
         };

         edit(draft);
         draft.ustamp = await Services.App.GetDateStampAsync();
         await Services.PostTa_UserCredential_Update(draft);
         UserCredential = draft;
         return draft;
      }
   }

   /// <summary>
   /// Base class for credential types whose proof has the shape <typeparamref name="TPayload"/>. Besides
   /// keeping the proof type matching, it also makes sure a credential that is not enrolled or has been
   /// revoked is always refused, so derived classes only need to take care of checking the proof's
   /// content.
   /// </summary>
   /// <typeparam name="TPayload">
   /// The kind of proof asked from the user, e.g. <see cref="string"/> for a password.
   /// </typeparam>
   public abstract class CredentialProviderBase<TPayload> : CredentialProviderBase
   {
      /// <inheritdoc/>
      public sealed override Task<bool> IsValidAsync(object payload) =>
         payload is TPayload typed
            ? IsValidAsync(typed)
            : throw new ArgumentException(
               $"Credential '{Name}' expects a payload of type {typeof(TPayload).Name}.", nameof(payload));

      /// <summary>
      /// Checks whether the proof sent by the user matches the stored credential. A credential that has not
      /// finished enrolling or has been revoked is always refused here, without checking the proof's content.
      /// </summary>
      /// <param name="payload">The proof from the user.</param>
      /// <returns><c>true</c> if the proof is valid.</returns>
      public Task<bool> IsValidAsync(TPayload payload) =>
         IsEnrolled ? ValidateAsync(payload) : Task.FromResult(false);

      /// <summary>
      /// The actual check, reached only for a credential that is enrolled and not revoked. Async on
      /// purpose: a one-time code has to record that it was used so the same code cannot be replayed,
      /// and that recording is a write.
      /// </summary>
      /// <param name="payload">The proof from the user.</param>
      protected abstract Task<bool> ValidateAsync(TPayload payload);
   }
}
