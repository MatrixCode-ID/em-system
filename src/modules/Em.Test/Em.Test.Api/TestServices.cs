using Em.Api.Core;
using Em.Shared;
using Em.Test.Models;
using Microsoft.EntityFrameworkCore;

namespace Em.Test.Api
{
   /// <summary>
   /// The server implementation of the test module. Split by topic across several partial files: data
   /// (this file), engine probes, business tasks, and approval.
   /// </summary>
   [Module(ITestServices.ModuleName)]
   public partial class TestServices(TestDbContext ctx) : ServicesBase, ITestServices
   {
      private const int MaxPageSize = 200;

      #region Tables

      #region TestItem

      [GetAction]
      public Task<ta_TestItem?> GetTa_TestItem_ById(string cTestItemId) =>
         ctx.ta_TestItems.Where(r => r.cTestItemId == cTestItemId).SingleOrDefaultAsync(AbortToken);

      [GetAction]
      public Task<ta_TestItem[]> GetTa_TestItems() =>
         ctx.ta_TestItems.OrderBy(r => r.cTestItemCode).ToArrayAsync(AbortToken);

      [GetAction]
      public Task<int> GetTa_TestItems_Count() => ctx.ta_TestItems.CountAsync(AbortToken);

      [GetAction]
      public Task<ta_TestItem[]> GetTa_TestItems_InPage(int page, int pageSize) {
         NormalizePaging(ref page, ref pageSize);
         return ctx.ta_TestItems.OrderBy(r => r.cTestItemCode)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(AbortToken);
      }

      [PostAction(claim: ITestServices.EditItemsClaim)]
      public async Task PostTa_TestItem_New(ta_TestItem data) {
         PrepareNewItem(data);
         await EnsureItemCodeFreeAsync(data.cTestItemCode, data.cTestItemId);
         ctx.ta_TestItems.Add(data);
         await ctx.SaveChangesAsync();
      }

      [PostAction(claim: ITestServices.EditItemsClaim)]
      public async Task PostTa_TestItem_NewBatch(ta_TestItem[] datas) {
         if (datas.Length == 0) return;
         foreach (var data in datas) PrepareNewItem(data);
         EnsureDistinctCodes(datas);
         foreach (var data in datas) await EnsureItemCodeFreeAsync(data.cTestItemCode, data.cTestItemId);

         ctx.ta_TestItems.AddRange(datas);
         await ctx.SaveChangesAsync();
      }

      [PostAction(claim: ITestServices.EditItemsClaim)]
      public async Task PostTa_TestItem_Update(ta_TestItem data) {
         ValidateItem(data);
         await EnsureItemCodeFreeAsync(data.cTestItemCode, data.cTestItemId);
         data.ustamp = DateTime.UtcNow;
         ctx.UpdateRow(data);
         await SaveOrNotFoundAsync("Item");
      }

      [PostAction(claim: ITestServices.EditItemsClaim)]
      public async Task PostTa_TestItem_UpdateBatch(ta_TestItem[] datas) {
         if (datas.Length == 0) return;
         EnsureDistinctCodes(datas);
         foreach (var data in datas) {
            ValidateItem(data);
            await EnsureItemCodeFreeAsync(data.cTestItemCode, data.cTestItemId);
            data.ustamp = DateTime.UtcNow;
            ctx.UpdateRow(data);
         }

         await SaveOrNotFoundAsync("Item");
      }

      [PostAction(claim: ITestServices.DeleteItemsClaim)]
      public async Task PostTa_TestItem_Delete(ta_TestItem data) {
         ctx.DeleteRow(data);
         await SaveOrNotFoundAsync("Item");
      }

      [PostAction(claim: ITestServices.DeleteItemsClaim)]
      public async Task PostTa_TestItem_DeleteBatch(ta_TestItem[] datas) {
         if (datas.Length == 0) return;
         foreach (var data in datas) ctx.DeleteRow(data);
         await SaveOrNotFoundAsync("Item");
      }

      #endregion

      #region TestDoc

      [GetAction]
      public Task<ta_TestDoc?> GetTa_TestDoc_ById(string cTestDocId) =>
         ctx.ta_TestDocs.Where(r => r.cTestDocId == cTestDocId).SingleOrDefaultAsync(AbortToken);

