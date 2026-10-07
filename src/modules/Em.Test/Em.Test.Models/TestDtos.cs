namespace Em.Test.Models
{
   /// <summary>The content of the echo test with various parameter types, so query and body binding can be compared.</summary>
   public class TestEchoRequest
   {
      public string Text { get; set; } = string.Empty;

      public int Number { get; set; }

      public decimal Amount { get; set; }

      public bool Flag { get; set; }

      public DateTime When { get; set; }

      public TestItemState State { get; set; }

      public Guid Token { get; set; }

      public string[] Tags { get; set; } = [];
   }

   /// <summary>The answer of the echo test: the content the server received together with who the caller is.</summary>
   public class TestEchoResult
   {
      public TestEchoRequest Received { get; set; } = new();

      public string Via { get; set; } = string.Empty;

      public string? CallerAccount { get; set; }

      public DateTime ServerTimeUtc { get; set; }
   }

   /// <summary>The caller's state according to the server, used to compare with the state according to the client.</summary>
   public class TestSessionInfo
   {
      public string? UserId { get; set; }

      public string? Account { get; set; }

      public bool IsAdmin { get; set; }

      public bool IsDebugRequest { get; set; }

      public string Source { get; set; } = string.Empty;

      public string[] Claims { get; set; } = [];
   }

   /// <summary>The header payload for the test stream upload.</summary>
   public class TestStreamRequest
   {
      public string Name { get; set; } = string.Empty;
   }

   /// <summary>The result of the test stream upload: what really reached the server.</summary>
   public class TestStreamResult
   {
      public string Name { get; set; } = string.Empty;

      public long Length { get; set; }

      public string Sha256 { get; set; } = string.Empty;
   }

   /// <summary>A request to run a test business task.</summary>
   public class TestTaskRequest
   {
      /// <summary>The duration of the work in seconds; its progress is reported every second.</summary>
      public int Seconds { get; set; } = 10;

      /// <summary>When true, the work fails midway, to test the failed status.</summary>
      public bool Fail { get; set; }

      /// <summary>true = a global task belonging to the module screen; false = a personal task belonging to its starter.</summary>
      public bool Global { get; set; }

      public TestTaskOutput Output { get; set; }
   }

   /// <summary>The conditions of an item search together with its page.</summary>
   public class TestItemQuery
   {
      public int Page { get; set; } = 1;

      public int PageSize { get; set; } = 10;

      public string? Search { get; set; }

      public TestItemState? State { get; set; }
   }

   /// <summary>One page of item search results.</summary>
   public class TestItemPage
   {
      public vi_TestItem[] Items { get; set; } = [];

      public int Total { get; set; }

      public int Page { get; set; }

      public int PageSize { get; set; }
   }

   /// <summary>An item data change proposal submitted through data approval.</summary>
   public class TestItemChange
   {
      public TestItemOperation Operation { get; set; }

      /// <summary>The id of the item being changed or deleted; empty for a new item.</summary>
      public string? ItemId { get; set; }

      public string Code { get; set; } = string.Empty;

      public string Name { get; set; } = string.Empty;

      public int Qty { get; set; }

      public decimal Price { get; set; }

      public string? Note { get; set; }
   }

   /// <summary>The result of submitting a data change proposal.</summary>
   public class TestSubmitResult
   {
      public string ApprovalRequestId { get; set; } = string.Empty;

      /// <summary>true when the caller holds the approval claim, so the change is applied directly.</summary>
      public bool AppliedImmediately { get; set; }
   }

   /// <summary>The input of the QA Check step on a test document.</summary>
   public class TestQaPayload
   {
      public bool Passed { get; set; }

      public string? Remarks { get; set; }
   }
}
