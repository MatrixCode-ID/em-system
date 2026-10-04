using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Test.Models
{
   /// <summary>
   /// Kontrak module uji. Isinya sengaja mencakup setiap bentuk action yang dikenal engine - CRUD tabel dan
   /// view, query berbentuk DTO, parameter stream, claim per action, action publik, batas waktu, kegagalan
   /// berstatus, business task, dan approval - supaya satu module cukup untuk mengujinya dari ujung ke ujung.
   /// </summary>
   public interface ITestServices : IServices
   {
      /// <summary>Nama module di route dan di claim.</summary>
      const string ModuleName = "test";

      /// <summary>Claim dasar module: membuka layar uji dan memanggil action tanpa claim sendiri.</summary>
      const string RunClaim = "Run Tests";

      /// <summary>Claim untuk menambah dan mengubah item dan dokumen uji.</summary>
      const string EditItemsClaim = "Edit Items";

      /// <summary>Claim untuk menghapus item dan dokumen uji.</summary>
      const string DeleteItemsClaim = "Delete Items";

      /// <summary>Claim untuk action probe yang sengaja lebih sempit dari akses module.</summary>
      const string ProbeClaim = "Run Probes";

      /// <summary>Claim yang memberi hak menyetujui usulan perubahan item dan menyimpannya langsung.</summary>
      const string ApproveItemClaim = "Approve Item Change";

      /// <summary>Jenis dokumen approval untuk dokumen uji.</summary>
      const string DocType = "EmTestDoc";

      /// <summary>Jenis dokumen approval untuk usulan perubahan item.</summary>
      const string ItemDocType = "EmTestItem";

      /// <summary>Awalan kunci business task uji.</summary>
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

      /// <summary>Pencarian dengan syarat berbentuk DTO: parameter record/class berjalan sebagai JSON di query.</summary>
      Task<TestItemPage> GetVi_TestItems_Search(TestItemQuery query);

      #endregion

      #region vi_TestDoc

      Task<vi_TestDoc?> GetVi_TestDoc_ById(string cTestDocId);

      Task<vi_TestDoc[]> GetVi_TestDocs_InPage(int page, int pageSize);

      Task<int> GetVi_TestDocs_Count();

      #endregion

      #region Meta's

      /// <summary>Action publik: dipanggil tanpa token sama sekali.</summary>
      Task<string> GetMeta_TestPublicPing();

      Task<string> GetMeta_TestPing();

      Task<TestSessionInfo> GetMeta_TestSession();

      /// <summary>Echo dengan parameter bertipe sederhana, masing-masing satu parameter query.</summary>
      Task<TestEchoResult> GetMeta_TestEchoSimple(string text, int number, decimal amount, bool flag, DateTime when,
         TestItemState state);

      /// <summary>Echo dengan satu DTO yang berjalan sebagai JSON di query.</summary>
      Task<TestEchoResult> GetMeta_TestEcho(TestEchoRequest request);

      /// <summary>Echo lewat POST: DTO dan array sebagai argumen posisional di body.</summary>
      Task<TestEchoResult> PostGetMeta_TestEcho(TestEchoRequest request, string[] tags);

      /// <summary>Menjawab dengan status yang diminta (400, 403, 404, 409, 500) untuk menguji penanganan kegagalan.</summary>
      Task<string> GetMeta_TestFail(int status);

      /// <summary>Menunggu sekian detik; batas waktu action ini lima detik, jadi lebih dari itu dibatalkan server.</summary>
      Task<string> GetMeta_TestSlow(int seconds);

      /// <summary>Menunggu sekian detik tanpa batas waktu server; hanya putusnya pemanggil yang membatalkan.</summary>
      Task<string> GetMeta_TestSlowUnlimited(int seconds);

      /// <summary>Hanya pemegang claim probe, atau administrator.</summary>
      Task<string> GetMeta_TestClaimGated();

      Task<string> GetMeta_TestAdminOnly();

      Task<string> GetMeta_TestSelfOrAdmin(string cUserId);

      /// <summary>Menerima isi stream mentah dan menjawab apa yang sampai: panjang dan SHA-256.</summary>
      Task<TestStreamResult> PostGetMeta_TestStreamUpload(TestStreamRequest request, Stream content);

      /// <summary>Mengirim stream berisi byte deterministik sebesar yang diminta (dalam KB).</summary>
      Task<Stream> GetMeta_TestStreamDownload(int kilobytes);

      /// <summary>PDF contoh dengan jumlah halaman yang diminta, untuk viewer PDF.</summary>
      Task<Stream> GetMeta_TestPdfSample(int pages);

      /// <summary>Memulai business task uji dan langsung kembali.</summary>
      Task<BusinessTaskInfo> PostGetMeta_TestStartTask(TestTaskRequest request);

      /// <summary>Task uji global yang masih ada, hidup maupun gagal-belum-di-clear.</summary>
      Task<BusinessTaskInfo[]> GetMeta_TestGlobalTasks();

      Task PostMeta_TestGlobalTaskCancel(string key);

      Task PostMeta_TestGlobalTaskClear(string key);

      /// <summary>Menyisipkan sepuluh item contoh yang belum ada, untuk bahan uji paging.</summary>
      Task<int> PostGetMeta_TestSeedItems();

      /// <summary>Mengajukan usulan perubahan item lewat data approval.</summary>
      Task<TestSubmitResult> PostGetMeta_TestItemSubmitChange(TestItemChange change);

      /// <summary>Mengajukan dokumen uji lewat document approval; mengembalikan id request-nya.</summary>
      Task<string> PostGetMeta_TestDocSubmit(string cTestDocId, string? note);

      /// <summary>PDF dasar dokumen uji (tanpa stamp), untuk tombol Source document dan viewer.</summary>
      Task<Stream> GetMeta_TestDocPdf(string cTestDocId);

      #endregion

      #endregion
   }
}
