using Em.Api.Core.Models;
using Em.Api.Core.ObjectStorage;
using Em.Shared;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.IntegrationTests;

public sealed class TenantPositionServicesTests(SqlServerDatabase database) : TenantOrgTestsBase(database)
{
   private TenantPositionServices Positions() => new(Db());
   private ContactAttachmentServices Attachments(InMemoryObjectStore store) => new(Db(), store);
   private static MemoryStream Bytes(string text) => new(System.Text.Encoding.UTF8.GetBytes(text));
   private Task<vi_TenantPosition> Position(string tenant, string code, int state = 0) =>
      Positions().PostGetTa_TenantPosition_New(new() { cTenantId = tenant, cTenantPositionCode = code, cTenantPositionName = "Name " + code, cTenantPositionState = state });

   [Fact]
   public async Task PositionsAreUniquePerTenantAndGuardStampAndInput() {
      var one = await Tenant("position-one"); var two = await Tenant("position-two");
      var row = await Position(one.cTenantId, "ENG");
      await Position(two.cTenantId, "ENG");
      await Status(409, () => Position(one.cTenantId, "eng"));
      await Status(400, () => Positions().PostGetTa_TenantPosition_New(new() { cTenantId = one.cTenantId, cTenantPositionCode = " ", cTenantPositionName = "x" }));
      var change = Copy<ta_TenantPosition>(row); change.cTenantPositionName = "Engineer"; change.cTenantPositionState = ta_TenantPosition.StateInactive;
      var saved = await Positions().PostGetTa_TenantPosition_Update(change);
      Assert.Equal("Engineer", saved.cTenantPositionName); Assert.Equal(ta_TenantPosition.StateInactive, saved.cTenantPositionState);
      await Status(409, () => Positions().PostGetTa_TenantPosition_Update(change));
      var moved = Copy<ta_TenantPosition>(saved); moved.cTenantId = two.cTenantId;
      await Status(400, () => Positions().PostGetTa_TenantPosition_Update(moved));
   }

   [Fact]
   public async Task CopySkipsExistingCodesAndInactiveSourcesAndIsSavedActive() {
      var source = await Tenant("copy-source"); var target = await Tenant("copy-target");
      var eng = await Position(source.cTenantId, "ENG"); var pm = await Position(source.cTenantId, "PM");
      var old = await Position(source.cTenantId, "OLD", ta_TenantPosition.StateInactive);
      await Position(target.cTenantId, "eng", ta_TenantPosition.StateInactive);
      var created = await Positions().PostGetMeta_TenantPosition_Copy(new() {
         SourceTenantId = source.cTenantId, TargetTenantId = target.cTenantId, PositionIds = [eng.cTenantPositionId, pm.cTenantPositionId, old.cTenantPositionId]
      });
      var copy = Assert.Single(created);
      Assert.Equal("PM", copy.cTenantPositionCode); Assert.Equal(ta_TenantPosition.StateActive, copy.cTenantPositionState);
      Assert.NotEqual(pm.cTenantPositionId, copy.cTenantPositionId);
      Assert.Equal(2, (await Positions().GetVi_TenantPositions(target.cTenantId)).Length);
      await Status(400, () => Positions().PostGetMeta_TenantPosition_Copy(new() { SourceTenantId = target.cTenantId, TargetTenantId = target.cTenantId }));
   }

