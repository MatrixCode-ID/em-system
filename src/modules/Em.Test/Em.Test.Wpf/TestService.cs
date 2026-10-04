using System.IO;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Test.Models;
using Em.Ui.Wpf.Core;

namespace Em.Test.Wpf
{
   /// <summary>Client WPF module uji: setiap method memanggil action server yang namanya sama.</summary>
   [Module(ITestServices.ModuleName)]
   public class TestService(EmApp emApp) : ServiceWpfBase(emApp), ITestServices
   {
      #region Tables

      #region TestItem

      public Task<ta_TestItem?> GetTa_TestItem_ById(string cTestItemId) =>
         GetAsync<ta_TestItem?>(nameof(GetTa_TestItem_ById), cTestItemId);

      public Task<ta_TestItem[]> GetTa_TestItems() => GetAsync<ta_TestItem[]>(nameof(GetTa_TestItems));

      public Task<int> GetTa_TestItems_Count() => GetAsync<int>(nameof(GetTa_TestItems_Count));

      public Task<ta_TestItem[]> GetTa_TestItems_InPage(int page, int pageSize) =>
         GetAsync<ta_TestItem[]>(nameof(GetTa_TestItems_InPage), page, pageSize);

      public Task PostTa_TestItem_New(ta_TestItem data) => PostAsync(nameof(PostTa_TestItem_New), data);

      public Task PostTa_TestItem_NewBatch(ta_TestItem[] datas) => PostAsync(nameof(PostTa_TestItem_NewBatch), datas);

      public Task PostTa_TestItem_Update(ta_TestItem data) => PostAsync(nameof(PostTa_TestItem_Update), data);

      public Task PostTa_TestItem_UpdateBatch(ta_TestItem[] datas) =>
         PostAsync(nameof(PostTa_TestItem_UpdateBatch), datas);

      public Task PostTa_TestItem_Delete(ta_TestItem data) => PostAsync(nameof(PostTa_TestItem_Delete), data);

      public Task PostTa_TestItem_DeleteBatch(ta_TestItem[] datas) =>
         PostAsync(nameof(PostTa_TestItem_DeleteBatch), datas);

      #endregion

      #region TestDoc

      public Task<ta_TestDoc?> GetTa_TestDoc_ById(string cTestDocId) =>
         GetAsync<ta_TestDoc?>(nameof(GetTa_TestDoc_ById), cTestDocId);

      public Task PostTa_TestDoc_New(ta_TestDoc data) => PostAsync(nameof(PostTa_TestDoc_New), data);

      public Task PostTa_TestDoc_Update(ta_TestDoc data) => PostAsync(nameof(PostTa_TestDoc_Update), data);

      public Task PostTa_TestDoc_Delete(ta_TestDoc data) => PostAsync(nameof(PostTa_TestDoc_Delete), data);

      #endregion

      #endregion

      #region Views

      #region vi_TestItem

      public Task<vi_TestItem?> GetVi_TestItem_ById(string cTestItemId) =>
         GetAsync<vi_TestItem?>(nameof(GetVi_TestItem_ById), cTestItemId);

      public Task<vi_TestItem[]> GetVi_TestItems() => GetAsync<vi_TestItem[]>(nameof(GetVi_TestItems));

      public Task<int> GetVi_TestItems_Count() => GetAsync<int>(nameof(GetVi_TestItems_Count));

      public Task<vi_TestItem[]> GetVi_TestItems_InPage(int page, int pageSize) =>
         GetAsync<vi_TestItem[]>(nameof(GetVi_TestItems_InPage), page, pageSize);

      public Task<TestItemPage> GetVi_TestItems_Search(TestItemQuery query) =>
         GetAsync<TestItemPage>(nameof(GetVi_TestItems_Search), query);

      #endregion

      #region vi_TestDoc

      public Task<vi_TestDoc?> GetVi_TestDoc_ById(string cTestDocId) =>
         GetAsync<vi_TestDoc?>(nameof(GetVi_TestDoc_ById), cTestDocId);

      public Task<vi_TestDoc[]> GetVi_TestDocs_InPage(int page, int pageSize) =>
         GetAsync<vi_TestDoc[]>(nameof(GetVi_TestDocs_InPage), page, pageSize);

