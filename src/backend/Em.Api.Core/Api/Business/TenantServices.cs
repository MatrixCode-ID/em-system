using System.Data;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core;

/// <summary>Tenant actions of the Business Module Manager.</summary>
/// <remarks>
/// Field content is taken as the form sends it; the database keeps codes unique. The checks made here are the
/// ones a constraint cannot make: the parent must exist and must not create a cycle, and an update must be based
/// on the latest saved row.
/// </remarks>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantServices(ApiCoreContext ctx) : ServicesBase, ITenantServices
{
   #region Views

   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public Task<vi_Tenant[]> GetVi_Tenants() =>
      ctx.vi_Tenants.AsNoTracking()
         .OrderBy(r => r.cTenantOrder).ThenBy(r => r.cTenantName).ThenBy(r => r.cTenantId)
         .ToArrayAsync(AbortToken);

   #endregion

   #region Tables

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_Tenant> PostGetTa_Tenant_New(ta_Tenant row) {
      var parentId = NullIfEmpty(row.cTenantParent_cTenantId);
      if (parentId is not null && !await ctx.ta_Tenants.AnyAsync(r => r.cTenantId == parentId))
         throw new ActionException("The parent tenant was not found.", 400);

      var now = DateTime.UtcNow;
      var tenant = new ta_Tenant {
         cTenantId = string.IsNullOrWhiteSpace(row.cTenantId) ? Ulid.NewUlid().ToString() : row.cTenantId,
         cTenantParent_cTenantId = parentId,
         cTenantName = row.cTenantName,
         cTenantCode = row.cTenantCode,
         cTenantState = row.cTenantState,
         cTenantOrder = row.cTenantOrder,
         cTenantNote = row.cTenantNote,
         json_object = row.json_object,
         ustamp = now,
         datestamp = now
      };
      ctx.ta_Tenants.Add(tenant);
      await SaveAsync(() => ctx.SaveChangesAsync());
      await AuditAsync(tenant.cTenantId, "tenant.created", $"Created tenant '{tenant.cTenantCode}'");
      return await ReadAsync(tenant.cTenantId);
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_Tenant> PostGetTa_Tenant_Update(ta_Tenant row) {
      var parentId = NullIfEmpty(row.cTenantParent_cTenantId);
      await using (var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable)) {
         var links = await ctx.ta_Tenants.AsNoTracking()
            .Select(r => new { r.cTenantId, r.cTenantParent_cTenantId, r.ustamp })
            .ToDictionaryAsync(r => r.cTenantId, StringComparer.OrdinalIgnoreCase);
         if (!links.TryGetValue(row.cTenantId, out var current))
            throw new ActionException("The tenant was not found.", 404);
         if (current.ustamp != row.ustamp)
            throw new ActionException("The tenant was changed by someone else. Reload before saving.", 409);
         if (parentId is not null) {
            if (string.Equals(parentId, row.cTenantId, StringComparison.OrdinalIgnoreCase))
               throw new ActionException("A tenant cannot be its own parent.", 400);
            if (!links.ContainsKey(parentId))
               throw new ActionException("The parent tenant was not found.", 400);
            // Walks up from the new parent: reaching the tenant itself means the parent is one of its descendants.
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var id = parentId; id is not null && visited.Add(id); id = links.TryGetValue(id, out var up) ? up.cTenantParent_cTenantId : null) {
               if (string.Equals(id, row.cTenantId, StringComparison.OrdinalIgnoreCase))
                  throw new ActionException("The parent cannot be one of the tenant's own descendants.", 400);
            }
         }

         // A datetime column keeps about 3 ms; make sure the stamp really moves so the next stale check sees it.
         var now = DateTime.UtcNow;
         if (now <= current.ustamp.AddMilliseconds(10)) now = current.ustamp.AddMilliseconds(10);
         var affected = 0;
         await SaveAsync(async () => affected = await ctx.ta_Tenants
            .Where(r => r.cTenantId == row.cTenantId && r.ustamp == current.ustamp)
            .ExecuteUpdateAsync(s => s
               .SetProperty(r => r.cTenantParent_cTenantId, parentId)
               .SetProperty(r => r.cTenantName, row.cTenantName)
               .SetProperty(r => r.cTenantCode, row.cTenantCode)
               .SetProperty(r => r.cTenantState, row.cTenantState)
               .SetProperty(r => r.cTenantOrder, row.cTenantOrder)
               .SetProperty(r => r.cTenantNote, row.cTenantNote)
               .SetProperty(r => r.json_object, row.json_object)
               .SetProperty(r => r.ustamp, now)));
         if (affected != 1)
            throw new ActionException("The tenant was changed by someone else. Reload before saving.", 409);
         await tx.CommitAsync();
      }

      await AuditAsync(row.cTenantId, "tenant.changed", $"Saved tenant '{row.cTenantCode}'");
      return await ReadAsync(row.cTenantId);
   }

   #endregion

   private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

   private async Task<vi_Tenant> ReadAsync(string id) =>
      await ctx.vi_Tenants.AsNoTracking().SingleAsync(r => r.cTenantId == id);

   private static async Task SaveAsync(Func<Task> save) {
      try {
         await save();
      }
      catch (Exception ex) when (IsUniqueConflict(ex)) {
         throw new ActionException("The tenant code is already in use.", 409);
      }
   }

   private static bool IsUniqueConflict(Exception ex) =>
      (ex as SqlException ?? ex.InnerException as SqlException) is { Number: 2601 or 2627 };

   // Called without an application when a test constructs the service directly.
   private Task AuditAsync(string id, string kind, string summary) =>
      App is null ? Task.CompletedTask : AuditSystemAsync("System.Settings", id, kind, summary, "tenant");
}
