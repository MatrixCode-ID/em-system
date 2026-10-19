using System.Text.RegularExpressions;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.IntegrationTests;

/// <summary>
/// Tenant actions of the Business Module Manager on a database created from the real
/// <c>sets/000-ulid.sql</c>, <c>tables/080-tenant.sql</c> and <c>views/vi_Tenant.sql</c>.
/// </summary>
public sealed class TenantServicesTests(SqlServerDatabase database) : IAsyncLifetime
{
   private string _connection = "";
   private readonly List<ApiCoreContext> _contexts = [];

   public async ValueTask InitializeAsync() {
      var ct = TestContext.Current.CancellationToken;
      _connection = await database.CreateExtraDatabaseAsync(ct);
      var sql = Path.Combine(RepoRoot(), "doc", "sqlscript", "mssql");
      await using var connection = new SqlConnection(_connection);
      await connection.OpenAsync(ct);
      await ExecuteAsync(connection, Path.Combine(sql, "sets", "000-ulid.sql"), ct);
      await ExecuteAsync(connection, Path.Combine(sql, "tables", "080-tenant.sql"), ct);
      // Running the script twice checks that it is idempotent.
      await ExecuteAsync(connection, Path.Combine(sql, "tables", "080-tenant.sql"), ct);
      await ExecuteAsync(connection, Path.Combine(sql, "views", "vi_Tenant.sql"), ct);
   }

   public async ValueTask DisposeAsync() {
      foreach (var db in _contexts) await db.DisposeAsync();
   }

   private TenantServices NewService() {
      var db = new ApiCoreContext(new DbContextOptionsBuilder<ApiCoreContext>().UseSqlServer(_connection).Options);
      _contexts.Add(db);
      return new TenantServices(db);
   }

   private static ta_Tenant Row(string code, string? parent = null, int order = -1) =>
      new() { cTenantCode = code, cTenantName = "Tenant " + code, cTenantParent_cTenantId = parent, cTenantOrder = order };

   private static ta_Tenant Copy(vi_Tenant t) => new() {
      cTenantId = t.cTenantId, cTenantParent_cTenantId = t.cTenantParent_cTenantId, cTenantName = t.cTenantName,
      cTenantCode = t.cTenantCode, cTenantState = t.cTenantState, cTenantOrder = t.cTenantOrder, cTenantNote = t.cTenantNote,
      json_object = t.json_object, ustamp = t.ustamp, datestamp = t.datestamp
   };

   private static async Task<int> StatusOf(Func<Task> action) =>
      (await Assert.ThrowsAsync<ActionException>(action)).StatusCode;

   [Fact]
   public async Task CreatesRootAndChildAndListsThemWithParentAndChildCount() {
      var root = await NewService().PostGetTa_Tenant_New(Row("acme.example"));
      Assert.Equal(26, root.cTenantId.Length);
      Assert.Equal(ta_Tenant.StateActive, root.cTenantState);
      var child = await NewService().PostGetTa_Tenant_New(Row("acme.example.branch", root.cTenantId));
      Assert.Equal("acme.example", child.cvTenantParentCode);
      Assert.Equal(root.cTenantName, child.cvTenantParentName);

      var list = await NewService().GetVi_Tenants();
      var listedRoot = list.Single(t => t.cTenantId == root.cTenantId);
      Assert.Equal(1, listedRoot.ccTenantChildCount);
      Assert.Null(listedRoot.cvTenantParentCode);
   }

   [Fact]
   public async Task DuplicateCodeIsRejectedWith409OnCreateAndUpdate() {
      await NewService().PostGetTa_Tenant_New(Row("dup.example"));
      Assert.Equal(409, await StatusOf(() => NewService().PostGetTa_Tenant_New(Row("dup.example"))));

      var other = await NewService().PostGetTa_Tenant_New(Row("other.example"));
      var change = Copy(other); change.cTenantCode = "dup.example";
      Assert.Equal(409, await StatusOf(() => NewService().PostGetTa_Tenant_Update(change)));
   }

   [Fact]
   public async Task MissingParentIsRejectedOnCreateAndUpdate() {
      var missing = Ulid.NewUlid().ToString();
      Assert.Equal(400, await StatusOf(() => NewService().PostGetTa_Tenant_New(Row("orphan.example", missing))));
      var tenant = await NewService().PostGetTa_Tenant_New(Row("lonely.example"));
      var change = Copy(tenant); change.cTenantParent_cTenantId = missing;
      Assert.Equal(400, await StatusOf(() => NewService().PostGetTa_Tenant_Update(change)));
   }

