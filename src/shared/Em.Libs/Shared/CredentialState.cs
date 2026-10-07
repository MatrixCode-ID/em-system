namespace Em.Shared
{
   /// <summary>
   /// State of a user credential. Follows the same value rule as other states in this application:
   /// negative values mean the credential must not be used to sign in, zero and above mean it is
   /// registered.
   /// </summary>
   public enum CredentialState
   {
      /// <summary>Deleted and cannot be restored.</summary>
      Deleted = -3,

      /// <summary>Revoked, e.g. because the device was lost or the secret leaked.</summary>
      Revoked = -2,

      /// <summary>
      /// Prepared but not fully registered - for example the row exists but the user has never set a
      /// password, or the first code from an authenticator app has not been confirmed. Cannot be used to
      /// sign in yet.
      /// </summary>
      Pending = -1,

      /// <summary>Registered, but temporarily disabled by the user or an administrator.</summary>
      Inactive = 0,

      /// <summary>Registered and ready to sign in with.</summary>
      Active = 1
   }
}