      [PostAction(claim: ITestServices.EditItemsClaim)]
      public async Task PostTa_TestDoc_New(ta_TestDoc data) {
         if (string.IsNullOrWhiteSpace(data.cTestDocId)) data.cTestDocId = $"{Ulid.NewUlid()}";
         ValidateDoc(data);
         data.cTestDocStatus = TestDocStatus.Draft;
         data.cTestDocQaPassed = null;
         data.cTestDocQaRemarks = null;
         data.datestamp = data.ustamp = DateTime.UtcNow;

         if (await ctx.ta_TestDocs.AnyAsync(r => r.cTestDocNo == data.cTestDocNo)) {
            throw new ActionException($"Document number '{data.cTestDocNo}' is already used.", 409);
         }

         ctx.ta_TestDocs.Add(data);
         await ctx.SaveChangesAsync();
      }

      [PostAction(claim: ITestServices.EditItemsClaim)]
      public async Task PostTa_TestDoc_Update(ta_TestDoc data) {
         ValidateDoc(data);

         // Only the draft is editable: a document in approval is what the signers are looking at, and
         // changing it underneath them would make their signatures cover something else.
         var current = await ctx.ta_TestDocs.Where(r => r.cTestDocId == data.cTestDocId)
            .Select(r => new { r.cTestDocStatus, r.datestamp }).SingleOrDefaultAsync();
         if (current is null) throw new ActionException("Document was not found.", 404);
         if (current.cTestDocStatus != TestDocStatus.Draft) {
            throw new ActionException("Only a draft document can be edited.", 409);
         }

         // Status and QA outcome belong to the approval flow, never to the caller.
         data.cTestDocStatus = TestDocStatus.Draft;
         data.cTestDocQaPassed = null;
         data.cTestDocQaRemarks = null;
         data.datestamp = current.datestamp;
         data.ustamp = DateTime.UtcNow;
         ctx.UpdateRow(data);
         await SaveOrNotFoundAsync("Document");
      }

      [PostAction(claim: ITestServices.DeleteItemsClaim)]
      public async Task PostTa_TestDoc_Delete(ta_TestDoc data) {
         var status = await ctx.ta_TestDocs.Where(r => r.cTestDocId == data.cTestDocId)
            .Select(r => (TestDocStatus?)r.cTestDocStatus).SingleOrDefaultAsync();
         if (status is null) throw new ActionException("Document was not found.", 404);
         if (status == TestDocStatus.InApproval) {
            throw new ActionException("A document in approval cannot be deleted. Withdraw the request first.", 409);
         }

         ctx.DeleteRow(data);
         await SaveOrNotFoundAsync("Document");
      }

      #endregion

      #endregion

      #region Views

      #region vi_TestItem

      [GetAction]
      public Task<vi_TestItem?> GetVi_TestItem_ById(string cTestItemId) =>
         ctx.vi_TestItems.Where(r => r.cTestItemId == cTestItemId).SingleOrDefaultAsync(AbortToken);

      [GetAction]
      public Task<vi_TestItem[]> GetVi_TestItems() =>
         ctx.vi_TestItems.OrderBy(r => r.cTestItemCode).ToArrayAsync(AbortToken);

      [GetAction]
      public Task<int> GetVi_TestItems_Count() => ctx.vi_TestItems.CountAsync(AbortToken);

      [GetAction]
      public Task<vi_TestItem[]> GetVi_TestItems_InPage(int page, int pageSize) {
         NormalizePaging(ref page, ref pageSize);
         return ctx.vi_TestItems.OrderBy(r => r.cTestItemCode)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(AbortToken);
      }

      [GetAction]
      public async Task<TestItemPage> GetVi_TestItems_Search(string? search, TestItemState? state, int page, int pageSize) {
         NormalizePaging(ref page, ref pageSize);

         var rows = ctx.vi_TestItems.AsQueryable();
         if (!string.IsNullOrWhiteSpace(search)) {
            var term = search.Trim();
            rows = rows.Where(r => r.cTestItemCode.Contains(term) || r.cTestItemName.Contains(term));
         }

         if (state is { } wanted) rows = rows.Where(r => r.cTestItemState == wanted);

         var total = await rows.CountAsync(AbortToken);
         var items = await rows.OrderBy(r => r.cTestItemCode)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(AbortToken);
         return new TestItemPage { Items = items, Total = total, Page = page, PageSize = pageSize };
      }