   [Fact]
   public async Task OwnParentAndDescendantParentAreRejected() {
      var a = await NewService().PostGetTa_Tenant_New(Row("cycle-a.example"));
      var b = await NewService().PostGetTa_Tenant_New(Row("cycle-b.example", a.cTenantId));
      var c = await NewService().PostGetTa_Tenant_New(Row("cycle-c.example", b.cTenantId));

      var self = Copy(a); self.cTenantParent_cTenantId = a.cTenantId;
      Assert.Equal(400, await StatusOf(() => NewService().PostGetTa_Tenant_Update(self)));
      var underGrandchild = Copy(a); underGrandchild.cTenantParent_cTenantId = c.cTenantId;
      Assert.Equal(400, await StatusOf(() => NewService().PostGetTa_Tenant_Update(underGrandchild)));

      // Moving a leaf under another branch is fine.
      var move = Copy(c); move.cTenantParent_cTenantId = a.cTenantId;
      var moved = await NewService().PostGetTa_Tenant_Update(move);
      Assert.Equal(a.cTenantId, moved.cTenantParent_cTenantId);
   }

   [Fact]
   public async Task StaleUpdateIsRejectedWith409AndFreshOneSaves() {
      var tenant = await NewService().PostGetTa_Tenant_New(Row("stale.example"));
      var first = Copy(tenant); first.cTenantName = "First";
      var saved = await NewService().PostGetTa_Tenant_Update(first);
      Assert.Equal("First", saved.cTenantName);
      Assert.NotEqual(tenant.ustamp, saved.ustamp);

      var stale = Copy(tenant); stale.cTenantName = "Stale";
      Assert.Equal(409, await StatusOf(() => NewService().PostGetTa_Tenant_Update(stale)));

      var fresh = Copy(saved); fresh.cTenantName = "Second"; fresh.cTenantNote = "note";
      var second = await NewService().PostGetTa_Tenant_Update(fresh);
      Assert.Equal("Second", second.cTenantName);
      Assert.Equal("note", second.cTenantNote);

      var missing = Copy(saved); missing.cTenantId = Ulid.NewUlid().ToString();
      Assert.Equal(404, await StatusOf(() => NewService().PostGetTa_Tenant_Update(missing)));
   }

   [Fact]
   public async Task DeactivateAndActivateKeepChildrenUntouched() {
      var parent = await NewService().PostGetTa_Tenant_New(Row("state-parent.example"));
      var child = await NewService().PostGetTa_Tenant_New(Row("state-child.example", parent.cTenantId));

      var off = Copy(parent); off.cTenantState = ta_Tenant.StateInactive;
      var inactive = await NewService().PostGetTa_Tenant_Update(off);
      Assert.Equal(ta_Tenant.StateInactive, inactive.cTenantState);
      var list = await NewService().GetVi_Tenants();
      Assert.Equal(ta_Tenant.StateActive, list.Single(t => t.cTenantId == child.cTenantId).cTenantState);

      // An inactive tenant can still be a parent.
      var another = await NewService().PostGetTa_Tenant_New(Row("state-late.example", parent.cTenantId));
      Assert.Equal(parent.cTenantId, another.cTenantParent_cTenantId);

      var on = Copy(inactive); on.cTenantState = ta_Tenant.StateActive;
      Assert.Equal(ta_Tenant.StateActive, (await NewService().PostGetTa_Tenant_Update(on)).cTenantState);
   }

   private static string RepoRoot() {
      for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
         if (File.Exists(Path.Combine(dir.FullName, "doc", "sqlscript", "mssql", "tables", "080-tenant.sql"))) return dir.FullName;
      throw new InvalidOperationException("Repository root with doc/sqlscript was not found.");
   }

   private static async Task ExecuteAsync(SqlConnection connection, string file, CancellationToken ct) {
      var script = await File.ReadAllTextAsync(file, ct);
      foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)) {
         if (string.IsNullOrWhiteSpace(batch)) continue;
         await using var command = new SqlCommand(batch, connection);
         await command.ExecuteNonQueryAsync(ct);
      }
   }
}
