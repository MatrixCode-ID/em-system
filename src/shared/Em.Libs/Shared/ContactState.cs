namespace Em.Shared
{
   /// <summary>State of a contact.</summary>
   public enum ContactState
   {
      /// <summary>Marked as deleted.</summary>
      Deleted = -2,

      /// <summary>Not used anymore.</summary>
      Inactive = -1,

      /// <summary>Active.</summary>
      Active = 0
   }
}