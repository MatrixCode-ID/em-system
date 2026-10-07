using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Test.Models
{
   /// <summary>
   /// The contract of the test module. Its content deliberately covers every form of action known to the
   /// engine - table and view CRUD, DTO-shaped queries, stream parameters, claims per action, public
   /// actions, time limits, status failures, business tasks, and approval - so one module is enough to test
   /// them end to end.
   /// </summary>
   public interface ITestServices : IServices
   {
      /// <summary>The module name in the route and in claims.</summary>
      const string ModuleName = "test";

      /// <summary>The base claim of the module: opening the test screens and calling actions that have no claim of their own.</summary>
      const string RunClaim = "Run Tests";

      /// <summary>The claim to add and change test items and documents.</summary>
      const string EditItemsClaim = "Edit Items";

      /// <summary>The claim to delete test items and documents.</summary>
      const string DeleteItemsClaim = "Delete Items";

      /// <summary>The claim for probe actions that are deliberately narrower than module access.</summary>
      const string ProbeClaim = "Run Probes";

      /// <summary>The claim that grants the right to approve item change proposals and save them directly.</summary>
      const string ApproveItemClaim = "Approve Item Change";

      /// <summary>The approval document type for test documents.</summary>
      const string DocType = "EmTestDoc";

      /// <summary>The approval document type for item change proposals.</summary>
      const string ItemDocType = "EmTestItem";

      /// <summary>The prefix of test business task keys.</summary>
      const string TaskKeyPrefix = "test.task.";

      #region Tables

      #region TestItem

      Task<ta_TestItem?> GetTa_TestItem_ById(string cTestItemId);

      Task<ta_TestItem[]> GetTa_TestItems();

      Task<int> GetTa_TestItems_Count();

      Task<ta_TestItem[]> GetTa_TestItems_InPage(int page, int pageSize);

      Task PostTa_TestItem_New(ta_TestItem data);

      Task PostTa_TestItem_NewBatch(ta_TestItem[] datas);

      Task PostTa_TestItem_Update(ta_TestItem data);

      Task PostTa_TestItem_UpdateBatch(ta_TestItem[] datas);

      Task PostTa_TestItem_Delete(ta_TestItem data);

      Task PostTa_TestItem_DeleteBatch(ta_TestItem[] datas);

      #endregion

      #region TestDoc

      Task<ta_TestDoc?> GetTa_TestDoc_ById(string cTestDocId);

      Task PostTa_TestDoc_New(ta_TestDoc data);

      Task PostTa_TestDoc_Update(ta_TestDoc data);

      Task PostTa_TestDoc_Delete(ta_TestDoc data);

      #endregion

      #endregion

      #region Views

      #region vi_TestItem

      Task<vi_TestItem?> GetVi_TestItem_ById(string cTestItemId);

      Task<vi_TestItem[]> GetVi_TestItems();

      Task<int> GetVi_TestItems_Count();

      Task<vi_TestItem[]> GetVi_TestItems_InPage(int page, int pageSize);

      /// <summary>A search with one query parameter per condition: GET carries no object or JSON.</summary>
      Task<TestItemPage> GetVi_TestItems_Search(string? search, TestItemState? state, int page, int pageSize);

      #endregion

      #region vi_TestDoc

      Task<vi_TestDoc?> GetVi_TestDoc_ById(string cTestDocId);

      Task<vi_TestDoc[]> GetVi_TestDocs_InPage(int page, int pageSize);

      Task<int> GetVi_TestDocs_Count();

      #endregion

      #region Meta's

      /// <summary>A public action: called with no token at all.</summary>
      Task<string> GetMeta_TestPublicPing();

      Task<string> GetMeta_TestPing();

      Task<TestSessionInfo> GetMeta_TestSession();

      /// <summary>An echo with simple typed parameters, each one query parameter.</summary>
      Task<TestEchoResult> GetMeta_TestEchoSimple(string text, int number, decimal amount, bool flag, DateTime when,
         TestItemState state);

      /// <summary>An echo through POST: a DTO and an array as positional arguments in the body.</summary>
      Task<TestEchoResult> PostGetMeta_TestEcho(TestEchoRequest request, string[] tags);

      /// <summary>Answers with the requested status (400, 403, 404, 409, 500) to test failure handling.</summary>
      Task<string> GetMeta_TestFail(int status);

      /// <summary>Waits some seconds; this action's time limit is five seconds, so anything longer is cancelled by the server.</summary>
      Task<string> GetMeta_TestSlow(int seconds);

      /// <summary>Waits some seconds with no server time limit; only the caller disconnecting cancels it.</summary>
      Task<string> GetMeta_TestSlowUnlimited(int seconds);

      /// <summary>Only holders of the probe claim, or an administrator.</summary>
      Task<string> GetMeta_TestClaimGated();

      Task<string> GetMeta_TestAdminOnly();

      Task<string> GetMeta_TestSelfOrAdmin(string cUserId);

      /// <summary>Receives the raw stream content and answers with what arrived: its length and SHA-256.</summary>
      Task<TestStreamResult> PostGetMeta_TestStreamUpload(TestStreamRequest request, Stream content);

      /// <summary>Sends a stream of deterministic bytes of the requested size (in KB).</summary>
      Task<Stream> GetMeta_TestStreamDownload(int kilobytes);

      /// <summary>A sample PDF with the requested number of pages, for the PDF viewer.</summary>
      Task<Stream> GetMeta_TestPdfSample(int pages);

      /// <summary>Starts a test business task and returns right away.</summary>
      Task<BusinessTaskInfo> PostGetMeta_TestStartTask(TestTaskRequest request);

      /// <summary>The global test task that still exists, live or failed and not yet cleared.</summary>
      Task<BusinessTaskInfo[]> GetMeta_TestGlobalTasks();

      Task PostMeta_TestGlobalTaskCancel(string key);

      Task PostMeta_TestGlobalTaskClear(string key);

      /// <summary>Inserts ten sample items that do not exist yet, as material for paging tests.</summary>
      Task<int> PostGetMeta_TestSeedItems();

      /// <summary>Submits an item change proposal through data approval.</summary>
      Task<TestSubmitResult> PostGetMeta_TestItemSubmitChange(TestItemChange change);

      /// <summary>Submits a test document through document approval; returns its request id.</summary>
      Task<string> PostGetMeta_TestDocSubmit(string cTestDocId, string? note);

      /// <summary>The base PDF of a test document (without stamps), for the Source document button and the viewer.</summary>
      Task<Stream> GetMeta_TestDocPdf(string cTestDocId);

      #endregion

      #endregion
   }
}
