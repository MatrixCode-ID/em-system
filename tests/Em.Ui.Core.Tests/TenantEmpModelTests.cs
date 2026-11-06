using System.Text.Json.Nodes;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Ui.Core.Tests;

public sealed class TenantEmpModelTests
{
   private static readonly DateTime ReadStamp = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

   private static vi_TenantEmp Row(string? json = null) => new() {
      cTenantEmpId = "E1", cTenantId = "T1", cContactId = "C1", cTenantDeptId = "D1", cTenantEmpCode = "001",
      cTenantEmpPosition = "Clerk", cTenantEmpNote = "Note", cvContactFullName = "Name", cvContactType = ContactType.Female,
      cvTenantDeptCode = "DC", cvTenantDeptName = "Dept", ccTenantEmpIsDeptHead = true, cvTenantEmpUserAccounts = "name",
      ustamp = ReadStamp, datestamp = ReadStamp.AddDays(-1), json_object = json
   };

   [Fact]
   public void ColumnsMapBothWays() {
      var model = TenantEmp.Build(new StubApp(), Row());
      Assert.Equal("Name", model.cvContactFullName); Assert.Equal("Dept", model.cvTenantDeptName); Assert.True(model.ccTenantEmpIsDeptHead);
      model.cTenantEmpCode = "002";
      var entity = model.ToEntity();
      Assert.Equal("002", entity.cTenantEmpCode); Assert.Equal("D1", entity.cTenantDeptId); Assert.Equal("E1", entity.cTenantEmpId);
      Assert.Equal("name", entity.cvTenantEmpUserAccounts);
   }

   [Fact]
   public void UnknownJsonFieldsSurviveADerivedModel() {
      var model = new Derived(new StubApp(), Row("""{"Known":"a","Other":{"x":1}}"""));
      Assert.Equal("a", model.Known);
      model.Known = "b";
      var json = JsonNode.Parse(model.ToEntity().json_object!)!;
      Assert.Equal("b", json["Known"]!.GetValue<string>()); Assert.Equal(1, json["Other"]!["x"]!.GetValue<int>());
   }

   [Fact]
   public void ToEntityDoesNotMakeTheModelDirty() {
      var model = new Derived(new StubApp(), Row("""{"Known":"a"}"""));
      model.ToEntity();
      Assert.False(model.IsDirty);
   }

   [Fact]
   public async Task SaveSendsTheLastReadStampAndKeepsTheReturnedOne() {
      var app = new StubApp(); var model = TenantEmp.Build(app, Row());
      model.cTenantEmpCode = "002";
      await model.SaveAsync();
      Assert.Equal(ReadStamp, app.Service.Saved!.Row.ustamp);
      Assert.Equal(app.Service.Returned, model.ustamp); Assert.False(model.IsDirty);
      model.cTenantEmpCode = "003";
      await model.SaveAsync();
      Assert.Equal(StubService.First, app.Service.Saved!.Row.ustamp);
   }

   [Fact]
   public async Task BlankSaveInsertsAndTakesTheKey() {
      var app = new StubApp(); var model = TenantEmp.NewBlank(app, "T1");
      Assert.True(model.IsBlank); Assert.False(model.IsDirty);
      model.NewContactName = "New person"; model.cTenantEmpCode = "009";
      await model.SaveAsync();
      Assert.False(model.IsBlank); Assert.Equal("NEW", model.cTenantEmpId); Assert.Equal("New person", app.Service.Saved!.NewContactName);
   }

   private sealed class Derived(IEmApp app, vi_TenantEmp data) : TenantEmp(app, data)
   {
      public string? Known { get; set => SetField(ref field, value); }
      protected override void ReadFrom(vi_TenantEmp source) { base.ReadFrom(source); Known = GetJson<string>("Known"); }
      protected override JsonObject BuildJson(JsonObject patch) { patch["Known"] = Known; return patch; }
   }

   private sealed class StubApp : IEmApp, IServiceProvider
   {
      public StubService Service { get; } = new();
      public IServiceProvider ServiceProvider => this;
      public object? GetService(Type serviceType) => serviceType == typeof(ITenantEmpServices) ? Service : null;
      public Task<DateTime> GetDateStampAsync() => Task.FromResult(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
   }

   private sealed class StubService : ITenantEmpServices
   {
      public static readonly DateTime First = new(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc);
      public TenantEmpSave? Saved { get; private set; }
      public DateTime Returned { get; private set; }
      public Task<vi_TenantEmp> PostGetMeta_TenantEmpSave(TenantEmpSave save) {
         Saved = save;
         Returned = Returned == default ? First : Returned.AddMinutes(1);
         var row = (vi_TenantEmp)save.Row;
         return Task.FromResult(new vi_TenantEmp {
            cTenantEmpId = row.cTenantEmpId == "" ? "NEW" : row.cTenantEmpId, cTenantId = row.cTenantId, cContactId = "C1",
            cTenantEmpCode = row.cTenantEmpCode, ustamp = Returned, datestamp = First, json_object = row.json_object
         });
      }
      public Task<vi_TenantEmp[]> GetVi_TenantEmps(string tenantId) => throw new NotSupportedException();
      public Task<vi_TenantEmp?> GetVi_TenantEmp_ById(string tenantEmpId) => throw new NotSupportedException();
      public Task<TenantEmpPerson> GetMeta_TenantEmpPerson(string contactId) => throw new NotSupportedException();
      public Task<TenantEmpContactCandidate[]> GetMeta_TenantEmpContacts(string tenantId, string? search) => throw new NotSupportedException();
      public Task<vi_TenantEmp?> GetMeta_TenantEmpByUser(string tenantId, string userId) => throw new NotSupportedException();
      public Task<string[]> GetMeta_TenantEmpUsers(string tenantEmpId) => throw new NotSupportedException();
      public Task<vi_TenantEmp?> GetMeta_TenantEmpHead(string tenantEmpId) => throw new NotSupportedException();
   }
}
