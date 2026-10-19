using System.Text.Json;
using System.Text.RegularExpressions;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.IntegrationTests;

public abstract class TenantOrgTestsBase(SqlServerDatabase database) : IAsyncLifetime
{
   private string _connection = "";
   private readonly List<ApiCoreContext> _contexts = [];
   public async ValueTask InitializeAsync() {
      _connection = await database.CreateExtraDatabaseAsync(TestContext.Current.CancellationToken);
      var root = new DirectoryInfo(AppContext.BaseDirectory);
      while (root is not null && !File.Exists(Path.Combine(root.FullName, "doc/sqlscript/mssql/tables/090-tenant-org.sql"))) root = root.Parent;
      Assert.NotNull(root);
      await using var connection = new SqlConnection(_connection); await connection.OpenAsync(TestContext.Current.CancellationToken);
      foreach (var file in new[] { "sets/000-ulid.sql", "tables/010-core.sql", "tables/080-tenant.sql", "tables/090-tenant-org.sql", "tables/090-tenant-org.sql", "views/vi_Tenant.sql", "views/vi_TenantDept.sql", "views/vi_TenantEmp.sql" }) {
         var script = await File.ReadAllTextAsync(Path.Combine(root.FullName, "doc/sqlscript/mssql", file), TestContext.Current.CancellationToken);
         foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(x => !string.IsNullOrWhiteSpace(x))) {
            await using var command = new SqlCommand(batch, connection); await command.ExecuteNonQueryAsync();
         }
      }
   }
   public async ValueTask DisposeAsync() { foreach (var db in _contexts) await db.DisposeAsync(); }
   protected ApiCoreContext Db() {
      var db = new ApiCoreContext(new DbContextOptionsBuilder<ApiCoreContext>().UseSqlServer(_connection).Options);
      _contexts.Add(db); return db;
   }
   protected TenantDeptServices Dept() => new(Db());
   protected TenantEmpServices Emp() => new(Db());
   protected Task<vi_Tenant> Tenant(string code) => new TenantServices(Db()).PostGetTa_Tenant_New(new() { cTenantCode = code, cTenantName = code });
   protected Task<vi_TenantDept> Department(string tenant, string code, string? parent = null) => Dept().PostGetTa_TenantDept_New(new() { cTenantId = tenant, cTenantDeptCode = code, cTenantDeptName = code, cTenantDeptParent_cTenantDeptId = parent });
   protected Task<vi_TenantEmp> Employee(string tenant, string code, string? dept = null, string? contact = null) => Emp().PostGetMeta_TenantEmpSave(new() { Row = new() { cTenantId = tenant, cTenantEmpCode = code, cTenantDeptId = dept, cContactId = contact ?? "" }, NewContactName = "Person " + code, NewContactType = ContactType.Female, Person = contact is null ? new() { Comm = new() { cCommValue = code + "@example.test" }, Address = new() { cAddressLocation = "Address " + code } } : null });
   protected static T Copy<T>(object row) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(row))!;
   protected static async Task Status(int expected, Func<Task> work) => Assert.Equal(expected, (await Assert.ThrowsAsync<ActionException>(work)).StatusCode);
   protected async Task<vi_TenantDept> Head(vi_TenantDept department, vi_TenantEmp employee) {
      var change = Copy<ta_TenantDept>(department); change.cTenantDeptHead_cTenantEmpId = employee.cTenantEmpId;
      return await Dept().PostGetTa_TenantDept_Update(change);
   }
}

public sealed class TenantDeptServicesTests(SqlServerDatabase database) : TenantOrgTestsBase(database)
{
   [Fact]
   public async Task HierarchyUniquenessAndTenantBoundaries() {
      var t = await Tenant("one"); var other = await Tenant("two");
      var root = await Department(t.cTenantId, "root"); var child = await Department(t.cTenantId, "child", root.cTenantDeptId);
      Assert.Equal("root", child.cvTenantDeptParentName);
      Assert.Equal(1, (await Dept().GetVi_TenantDepts(t.cTenantId)).Single(x => x.cTenantDeptId == root.cTenantDeptId).ccTenantDeptChildCount);
      await Status(409, () => Department(t.cTenantId, "root"));
      await Department(other.cTenantId, "root");
      var duplicateUpdate = Copy<ta_TenantDept>(child); duplicateUpdate.cTenantDeptCode = "root";
      await Status(409, () => Dept().PostGetTa_TenantDept_Update(duplicateUpdate));
      await Status(400, () => Department(other.cTenantId, "invalid", root.cTenantDeptId));
      var change = Copy<ta_TenantDept>(root); change.cTenantDeptParent_cTenantDeptId = root.cTenantDeptId;
      await Status(400, () => Dept().PostGetTa_TenantDept_Update(change));
      change.cTenantDeptParent_cTenantDeptId = child.cTenantDeptId;
      await Status(400, () => Dept().PostGetTa_TenantDept_Update(change));
      change.cTenantDeptParent_cTenantDeptId = null; change.cTenantId = other.cTenantId;
      await Status(400, () => Dept().PostGetTa_TenantDept_Update(change));
   }
   [Fact]
   public async Task StampStateAndHeadValidation() {
      var tenant = await Tenant("state"); var root = await Department(tenant.cTenantId, "root"); var other = await Department(tenant.cTenantId, "other");
      var employee = await Employee(tenant.cTenantId, "member", root.cTenantDeptId);
      await Status(400, () => Head(other, employee));
      var off = Copy<ta_TenantEmp>(employee); off.cTenantEmpState = -1;
      var inactive = await Emp().PostGetMeta_TenantEmpSave(new() { Row = off });
      await Status(400, () => Head(root, inactive));
      var change = Copy<ta_TenantDept>(root); change.cTenantDeptState = -1;
      var saved = await Dept().PostGetTa_TenantDept_Update(change);
      Assert.Equal(-1, saved.cTenantDeptState); Assert.NotEqual(root.ustamp, saved.ustamp);
      await Status(409, () => Dept().PostGetTa_TenantDept_Update(change));
      change = Copy<ta_TenantDept>(saved); change.cTenantDeptState = 0;
      Assert.Equal(0, (await Dept().PostGetTa_TenantDept_Update(change)).cTenantDeptState);
      change = Copy<ta_TenantDept>(saved); change.cTenantDeptId = Ulid.NewUlid().ToString();
      await Status(404, () => Dept().PostGetTa_TenantDept_Update(change));
   }
   [Fact]
   public async Task CreateIgnoresHeadAndDatabaseRejectsForeignTenantParent() {
      var t = await Tenant("create"); var t2 = await Tenant("foreign"); var d = await Department(t.cTenantId, "d");
      var e = await Employee(t.cTenantId, "e", d.cTenantDeptId);
      var created = await Dept().PostGetTa_TenantDept_New(new() { cTenantId = t.cTenantId, cTenantDeptCode = "new", cTenantDeptHead_cTenantEmpId = e.cTenantEmpId });
      Assert.Null(created.cTenantDeptHead_cTenantEmpId);
      await Assert.ThrowsAsync<SqlException>(() => Db().Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.ta_TenantDept SET cTenantDeptParent_cTenantDeptId={d.cTenantDeptId}, cTenantId={t2.cTenantId} WHERE cTenantDeptId={created.cTenantDeptId}", TestContext.Current.CancellationToken));
   }
}
