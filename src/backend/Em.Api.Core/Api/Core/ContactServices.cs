using System.Linq.Expressions;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   [Module("core.contact")]
   public class ContactServices(ApiCoreContext ctx) : ServicesBase, IContactServices
   {
      #region Tables

      #region ta_Contact

      [GetAction]
      public async Task<ta_Contact?> GetTa_Contact_ById(string cContactId) {
         var data = await ctx.ta_Contacts.Where(r => r.cContactId == cContactId)
            .SingleOrDefaultAsync(AbortToken);
         return data;
      }

      [GetAction]
      public async Task<ta_Contact[]> GetTa_Contacts() {
         var data = await ctx.ta_Contacts
            .ToArrayAsync(AbortToken);
         return data;
      }

      [GetAction]
      public Task<int> GetTa_Contacts_Count() => ctx.ta_Contacts.CountAsync(AbortToken);

      [GetAction]
      public async Task<ta_Contact[]> GetTa_Contacts_InPage(int page, int pageSize) {
         if (page < 1) page = 1;
         if (pageSize < 1) pageSize = 10;
         var data = await ctx.ta_Contacts
            // A page is only a slice of an order, and without one the server is free to
            // return the rows differently each time - which is how a row ends up on two
            // pages or on none. The key is unique, so the order it gives is total.
            .OrderBy(r => r.cContactId)
            .Skip((page - 1) * pageSize) // Skips the rows of prior pages
            .Take(pageSize)
            .ToArrayAsync(AbortToken);
         return data;
      }

      [PostAction]
      public async Task PostTa_Contact_New(ta_Contact data) {
         ctx.ta_Contacts.Add(data);
         await ctx.SaveChangesAsync();
      }

      [PostAction]
      public async Task PostTa_Contact_NewBatch(ta_Contact[] datas) {
         await ctx.BulkInsertAsync(datas);
      }

      [PostAction]
      public async Task PostTa_Contact_Update(ta_Contact data) {
         ctx.UpdateRow(data);
         await ctx.SaveChangesAsync();
      }

      [PostAction]
      public async Task PostTa_Contact_UpdateBatch(ta_Contact[] datas) {
         await ctx.BulkUpdateAsync(datas);
      }

      [PostAction]
      public async Task PostTa_Contact_Delete(ta_Contact data) {
         ctx.DeleteRow(data);
         await ctx.SaveChangesAsync();
      }

      [PostAction]
      public async Task PostTa_Contact_DeleteBatch(ta_Contact[] datas) {
         await ctx.BulkDeleteAsync(datas);
      }

      #endregion

      #region ta_Comm

      [GetAction]
      public async Task<ta_Comm?> GetTa_Comm_ById(string cCommId) {
         var data = await ctx.ta_Comms.Where(r => r.cCommId == cCommId)
            .SingleOrDefaultAsync(AbortToken);
         return data;
      }

      [GetAction]
      public async Task<ta_Comm[]> GetTa_Comms() {
         var data = await ctx.ta_Comms
            .ToArrayAsync(AbortToken);
         return data;
      }

      [GetAction]
      public Task<int> GetTa_Comms_Count() => ctx.ta_Comms.CountAsync(AbortToken);

      [GetAction]
      public async Task<ta_Comm[]> GetTa_Comms_ByContactId(string cContactId) {
         var data = await ctx.ta_Comms
            .Where(r => r.cContactId == cContactId)
            .ToArrayAsync(AbortToken);
         return data;
      }

      [GetAction]
      public async Task<ta_Comm[]> GetTa_Comms_InPage(int page, int pageSize) {
         if (page < 1) page = 1;
         if (pageSize < 1) pageSize = 10;
         var data = await ctx.ta_Comms
            // A page is only a slice of an order, and without one the server is free to
            // return the rows differently each time - which is how a row ends up on two
            // pages or on none. The key is unique, so the order it gives is total.
            .OrderBy(r => r.cCommId)
            .Skip((page - 1) * pageSize) // Skips the rows of prior pages
            .Take(pageSize)
            .ToArrayAsync(AbortToken);
         return data;
      }

      [PostAction]
      public async Task PostTa_Comm_New(ta_Comm data) {
         ctx.ta_Comms.Add(data);
         await ctx.SaveChangesAsync();
      }

      [PostAction]
      public async Task PostTa_Comm_NewBatch(ta_Comm[] datas) {
         await ctx.BulkInsertAsync(datas);
      }

      [PostAction]
      public async Task PostTa_Comm_Update(ta_Comm data) {
         ctx.UpdateRow(data);
         await ctx.SaveChangesAsync();
      }

      [PostAction]
      public async Task PostTa_Comm_UpdateBatch(ta_Comm[] datas) {
         await ctx.BulkUpdateAsync(datas);
      }

      [PostAction]
      public async Task PostTa_Comm_Delete(ta_Comm data) {
         ctx.DeleteRow(data);
         await ctx.SaveChangesAsync();
      }

      [PostAction]
      public async Task PostTa_Comm_DeleteBatch(ta_Comm[] datas) {
         await ctx.BulkDeleteAsync(datas);
      }

      #endregion

      #region ta_Address

      [GetAction]
      public async Task<ta_Address?> GetTa_Address_ById(string cAddressId) {
         var data = await ctx.ta_Addresses.Where(r => r.cAddressId == cAddressId)
            .SingleOrDefaultAsync(AbortToken);
         return data;
      }

      [GetAction]
      public async Task<ta_Address[]> GetTa_Addresses() {
         var data = await ctx.ta_Addresses
            .ToArrayAsync(AbortToken);
         return data;
      }

      [GetAction]
      public async Task<ta_Address[]> GetTa_Addresses_ByContactId(string cContactId) {
         var data = await ctx.ta_Addresses
            .Where(r => r.cContactId == cContactId)
            .ToArrayAsync(AbortToken);
         return data;
      }

      [GetAction]
      public Task<int> GetTa_Addresses_Count() => ctx.ta_Addresses.CountAsync(AbortToken);

      [GetAction]
      public async Task<ta_Address[]> GetTa_Addresses_InPage(int page, int pageSize) {
         if (page < 1) page = 1;
         if (pageSize < 1) pageSize = 10;
         var data = await ctx.ta_Addresses
            // A page is only a slice of an order, and without one the server is free to
            // return the rows differently each time - which is how a row ends up on two
            // pages or on none. The key is unique, so the order it gives is total.
            .OrderBy(r => r.cAddressId)
            .Skip((page - 1) * pageSize) // Skips the rows of prior pages
            .Take(pageSize)
            .ToArrayAsync(AbortToken);
         return data;
      }

      [PostAction]
      public async Task PostTa_Address_New(ta_Address data) {
         ctx.ta_Addresses.Add(data);
         await ctx.SaveChangesAsync();
      }

      [PostAction]
      public async Task PostTa_Address_NewBatch(ta_Address[] datas) {
         await ctx.BulkInsertAsync(datas);
      }

      [PostAction]
      public async Task PostTa_Address_Update(ta_Address data) {
         ctx.UpdateRow(data);
         await ctx.SaveChangesAsync();
      }

      [PostAction]
      public async Task PostTa_Address_UpdateBatch(ta_Address[] datas) {
         await ctx.BulkUpdateAsync(datas);
      }

      [PostAction]
      public async Task PostTa_Address_Delete(ta_Address data) {
         ctx.DeleteRow(data);
         await ctx.SaveChangesAsync();
      }

      [PostAction]
      public async Task PostTa_Address_DeleteBatch(ta_Address[] datas) {
         await ctx.BulkDeleteAsync(datas);
      }

      #endregion

      #endregion

      #region Views

      #region vi_Contact

      [GetAction]
      public async Task<vi_Contact?> GetVi_Contact_ById(string cContactId) {
         var data = await ctx.vi_Contacts.Where(r => r.cContactId == cContactId)
            .SingleOrDefaultAsync(AbortToken);
         return data;
      }

      [GetAction]
      public async Task<vi_Contact[]> GetVi_Contacts() {
         var data = await ctx.vi_Contacts
            .ToArrayAsync(AbortToken);
         return data;
      }

      [GetAction]
      public async Task<vi_Contact[]> GetVi_Contacts_InPage(int page, int pageSize) {
         if (page < 1) page = 1;
         if (pageSize < 1) pageSize = 10;
         var data = await ctx.vi_Contacts
            // A page is only a slice of an order, and without one the server is free to
            // return the rows differently each time - which is how a row ends up on two
            // pages or on none. The key is unique, so the order it gives is total.
            .OrderBy(r => r.cContactId)
            .Skip((page - 1) * pageSize) // Skips the rows of prior pages
            .Take(pageSize)
            .ToArrayAsync(AbortToken);
         return data;
      }

      [GetAction]
      public async Task<vi_Contact[]> GetVi_Contacts_Search(ContactSearchType searchType, string searchTerm,
         int maxResults) {
         if (maxResults < 1) maxResults = 200;
         var tokens = Helper.BuildSearchTokens(searchTerm);
         // Without a usable token nothing can match, so the database is never touched at all.
         if (tokens.Length == 0) return [];

         var query = ctx.vi_Contacts.AsNoTracking();

         // Every token must be found, but each one may sit in any of the searched columns: AND between
         // tokens, OR between columns. Chaining Where() once per token builds exactly that shape, and
         // the whole predicate still leaves as a single SQL statement.
         foreach (var token in tokens) {
            var pattern = $"%{token}%";
            query = searchType switch {
               ContactSearchType.ByNameSearch =>
                  query.Where(r => EF.Functions.Like(r.cContactFullName, pattern)),
               ContactSearchType.ByAddressSearch =>
                  query.Where(r => EF.Functions.Like(r.cAddressLocation!, pattern)),
               _ => query.Where(r => EF.Functions.Like(r.cContactFullName, pattern)
                                     || EF.Functions.Like(r.cContactNote!, pattern)
                                     || EF.Functions.Like(r.cAddressName!, pattern)
                                     || EF.Functions.Like(r.cAddressLocation!, pattern)
                                     || EF.Functions.Like(r.cAddressZip!, pattern)
                                     || EF.Functions.Like(r.cCommValue!, pattern)
                                     || EF.Functions.Like(r.cCommNote!, pattern))
            };
         }

         // Ranking runs on the column the search type is about, so the rows Take() keeps are the most
         // relevant ones rather than an arbitrary slice. A NULL column just falls into the last
         // bucket, because LIKE against NULL is never true.
         var term = string.Join(' ', tokens);
         Expression<Func<vi_Contact, int>> rank = searchType == ContactSearchType.ByAddressSearch
            ? r => r.cAddressLocation == term ? 0
               : r.cAddressLocation!.StartsWith(term) ? 1
               : r.cAddressLocation!.Contains(term) ? 2
               : 3
            : r => r.cContactFullName == term ? 0
               : r.cContactFullName.StartsWith(term) ? 1
               : r.cContactFullName.Contains(term) ? 2
               : 3;

         var data = await query
            .OrderBy(rank)
            .ThenBy(r => r.cContactFullName)
            .Take(maxResults) // Applied by the server, so only the rows that are kept travel back
            .ToArrayAsync(AbortToken);
         return data;
      }

      #endregion

      #region vi_Address

      [GetAction]
      public async Task<vi_Address?> GetVi_Address_ById(string cAddressId) {
         var data = await ctx.vi_Addresses.Where(r => r.cAddressId == cAddressId)
            .SingleOrDefaultAsync(AbortToken);
         return data;
      }

      [GetAction]
      public async Task<vi_Address[]> GetVi_Addresses() {
         var data = await ctx.vi_Addresses
            .ToArrayAsync(AbortToken);
         return data;
      }

      [GetAction]
      public async Task<vi_Address[]> GetVi_Addresses_InPage(int page, int pageSize) {
         if (page < 1) page = 1;
         if (pageSize < 1) pageSize = 10;
         var data = await ctx.vi_Addresses
            // A page is only a slice of an order, and without one the server is free to
            // return the rows differently each time - which is how a row ends up on two
            // pages or on none. The key is unique, so the order it gives is total.
            .OrderBy(r => r.cAddressId)
            .Skip((page - 1) * pageSize) // Skips the rows of prior pages
            .Take(pageSize)
            .ToArrayAsync(AbortToken);
         return data;
      }

      [GetAction]
      public async Task<vi_Address[]> GetVi_Addresses_ByContactId(string cContactId) {
         var data = await ctx.vi_Addresses
            .Where(r => r.cContactId == cContactId)
            .ToArrayAsync(AbortToken);
         return data;
      }

      #endregion

      #region vi_Comm

      [GetAction]
      public async Task<vi_Comm?> GetVi_Comm_ById(string cCommId) {
         var data = await ctx.vi_Comms.Where(r => r.cCommId == cCommId)
            .SingleOrDefaultAsync(AbortToken);
         return data;
      }

      [GetAction]
      public async Task<vi_Comm[]> GetVi_Comms() {
         var data = await ctx.vi_Comms
            .ToArrayAsync(AbortToken);
         return data;
      }

      [GetAction]
      public async Task<vi_Comm[]> GetVi_Comms_InPage(int page, int pageSize) {
         if (page < 1) page = 1;
         if (pageSize < 1) pageSize = 10;
         var data = await ctx.vi_Comms
            // A page is only a slice of an order, and without one the server is free to
            // return the rows differently each time - which is how a row ends up on two
            // pages or on none. The key is unique, so the order it gives is total.
            .OrderBy(r => r.cCommId)
            .Skip((page - 1) * pageSize) // Skips the rows of prior pages
            .Take(pageSize)
            .ToArrayAsync(AbortToken);
         return data;
      }

      [GetAction]
      public async Task<vi_Comm[]> GetVi_Comms_ByContactId(string cContactId) {
         var data = await ctx.vi_Comms
            .Where(r => r.cContactId == cContactId)
            .ToArrayAsync(AbortToken);
         return data;
      }

      #endregion

      #endregion
   }
}