   [Fact]
   public async Task EmployeeTakesAnActivePositionOfItsTenantAndKeepsItAfterDeactivation() {
      var t = await Tenant("position-emp"); var other = await Tenant("position-other");
      var active = await Position(t.cTenantId, "ACT"); var foreign = await Position(other.cTenantId, "FOR");
      var inactive = await Position(t.cTenantId, "OFF", ta_TenantPosition.StateInactive);
      var employee = await Employee(t.cTenantId, "pos");
      var change = Copy<ta_TenantEmp>(employee); change.cTenantPositionId = foreign.cTenantPositionId;
      await Status(400, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = change }));
      change.cTenantPositionId = inactive.cTenantPositionId;
      await Status(400, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = change }));
      change.cTenantPositionId = active.cTenantPositionId;
      employee = await Emp().PostGetMeta_TenantEmpSave(new() { Row = change });
      Assert.Equal("ACT", employee.cvTenantPositionCode); Assert.Equal(1, (await Positions().GetVi_TenantPositions(t.cTenantId)).Single(x => x.cTenantPositionCode == "ACT").ccTenantPositionEmpCount);
      var off = Copy<ta_TenantPosition>(await Positions().GetVi_TenantPositions(t.cTenantId).ContinueWith(r => r.Result.Single(x => x.cTenantPositionCode == "ACT")));
      off.cTenantPositionState = ta_TenantPosition.StateInactive;
      await Positions().PostGetTa_TenantPosition_Update(off);
      employee = await Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(employee) });
      Assert.Equal(ta_TenantPosition.StateInactive, employee.cvTenantPositionState);
      await using var connection = new SqlConnection(Db().Database.GetConnectionString()); await connection.OpenAsync(TestContext.Current.CancellationToken);
      await using var command = new SqlCommand("UPDATE dbo.ta_TenantEmp SET cTenantPositionId = @p WHERE cTenantEmpId = @e", connection);
      command.Parameters.AddWithValue("@p", foreign.cTenantPositionId); command.Parameters.AddWithValue("@e", employee.cTenantEmpId);
      await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
   }

   [Fact]
   public async Task FullNameIsSavedWithTheEmployeeAndGuarded() {
      var t = await Tenant("full-name"); var employee = await Employee(t.cTenantId, "name");
      var person = await Emp().GetMeta_TenantEmpPerson(employee.cContactId);
      Assert.Equal("Person name", person.FullName);
      person.FullName = "  Corrected Name  ";
      employee = await Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(employee), Person = person });
      Assert.Equal("Corrected Name", employee.cvContactFullName);
      await Status(409, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(employee), Person = person }));
      person = await Emp().GetMeta_TenantEmpPerson(employee.cContactId); person.FullName = " ";
      await Status(400, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(employee), Person = person }));
      person = await Emp().GetMeta_TenantEmpPerson(employee.cContactId); person.FullName = new string('x', 256);
      await Status(400, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(employee), Person = person }));
      person = await Emp().GetMeta_TenantEmpPerson(employee.cContactId); person.FullName = null;
      employee = await Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(employee), Person = person });
      Assert.Equal("Corrected Name", employee.cvContactFullName);
      await Status(400, () => Emp().PostGetMeta_TenantEmpSave(new() {
         Row = new() { cTenantId = t.cTenantId, cTenantEmpCode = "blank" }, NewContactName = " ",
         Person = new() { Comm = new() { cCommValue = "x" }, Address = new() { cAddressLocation = "y" } }
      }));
   }

   [Fact]
   public async Task HeadIsSetClearedAndMovedInOneSave() {
      var t = await Tenant("head-editor"); var first = await Department(t.cTenantId, "first"); var second = await Department(t.cTenantId, "second");
      var a = await Employee(t.cTenantId, "a", first.cTenantDeptId); var b = await Employee(t.cTenantId, "b", first.cTenantDeptId);
      async Task<vi_TenantDept> Read(string id) => (await Dept().GetVi_TenantDepts(t.cTenantId)).Single(x => x.cTenantDeptId == id);
      a = await Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(a), IsDeptHead = true });
      Assert.True(a.ccTenantEmpIsDeptHead);
      b = await Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(b), IsDeptHead = true });
      Assert.Equal(b.cTenantEmpId, (await Read(first.cTenantDeptId)).cTenantDeptHead_cTenantEmpId);
      var move = Copy<ta_TenantEmp>(b); move.cTenantDeptId = second.cTenantDeptId;
      b = await Emp().PostGetMeta_TenantEmpSave(new() { Row = move, IsDeptHead = true });
      Assert.Null((await Read(first.cTenantDeptId)).cTenantDeptHead_cTenantEmpId);
      Assert.Equal(b.cTenantEmpId, (await Read(second.cTenantDeptId)).cTenantDeptHead_cTenantEmpId);
      a = await Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(await Emp().GetVi_TenantEmp_ById(a.cTenantEmpId) ?? a), IsDeptHead = false });
      Assert.Equal(b.cTenantEmpId, (await Read(second.cTenantDeptId)).cTenantDeptHead_cTenantEmpId);
      b = await Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(b), IsDeptHead = false });
      Assert.Null((await Read(second.cTenantDeptId)).cTenantDeptHead_cTenantEmpId);
      var none = await Employee(t.cTenantId, "none");
      await Status(400, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(none), IsDeptHead = true }));
      var off = Copy<ta_TenantEmp>(b); off.cTenantEmpState = ta_TenantEmp.StateInactive;
      await Status(400, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = off, IsDeptHead = true }));
   }

   [Fact]
   public async Task UpdateScriptMovesPositionTextAndIsSafeToRunAgain() {
      var t = await Tenant("sql-update"); var employee = await Employee(t.cTenantId, "old");
      var connectionString = Db().Database.GetConnectionString()!;
      async Task Batches(string script) {
         await using var connection = new SqlConnection(connectionString); await connection.OpenAsync(TestContext.Current.CancellationToken);
         foreach (var batch in System.Text.RegularExpressions.Regex.Split(script, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase).Where(x => !string.IsNullOrWhiteSpace(x))) {
            await using var command = new SqlCommand(batch, connection); await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
         }
      }
      async Task File(string relative) {
         var root = new DirectoryInfo(AppContext.BaseDirectory);
         while (!System.IO.File.Exists(Path.Combine(root!.FullName, "doc/sqlscript/mssql/tables/090-tenant-org.sql"))) root = root.Parent;
         await Batches(await System.IO.File.ReadAllTextAsync(Path.Combine(root.FullName, "doc/sqlscript/mssql", relative), TestContext.Current.CancellationToken));
      }
      await Batches(@"ALTER TABLE dbo.ta_TenantEmp DROP CONSTRAINT FK_ta_TenantEmp_ta_TenantPosition;
         DROP INDEX IX_ta_TenantEmp_cTenantPositionId ON dbo.ta_TenantEmp;
         ALTER TABLE dbo.ta_TenantEmp DROP COLUMN cTenantPositionId;
         DROP TABLE dbo.ta_TenantPosition; DROP TABLE dbo.ta_ContactAttachment;
         ALTER TABLE dbo.ta_TenantEmp ADD cTenantEmpPosition varchar(255) NULL;
         GO
         UPDATE dbo.ta_TenantEmp SET cTenantEmpPosition = ' Site Engineer ' WHERE cTenantEmpId = '" + employee.cTenantEmpId + "';");
      await File("updates/20261107-TenantPosition.sql");
      await File("updates/20261107-TenantPosition.sql");
      await File("views/vi_TenantPosition.sql");
      await File("views/vi_TenantEmp.sql");
      var read = await Emp().GetVi_TenantEmp_ById(employee.cTenantEmpId);
      Assert.Equal("Site Engineer", read!.cvTenantPositionName); Assert.Equal("Site Engineer", read.cvTenantPositionCode);
      Assert.Single(await Positions().GetVi_TenantPositions(t.cTenantId));
   }

   [Fact]
   public async Task AttachmentsBelongToTheContactAndKeepFilesWhenRemoved() {
      var t = await Tenant("attachments"); var employee = await Employee(t.cTenantId, "files"); var other = await Employee(t.cTenantId, "other");
      var store = new InMemoryObjectStore();
      var added = await Attachments(store).PostGetMeta_ContactAttachment_Add(new() { ContactId = employee.cContactId, FileName = @"C:\scans\diploma.pdf", Kind = ContactAttachmentKind.Diploma }, Bytes("one"));
      Assert.Equal("diploma.pdf", added.Row.cContactAttachmentName); Assert.Equal("diploma.pdf", added.FileName);
      Assert.Equal("application/pdf", added.Mime); Assert.Equal(3, added.Size); Assert.Equal(1, added.Version);
      Assert.Equal(store.ObjectId, added.Row.cStoreObjectId);
      var person = await Emp().GetMeta_TenantEmpPerson(employee.cContactId);
      var edit = Copy<ta_ContactAttachment>(added.Row); edit.cContactAttachmentName = "Degree"; edit.cContactAttachmentNote = "S1"; edit.cContactAttachmentKind = ContactAttachmentKind.Other;
      person.Attachments = [edit];
      employee = await Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(employee), Person = person });
      var listed = Assert.Single(await Attachments(store).GetMeta_ContactAttachments(employee.cContactId));
      Assert.Equal("Degree", listed.Row.cContactAttachmentName); Assert.Equal("S1", listed.Row.cContactAttachmentNote);
      var stale = Copy<ta_ContactAttachment>(edit);
      person = await Emp().GetMeta_TenantEmpPerson(employee.cContactId); person.Attachments = [stale];
      await Status(409, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(employee), Person = person }));
      var foreign = await Emp().GetMeta_TenantEmpPerson(other.cContactId); foreign.Attachments = [Copy<ta_ContactAttachment>(listed.Row)];
      await Status(400, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(other), Person = foreign }));

      var replaced = await Attachments(store).PostGetMeta_ContactAttachment_Replace(new() { AttachmentId = listed.Row.cContactAttachmentId, Ustamp = listed.Row.ustamp, FileName = "degree-2.png" }, Bytes("second"));
      Assert.Equal(2, replaced.Version); Assert.Equal("degree-2.png", replaced.FileName); Assert.Equal("Degree", replaced.Row.cContactAttachmentName);
      Assert.Equal(listed.Row.cStoreObjectId, replaced.Row.cStoreObjectId);
      await Status(409, () => Attachments(store).PostGetMeta_ContactAttachment_Replace(new() { AttachmentId = listed.Row.cContactAttachmentId, Ustamp = listed.Row.ustamp, FileName = "x.png" }, Bytes("x")));
      await using (var read = await Attachments(store).GetMeta_ContactAttachment_Download(listed.Row.cContactAttachmentId)) {
         Assert.Equal("second", await new StreamReader(read).ReadToEndAsync(TestContext.Current.CancellationToken));
      }
      await Attachments(store).PostMeta_ContactAttachment_Remove(replaced.Row.cContactAttachmentId, replaced.Row.ustamp);
      Assert.Empty(await Attachments(store).GetMeta_ContactAttachments(employee.cContactId));
      Assert.Equal(ta_ContactAttachment.StateInactive, (await Db().ta_ContactAttachments.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).cContactAttachmentState);
      Assert.Equal(2, store.VersionCount);
      await Status(404, () => Attachments(store).GetMeta_ContactAttachment_Download(replaced.Row.cContactAttachmentId));
      await Status(404, () => Attachments(store).PostGetMeta_ContactAttachment_Add(new() { ContactId = Ulid.NewUlid().ToString(), FileName = "x.pdf" }, Bytes("x")));
      await Status(503, () => new ContactAttachmentServices(Db()).PostGetMeta_ContactAttachment_Add(new() { ContactId = employee.cContactId, FileName = "x.pdf" }, Bytes("x")));
   }
}

