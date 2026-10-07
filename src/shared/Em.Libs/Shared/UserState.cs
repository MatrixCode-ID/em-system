namespace Em.Shared
{
   /// <summary>Lifecycle state of a user account; negative values cannot sign in.</summary>
   public enum UserState
   {
      /// <summary>The account has been deleted.</summary>
      Deleted = -3,
      /// <summary>The account is suspended.</summary>
      Suspended = -2,
      /// <summary>The account is waiting for activation.</summary>
      Pending = -1,
      /// <summary>The account exists but is inactive.</summary>
      Inactive = 0,
      /// <summary>The account is active.</summary>
      Active = 1,
   }
}