      #endregion

      #region vi_TestDoc

      [GetAction]
      public Task<vi_TestDoc?> GetVi_TestDoc_ById(string cTestDocId) =>
         ctx.vi_TestDocs.Where(r => r.cTestDocId == cTestDocId).SingleOrDefaultAsync(AbortToken);

      [GetAction]
      public Task<vi_TestDoc[]> GetVi_TestDocs_InPage(int page, int pageSize) {
         NormalizePaging(ref page, ref pageSize);
         return ctx.vi_TestDocs.OrderByDescending(r => r.datestamp).ThenBy(r => r.cTestDocNo)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(AbortToken);
      }

      [GetAction]
      public Task<int> GetVi_TestDocs_Count() => ctx.vi_TestDocs.CountAsync(AbortToken);

      #endregion

      #endregion

      #region Helpers

      private static void NormalizePaging(ref int page, ref int pageSize) {
         if (page < 1) page = 1;
         if (pageSize < 1) pageSize = 10;
         if (pageSize > MaxPageSize) pageSize = MaxPageSize;
      }

      private static void PrepareNewItem(ta_TestItem data) {
         if (string.IsNullOrWhiteSpace(data.cTestItemId)) data.cTestItemId = $"{Ulid.NewUlid()}";
         ValidateItem(data);
         data.datestamp = data.ustamp = DateTime.UtcNow;
      }

      private static void ValidateItem(ta_TestItem data) {
         data.cTestItemCode = (data.cTestItemCode ?? string.Empty).Trim();
         data.cTestItemName = (data.cTestItemName ?? string.Empty).Trim();

         if (data.cTestItemCode.Length is 0 or > 30) {
            throw new ActionException("The item code must be 1 to 30 characters.", 400);
         }

         if (data.cTestItemName.Length is 0 or > 100) {
            throw new ActionException("The item name must be 1 to 100 characters.", 400);
         }

         if (data.cTestItemQty < 0) throw new ActionException("The quantity must not be negative.", 400);
         if (data.cTestItemPrice < 0) throw new ActionException("The price must not be negative.", 400);
         if (!Enum.IsDefined(data.cTestItemState)) throw new ActionException("The item state is not known.", 400);
         if (data.cTestItemNote?.Length > 500) throw new ActionException("The note is limited to 500 characters.", 400);
      }

      private static void EnsureDistinctCodes(IEnumerable<ta_TestItem> datas) {
         var duplicate = datas.GroupBy(r => r.cTestItemCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(r => r.Count() > 1);
         if (duplicate is not null) {
            throw new ActionException($"The code '{duplicate.Key}' appears more than once in the batch.", 409);
         }
      }

      private async Task EnsureItemCodeFreeAsync(string code, string id) {
         if (await ctx.ta_TestItems.AnyAsync(r => r.cTestItemCode == code && r.cTestItemId != id)) {
            throw new ActionException($"The item code '{code}' is already used.", 409);
         }
      }

      private static void ValidateDoc(ta_TestDoc data) {
         data.cTestDocNo = (data.cTestDocNo ?? string.Empty).Trim();
         data.cTestDocTitle = (data.cTestDocTitle ?? string.Empty).Trim();

         if (data.cTestDocNo.Length is 0 or > 30) {
            throw new ActionException("The document number must be 1 to 30 characters.", 400);
         }

         if (data.cTestDocTitle.Length is 0 or > 100) {
            throw new ActionException("The document title must be 1 to 100 characters.", 400);
         }

         if (data.cTestDocAmount < 0) throw new ActionException("The amount must not be negative.", 400);
      }

      // A row that is gone between the screen and the save surfaces as a concurrency error; to the caller
      // it is simply a missing row, which is what 404 says.
      private async Task SaveOrNotFoundAsync(string what) {
         try {
            await ctx.SaveChangesAsync();
         }
         catch (DbUpdateConcurrencyException) {
            throw new ActionException($"{what} was not found, or was changed by someone else.", 404);
         }
      }

      #endregion
   }
}
