namespace Em.Shared
{
   /// <summary>
   /// State of a sign-in session. Follows the same value rule as other states in this application:
   /// negative values mean the session can no longer be used, zero and above mean it is still alive.
   /// </summary>
   public enum SessionState
   {
      /// <summary>Deleted and cannot be restored.</summary>
      Deleted = -3,

      /// <summary>
      /// Revoked before it ran out - the user signed out, an administrator ended it, or its refresh token
      /// has been exchanged for a new one.
      /// </summary>
      Revoked = -2,

      /// <summary>Its lifetime has passed. It can no longer issue new tokens.</summary>
      Expired = -1,

      /// <summary>Still running and its refresh token can still be exchanged.</summary>
      Active = 1
   }
}