      public Task<int> GetVi_TestDocs_Count() => GetAsync<int>(nameof(GetVi_TestDocs_Count));

      #endregion

      #region Meta's

      public Task<string> GetMeta_TestPublicPing() => GetAsync<string>(nameof(GetMeta_TestPublicPing));

      public Task<string> GetMeta_TestPing() => GetAsync<string>(nameof(GetMeta_TestPing));

      public Task<TestSessionInfo> GetMeta_TestSession() => GetAsync<TestSessionInfo>(nameof(GetMeta_TestSession));

      public Task<TestEchoResult> GetMeta_TestEchoSimple(string text, int number, decimal amount, bool flag,
         DateTime when, TestItemState state) =>
         GetAsync<TestEchoResult>(nameof(GetMeta_TestEchoSimple), text, number, amount, flag, when, state);

      public Task<TestEchoResult> GetMeta_TestEcho(TestEchoRequest request) =>
         GetAsync<TestEchoResult>(nameof(GetMeta_TestEcho), request);

      public Task<TestEchoResult> PostGetMeta_TestEcho(TestEchoRequest request, string[] tags) =>
         PostAsync<TestEchoResult>(nameof(PostGetMeta_TestEcho), request, tags);

      public Task<string> GetMeta_TestFail(int status) => GetAsync<string>(nameof(GetMeta_TestFail), status);

      public Task<string> GetMeta_TestSlow(int seconds) => GetAsync<string>(nameof(GetMeta_TestSlow), seconds);

      public Task<string> GetMeta_TestSlowUnlimited(int seconds) =>
         GetAsync<string>(nameof(GetMeta_TestSlowUnlimited), seconds);

      public Task<string> GetMeta_TestClaimGated() => GetAsync<string>(nameof(GetMeta_TestClaimGated));

      public Task<string> GetMeta_TestAdminOnly() => GetAsync<string>(nameof(GetMeta_TestAdminOnly));

      public Task<string> GetMeta_TestSelfOrAdmin(string cUserId) =>
         GetAsync<string>(nameof(GetMeta_TestSelfOrAdmin), cUserId);

      public Task<TestStreamResult> PostGetMeta_TestStreamUpload(TestStreamRequest request, Stream content) =>
         PostStreamAsync<TestStreamResult>(nameof(PostGetMeta_TestStreamUpload), content, request);

      public Task<Stream> GetMeta_TestStreamDownload(int kilobytes) =>
         GetStreamAsync(nameof(GetMeta_TestStreamDownload), kilobytes);

      public Task<Stream> GetMeta_TestPdfSample(int pages) => GetStreamAsync(nameof(GetMeta_TestPdfSample), pages);

      public Task<BusinessTaskInfo> PostGetMeta_TestStartTask(TestTaskRequest request) =>
         PostAsync<BusinessTaskInfo>(nameof(PostGetMeta_TestStartTask), request);

      public Task<BusinessTaskInfo[]> GetMeta_TestGlobalTasks() =>
         GetAsync<BusinessTaskInfo[]>(nameof(GetMeta_TestGlobalTasks));

      public Task PostMeta_TestGlobalTaskCancel(string key) => PostAsync(nameof(PostMeta_TestGlobalTaskCancel), key);

      public Task PostMeta_TestGlobalTaskClear(string key) => PostAsync(nameof(PostMeta_TestGlobalTaskClear), key);

      public Task<int> PostGetMeta_TestSeedItems() => PostAsync<int>(nameof(PostGetMeta_TestSeedItems));

      public Task<TestSubmitResult> PostGetMeta_TestItemSubmitChange(TestItemChange change) =>
         PostAsync<TestSubmitResult>(nameof(PostGetMeta_TestItemSubmitChange), change);

      public Task<string> PostGetMeta_TestDocSubmit(string cTestDocId, string? note) =>
         PostAsync<string>(nameof(PostGetMeta_TestDocSubmit), cTestDocId, note!);

      public Task<Stream> GetMeta_TestDocPdf(string cTestDocId) =>
         GetStreamAsync(nameof(GetMeta_TestDocPdf), cTestDocId);

      #endregion

      #endregion
   }
}
