using Em.Api.Core.Models;
using Em.Api.Shared;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.IntegrationTests;

/// <summary>Positions, department heads, contact names and attachments on a real MySQL server.</summary>
public sealed class MySqlTenantOrgTests(MySqlEngineDatabase database) : IClassFixture<MySqlEngineDatabase>, IAsyncLifetime
{
   private static CancellationToken Ct => TestContext.Current.CancellationToken;
   private readonly List<ApiCoreContext> _contexts = [];
   private string _connection = "";

   public async ValueTask InitializeAsync() => _connection = await database.CreateFullAsync(Ct);

   public async ValueTask DisposeAsync() {
      foreach (var db in _contexts) await db.DisposeAsync();
   }

   private ApiCoreContext Core() {
      var options = new DbContextOptionsBuilder<ApiCoreContext>();
      options.UseEmProvider(DatabaseProvider.MySql, _connection);
      var db = new ApiCoreContext(options.Options);
      _contexts.Add(db);
      return db;
   }

   private Task<vi_Tenant> Tenant(string code) => new TenantServices(Core()).PostGetTa_Tenant_New(new() { cTenantCode = code, cTenantName = code });
   private TenantEmpServices Emp() => new(Core());
   private TenantPositionServices Positions() => new(Core());
   private Task<vi_TenantEmp> Employee(string tenant, string code, string? dept = null) => Emp().PostGetMeta_TenantEmpSave(new() {
      Row = new() { cTenantId = tenant, cTenantEmpCode = code, cTenantDeptId = dept }, NewContactName = "Person " + code, NewContactType = ContactType.Female,
      Person = new() { Comm = new() { cCommValue = code + "@example.test" }, Address = new() { cAddressLocation = "Address " + code } }
   });

   [Fact]
   public async Task PositionsCopyAndHeadWorkInOneSave() {
      var source = await Tenant("my-source"); var target = await Tenant("my-target");
      var eng = await Positions().PostGetTa_TenantPosition_New(new() { cTenantId = source.cTenantId, cTenantPositionCode = "ENG", cTenantPositionName = "Engineer" });
      await Assert.ThrowsAsync<ActionException>(() => Positions().PostGetTa_TenantPosition_New(new() { cTenantId = source.cTenantId, cTenantPositionCode = "ENG", cTenantPositionName = "Twice" }));
      var copy = Assert.Single(await Positions().PostGetMeta_TenantPosition_Copy(new() { SourceTenantId = source.cTenantId, TargetTenantId = target.cTenantId, PositionIds = [eng.cTenantPositionId] }));
      var department = await new TenantDeptServices(Core()).PostGetTa_TenantDept_New(new() { cTenantId = target.cTenantId, cTenantDeptCode = "OPS", cTenantDeptName = "Operations" });
      var employee = await Employee(target.cTenantId, "head", department.cTenantDeptId);
      var change = JsonCopy<ta_TenantEmp>(employee); change.cTenantPositionId = copy.cTenantPositionId;
      employee = await Emp().PostGetMeta_TenantEmpSave(new() { Row = change, IsDeptHead = true });
      Assert.Equal("Engineer", employee.cvTenantPositionName); Assert.True(employee.ccTenantEmpIsDeptHead);
      Assert.Equal(1, (await Positions().GetVi_TenantPositions(target.cTenantId)).Single().ccTenantPositionEmpCount);
      var person = await Emp().GetMeta_TenantEmpPerson(employee.cContactId); person.FullName = "Renamed";
      employee = await Emp().PostGetMeta_TenantEmpSave(new() { Row = JsonCopy<ta_TenantEmp>(employee), Person = person });
      Assert.Equal("Renamed", employee.cvContactFullName);
   }

   [Fact]
   public async Task AttachmentsKeepTheirRecordAndStamp() {
      var tenant = await Tenant("my-files"); var employee = await Employee(tenant.cTenantId, "files");
      var store = new InMemoryObjectStore();
      using var content = new MemoryStream([1, 2, 3]);
      var added = await new ContactAttachmentServices(Core(), store).PostGetMeta_ContactAttachment_Add(new() { ContactId = employee.cContactId, FileName = "photo.png", Kind = ContactAttachmentKind.Photo }, content);
      Assert.Equal("image/png", added.Mime);
      var person = await Emp().GetMeta_TenantEmpPerson(employee.cContactId);
      var edit = JsonCopy<ta_ContactAttachment>(added.Row); edit.cContactAttachmentNote = "Portrait";
      person.Attachments = [edit];
      await Emp().PostGetMeta_TenantEmpSave(new() { Row = JsonCopy<ta_TenantEmp>(employee), Person = person });
      var listed = Assert.Single(await new ContactAttachmentServices(Core(), store).GetMeta_ContactAttachments(employee.cContactId));
      Assert.Equal("Portrait", listed.Row.cContactAttachmentNote); Assert.Equal(ContactAttachmentKind.Photo, listed.Row.cContactAttachmentKind);
      await new ContactAttachmentServices(Core(), store).PostMeta_ContactAttachment_Remove(listed.Row.cContactAttachmentId, listed.Row.ustamp);
      Assert.Empty(await new ContactAttachmentServices(Core(), store).GetMeta_ContactAttachments(employee.cContactId));
   }

   [Fact]
   public async Task UpdateScriptMovesPositionTextAndIsSafeToRunAgain() {
      var tenant = await Tenant("my-update"); var employee = await Employee(tenant.cTenantId, "old");
      await using (var connection = new MySqlConnector.MySqlConnection(_connection)) {
         await connection.OpenAsync(Ct);
         foreach (var sql in (string[])["ALTER TABLE ta_TenantEmp DROP FOREIGN KEY FK_ta_TenantEmp_ta_TenantPosition", "ALTER TABLE ta_TenantEmp DROP COLUMN cTenantPositionId",
            "DROP TABLE ta_TenantPosition", "DROP TABLE ta_ContactAttachment", "ALTER TABLE ta_TenantEmp ADD cTenantEmpPosition varchar(255) NULL",
            $"UPDATE ta_TenantEmp SET cTenantEmpPosition = ' Site Engineer ' WHERE cTenantEmpId = '{employee.cTenantEmpId}'"]) {
            await using var command = new MySqlConnector.MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(Ct);
         }
      }
      await database.RunAsync(_connection, "updates/20261107-TenantPosition.sql", Ct);
      await database.RunAsync(_connection, "updates/20261107-TenantPosition.sql", Ct);
      await database.RunAsync(_connection, "views/vi_TenantPosition.sql", Ct);
      await database.RunAsync(_connection, "views/vi_TenantEmp.sql", Ct);
      var read = await Emp().GetVi_TenantEmp_ById(employee.cTenantEmpId);
      Assert.Equal("Site Engineer", read!.cvTenantPositionName); Assert.Equal("Site Engineer", read.cvTenantPositionCode);
      Assert.Single(await Positions().GetVi_TenantPositions(tenant.cTenantId));
   }

   private static T JsonCopy<T>(object row) => System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(row))!;
}