/// <summary>Keeps one object per key with its versions, enough for the attachment rules.</summary>
internal sealed class InMemoryObjectStore : IObjectStore
{
   private sealed record Version(byte[] Content, string Mime, Dictionary<string, string>? Metadata, DateTime At);
   private readonly Dictionary<string, List<Version>> _objects = [];
   public string ObjectId { get; } = Ulid.NewUlid().ToString();
   public int VersionCount => _objects.Values.Sum(x => x.Count);

   private StoreObjectInfo Info(string key) {
      var versions = _objects[key]; var last = versions[^1];
      return new StoreObjectInfo(ObjectId, "bucket", key, versions.Count, last.Content.Length, last.Mime, false, last.At, "hash");
   }
   public async Task<StoreObjectInfo> PutAsync(string bucket, string key, Stream content, string? mime = null, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) {
      using var copy = new MemoryStream(); await content.CopyToAsync(copy, ct);
      if (!_objects.TryGetValue(key, out var versions)) _objects[key] = versions = [];
      versions.Add(new(copy.ToArray(), mime ?? "application/octet-stream", metadata?.ToDictionary(), DateTime.UtcNow));
      return Info(key);
   }
   public Task<StoreObjectRead> OpenAsync(string bucket, string key, int? version = null, CancellationToken ct = default) =>
      Task.FromResult(new StoreObjectRead(Info(key), new MemoryStream(_objects[key][^1].Content), _objects[key][^1].Metadata));
   public Task<StoreObjectInfo?> GetAsync(string bucket, string key, CancellationToken ct = default) => Task.FromResult(_objects.ContainsKey(key) ? Info(key) : null);
   public Task<StoreObjectVersionInfo[]> VersionsAsync(string bucket, string key, CancellationToken ct = default) {
      var versions = _objects[key];
      return Task.FromResult(versions.Select((v, i) => new StoreObjectVersionInfo(ObjectId, i + 1, i == versions.Count - 1, false, v.Content.Length, v.Mime, "hash", v.At, v.Metadata)).Reverse().ToArray());
   }
   public Task<StoreObjectInfo> DeleteAsync(string bucket, string key, CancellationToken ct = default) => throw new NotSupportedException();
   public Task<StoreObjectInfo> RestoreAsync(string bucket, string key, int version, CancellationToken ct = default) => throw new NotSupportedException();
   public Task PurgeVersionAsync(string bucket, string key, int version, CancellationToken ct = default) => throw new NotSupportedException();
   public Task<StoreObjectListing> ListAsync(string bucket, string? prefix = null, string? delimiter = null, string? continuation = null, int limit = 1000, CancellationToken ct = default) => throw new NotSupportedException();
}
