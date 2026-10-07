namespace Em.Shared
{
   /// <summary>State of an address row.</summary>
   public enum AddressState
   {
      /// <summary>Not used anymore.</summary>
      Inactive = -1,

      /// <summary>Active.</summary>
      Active = 0,

      /// <summary>Active and the contact's default address.</summary>
      ActiveAsDefault = 1,

      /// <summary>Active and referenced by a user.</summary>
      ActiveAsUserReference = 2
   }
}
