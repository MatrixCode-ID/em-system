namespace Em.Shared
{
   /// <summary>How a contact search term is matched.</summary>
   public enum ContactSearchType
   {
      /// <summary>Full-text search over the contact.</summary>
      FullTextSearch,

      /// <summary>Search by name.</summary>
      ByNameSearch,

      /// <summary>Search by address.</summary>
      ByAddressSearch
   }
}