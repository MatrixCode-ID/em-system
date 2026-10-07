namespace Em.Shared
{
   /// <summary>State of a contact communication channel.</summary>
   public enum ContactCommunicationState
   {
      /// <summary>Not used anymore.</summary>
      Inactive = -1,

      /// <summary>Active and the default channel of its kind.</summary>
      ActiveAsDefault = 0,

      /// <summary>Active.</summary>
      Active = 1,

      /// <summary>Active and referenced by a user.</summary>
      ActiveAsUserReference = 2
   }
}
