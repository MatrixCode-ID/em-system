namespace Em.Test.Models
{
   /// <summary>The state of a test item.</summary>
   public enum TestItemState
   {
      /// <summary>The item is active and may be used.</summary>
      Active = 0,

      /// <summary>Item dinonaktifkan.</summary>
      Disabled = 1
   }

   /// <summary>The life stage of a test document with respect to approval.</summary>
   public enum TestDocStatus
   {
      /// <summary>Draft: it can still be changed and has not been submitted.</summary>
      Draft = 0,

      /// <summary>Submitted and waiting for a decision.</summary>
      InApproval = 1,

      /// <summary>All approval steps are complete.</summary>
      Approved = 2,

      /// <summary>Rejected by one of the steps.</summary>
      Rejected = 3
   }

   /// <summary>The kind of result of a test business task.</summary>
   public enum TestTaskOutput
   {
      /// <summary>No result to fetch.</summary>
      None = 0,

      /// <summary>The result is JSON data.</summary>
      Json = 1,

      /// <summary>The result is a downloaded text file.</summary>
      File = 2
   }

   /// <summary>The operation proposed by an item data change.</summary>
   public enum TestItemOperation
   {
      /// <summary>A new item.</summary>
      Create = 1,

      /// <summary>Changes an existing item.</summary>
      Update = 2,

      /// <summary>Deletes the item.</summary>
      Delete = 3
   }
}
