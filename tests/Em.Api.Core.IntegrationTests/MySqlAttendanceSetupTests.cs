using Em.Api.Core.Models;
using Em.Api.Shared;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.IntegrationTests;

/// <summary>Schedule templates, scheduling settings, attendance codes and public holidays on a real MySQL server.</summary>
public sealed class MySqlAttendanceSetupTests(MySqlEngineDatabase database) : IClassFixture<MySqlEngineDatabase>, IAsyncLifetime
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

   private TenantSchedTemplateServices Templates() => new(Core());
   private TenantSchedSettingServices Settings() => new(Core());
   private TenantAttCodeServices Codes() => new(Core());
   private PublicHolidayServices Holidays() => new(Core());
   private Task<vi_Tenant> Tenant(string code) => new TenantServices(Core()).PostGetTa_Tenant_New(new() { cTenantCode = code, cTenantName = code });
   private Task<vi_TenantDept> Dept(string tenant, string code, string? parent = null) => new TenantDeptServices(Core()).PostGetTa_TenantDept_New(new() {
      cTenantId = tenant, cTenantDeptCode = code, cTenantDeptName = code, cTenantDeptParent_cTenantDeptId = parent
   });

   private static ta_TenantSchedTemplateDay[] Week(decimal hours = 8, decimal saturday = 0) => [.. Enumerable.Range(1, 7).Select(day => new ta_TenantSchedTemplateDay {
      cTenantSchedTemplateDayWeekday = day, cTenantSchedTemplateDayHours = day <= 5 ? hours : day == 6 ? saturday : 0
   })];

   private static TenantSchedTemplateSet NewSet(string tenant, string code, string start = "08:00", decimal hours = 8) => new() {
      Template = new() {
         cTenantId = tenant, cTenantSchedTemplateCode = code, cTenantSchedTemplateName = "Template " + code, cTenantSchedTemplateStart = TimeSpan.Parse(start),
         cTenantSchedTemplateBreak = 60
      },
      Days = Week(hours)
   };

   private Task<TenantSchedTemplateSet> Template(string tenant, string code) => Templates().PostGetMeta_TenantSchedTemplate_Save(NewSet(tenant, code));

   private static async Task<int> Status(Func<Task> action) => (await Assert.ThrowsAsync<ActionException>(action)).StatusCode;

   [Fact]
   public async Task TemplateSavesSevenDaysAndDuplicates() {
      var tenant = await Tenant("t-template");
      var set = NewSet(tenant.cTenantId, "OFF");
      set.Days[5].cTenantSchedTemplateDayHours = 5; set.Days[5].cTenantSchedTemplateDayBreak = 0;
      var saved = await Templates().PostGetMeta_TenantSchedTemplate_Save(set);
      Assert.Equal(7, saved.Days.Length); Assert.Equal(45m, saved.Template.ccTenantSchedTemplateWeekHours); Assert.False(saved.Template.ccTenantSchedTemplateEndsNextDay);
      Assert.Equal(TimeSpan.FromHours(8), saved.Template.cTenantSchedTemplateStart);
      var read = await Templates().GetMeta_TenantSchedTemplate(saved.Template.cTenantSchedTemplateId);
      Assert.Equal(0, read.Days[5].cTenantSchedTemplateDayBreak); Assert.Null(read.Days[0].cTenantSchedTemplateDayBreak);

      read.Days[0].cTenantSchedTemplateDayHours = 7.5m; read.Template.cTenantSchedTemplateName = "Office";
      var changed = await Templates().PostGetMeta_TenantSchedTemplate_Save(read);
      Assert.Equal(44.5m, changed.Template.ccTenantSchedTemplateWeekHours); Assert.Equal(7, changed.Days.Length);

      var copy = await Templates().PostGetMeta_TenantSchedTemplate_Duplicate(saved.Template.cTenantSchedTemplateId);
      Assert.Equal("OFF2", copy.Template.cTenantSchedTemplateCode); Assert.Equal("Office copy", copy.Template.cTenantSchedTemplateName);
      Assert.Equal(7, copy.Days.Length); Assert.Equal(5m, copy.Days[5].cTenantSchedTemplateDayHours); Assert.Equal(0, copy.Days[5].cTenantSchedTemplateDayBreak);
      var second = await Templates().PostGetMeta_TenantSchedTemplate_Duplicate(saved.Template.cTenantSchedTemplateId);
      Assert.Equal("OFF3", second.Template.cTenantSchedTemplateCode);

      var longCode = await Template(tenant.cTenantId, "ABCDEFGHIJ");
      Assert.Equal("ABCDEFGHI2", (await Templates().PostGetMeta_TenantSchedTemplate_Duplicate(longCode.Template.cTenantSchedTemplateId)).Template.cTenantSchedTemplateCode);
   }

   [Fact]
   public async Task TemplateEndingOnTheNextDayIsMarked() {
      var tenant = await Tenant("t-night");
      var night = await Templates().PostGetMeta_TenantSchedTemplate_Save(NewSet(tenant.cTenantId, "N", "23:00", 7));
      Assert.True(night.Template.ccTenantSchedTemplateEndsNextDay);
      Assert.True((await Templates().GetVi_TenantSchedTemplates(tenant.cTenantId)).Single().ccTenantSchedTemplateEndsNextDay);
   }

   [Fact]
   public async Task TemplateValidationAnswers400() {
      var tenant = await Tenant("t-valid");
      async Task Bad(Action<TenantSchedTemplateSet> change) {
         var set = NewSet(tenant.cTenantId, "BAD"); change(set);
         Assert.Equal(400, await Status(() => Templates().PostGetMeta_TenantSchedTemplate_Save(set)));
      }
      await Bad(x => x.Template.cTenantSchedTemplateCode = " ");
      await Bad(x => x.Template.cTenantSchedTemplateCode = "ELEVEN-CHAR");
      await Bad(x => x.Template.cTenantSchedTemplateName = "");
      await Bad(x => x.Template.cTenantSchedTemplateState = 5);
      await Bad(x => x.Template.cTenantSchedTemplateBreak = -1);
      await Bad(x => x.Days = x.Days[..6]);
      await Bad(x => x.Days[6].cTenantSchedTemplateDayWeekday = 1);
      await Bad(x => x.Days[0].cTenantSchedTemplateDayHours = 25);
      await Bad(x => x.Days[0].cTenantSchedTemplateDayBreak = -5);
      await Bad(x => { x.Days[0].cTenantSchedTemplateDayHours = 24; x.Days[0].cTenantSchedTemplateDayBreak = 1; });
      Assert.Empty(await Templates().GetVi_TenantSchedTemplates(tenant.cTenantId));
   }

   [Fact]
   public async Task TemplateStampCodeAndRollbackAreEnforced() {
      var tenant = await Tenant("t-stale");
      var first = await Template(tenant.cTenantId, "A"); var second = await Template(tenant.cTenantId, "B");
      Assert.Equal(409, await Status(() => Templates().PostGetMeta_TenantSchedTemplate_Save(NewSet(tenant.cTenantId, "a"))));

      var edit = await Templates().GetMeta_TenantSchedTemplate(first.Template.cTenantSchedTemplateId);
      var stale = await Templates().GetMeta_TenantSchedTemplate(first.Template.cTenantSchedTemplateId);
      edit.Template.cTenantSchedTemplateNote = "fresh";
      await Templates().PostGetMeta_TenantSchedTemplate_Save(edit);
      Assert.Equal(409, await Status(() => Templates().PostGetMeta_TenantSchedTemplate_Save(stale)));

      // A code conflict rolls the whole save back, days included.
      var conflict = await Templates().GetMeta_TenantSchedTemplate(second.Template.cTenantSchedTemplateId);
      conflict.Template.cTenantSchedTemplateCode = "A"; conflict.Days[0].cTenantSchedTemplateDayHours = 1;
      Assert.Equal(409, await Status(() => Templates().PostGetMeta_TenantSchedTemplate_Save(conflict)));
      var kept = await Templates().GetMeta_TenantSchedTemplate(second.Template.cTenantSchedTemplateId);
      Assert.Equal("B", kept.Template.cTenantSchedTemplateCode); Assert.Equal(8m, kept.Days[0].cTenantSchedTemplateDayHours);

      var other = await Tenant("t-other");
      var moved = await Templates().GetMeta_TenantSchedTemplate(first.Template.cTenantSchedTemplateId);
      moved.Template.cTenantId = other.cTenantId;
      Assert.Equal(400, await Status(() => Templates().PostGetMeta_TenantSchedTemplate_Save(moved)));
      Assert.Equal(404, await Status(() => Templates().GetMeta_TenantSchedTemplate("01KMISSINGMISSINGMISSINGXX")));
   }

   [Fact]
   public async Task TenantStartsDisabledThenUsesTheBuiltInSchedule() {
      var tenant = await Tenant("t-built");
      var dept = await Dept(tenant.cTenantId, "OPS");
      var effective = await Settings().GetMeta_TenantSchedSetting_Effective(tenant.cTenantId, null);
      Assert.False(effective.Enabled); Assert.StartsWith("Disabled", effective.Text); Assert.Equal(14, effective.PeriodDays);

      var saved = await Settings().PostGetMeta_TenantSchedSetting_Save(new() { TenantId = tenant.cTenantId, Enable = TenantSchedEnable.Enabled, Mode = TenantSchedMode.BuiltIn, PeriodDays = 30 });
      Assert.Equal(tenant.cTenantId, saved.cTenantSchedSettingKey); Assert.Null(saved.cTenantDeptId);
      effective = await Settings().GetMeta_TenantSchedSetting_Effective(tenant.cTenantId, dept.cTenantDeptId);
      Assert.True(effective.Enabled); Assert.Equal(TenantSchedMode.BuiltIn, effective.Mode); Assert.Null(effective.TemplateId); Assert.Equal(30, effective.PeriodDays);
      Assert.Contains(TenantSchedBuiltIn.Name, effective.Text); Assert.Contains("inherited from t-built", effective.Text);
      Assert.Empty(await Templates().GetVi_TenantSchedTemplates(tenant.cTenantId));
   }

   [Fact]
   public async Task SubDepartmentInheritsThroughItsParentChain() {
      var tenant = await Tenant("t-chain");
      var plant = await Dept(tenant.cTenantId, "PLANT"); var line = await Dept(tenant.cTenantId, "LINE", plant.cTenantDeptId); var cell = await Dept(tenant.cTenantId, "CELL", line.cTenantDeptId);
      var office = await Template(tenant.cTenantId, "OFF"); var factory = await Template(tenant.cTenantId, "FAC");
      await Settings().PostGetMeta_TenantSchedSetting_Save(new() { TenantId = tenant.cTenantId, Enable = TenantSchedEnable.Enabled, Mode = TenantSchedMode.Template, TemplateId = office.Template.cTenantSchedTemplateId });
      await Settings().PostGetMeta_TenantSchedSetting_Save(new() { TenantId = tenant.cTenantId, DeptId = plant.cTenantDeptId, Enable = TenantSchedEnable.Inherit, Mode = TenantSchedMode.Template, TemplateId = factory.Template.cTenantSchedTemplateId });
      await Settings().PostGetMeta_TenantSchedSetting_Save(new() { TenantId = tenant.cTenantId, DeptId = line.cTenantDeptId, Enable = TenantSchedEnable.Disabled, Mode = TenantSchedMode.Inherit });

      var rows = await Settings().GetMeta_TenantSchedSettings(tenant.cTenantId);
      Assert.Equal(4, rows.Length); Assert.Null(rows[0].DeptId);
      var cellRow = rows.Single(x => x.DeptId == cell.cTenantDeptId);
      Assert.Null(cellRow.Setting);
      Assert.False(cellRow.Effective.Enabled); Assert.Equal("LINE", cellRow.Effective.EnableSource);
      Assert.Equal(factory.Template.cTenantSchedTemplateId, cellRow.Effective.TemplateId); Assert.Equal("PLANT", cellRow.Effective.ScheduleSource);
      var plantRow = rows.Single(x => x.DeptId == plant.cTenantDeptId);
      Assert.True(plantRow.Effective.Enabled); Assert.Equal(tenant.cTenantName, plantRow.Effective.EnableSource); Assert.Contains("set on this level", plantRow.Effective.Text);
      Assert.Equal("Template FAC", plantRow.Setting!.cvTenantSchedTemplateName); Assert.Equal("PLANT", plantRow.Setting.cvTenantDeptCode);

      var kept = await Settings().PostGetMeta_TenantSchedSetting_UseInherited(tenant.cTenantId, plant.cTenantDeptId, plantRow.Setting.ustamp);
      Assert.Equal(TenantSchedMode.Inherit, kept!.cTenantSchedSettingMode); Assert.Null(kept.cTenantSchedTemplateId);
      Assert.Equal(office.Template.cTenantSchedTemplateId, (await Settings().GetMeta_TenantSchedSetting_Effective(tenant.cTenantId, plant.cTenantDeptId)).TemplateId);
      Assert.Null(await Settings().PostGetMeta_TenantSchedSetting_UseInherited(tenant.cTenantId, cell.cTenantDeptId, DateTime.UtcNow));
   }

   [Fact]
   public async Task RotationIsValidatedAndReusesItsSteps() {
      var tenant = await Tenant("t-rotation");
      var dept = await Dept(tenant.cTenantId, "PROD");
      var a = await Template(tenant.cTenantId, "A"); var b = await Template(tenant.cTenantId, "B"); var c = await Template(tenant.cTenantId, "C");
      string[] ids = [a.Template.cTenantSchedTemplateId, b.Template.cTenantSchedTemplateId, c.Template.cTenantSchedTemplateId];
      var day1 = new DateTime(2026, 12, 21);
      TenantSchedSettingSave Save(string[] steps, DateTime? stamp = null) => new() {
         TenantId = tenant.cTenantId, DeptId = dept.cTenantDeptId, Enable = TenantSchedEnable.Enabled, Mode = TenantSchedMode.Rotation, StepTemplateIds = steps, StepWeeks = 2, CycleStart = day1, ustamp = stamp
      };
      Assert.Equal(400, await Status(() => Settings().PostGetMeta_TenantSchedSetting_Save(Save(ids[..1]))));
      var noWeeks = Save(ids); noWeeks.StepWeeks = 0;
      Assert.Equal(400, await Status(() => Settings().PostGetMeta_TenantSchedSetting_Save(noWeeks)));
      var noDay = Save(ids); noDay.CycleStart = null;
      Assert.Equal(400, await Status(() => Settings().PostGetMeta_TenantSchedSetting_Save(noDay)));

      var saved = await Settings().PostGetMeta_TenantSchedSetting_Save(Save(ids));
      var effective = await Settings().GetMeta_TenantSchedSetting_Effective(tenant.cTenantId, dept.cTenantDeptId);
      Assert.Equal(TenantSchedMode.Rotation, effective.Mode); Assert.Equal(3, effective.Steps.Length); Assert.Equal(day1, effective.CycleStart);
      Assert.Null(effective.StepOn(day1.AddDays(-1)));
      Assert.Equal(ids[0], effective.StepOn(day1)!.TemplateId); Assert.Equal(ids[1], effective.StepOn(day1.AddDays(14))!.TemplateId);
      Assert.Equal(ids[2], effective.StepOn(day1.AddDays(28))!.TemplateId); Assert.Equal(ids[0], effective.StepOn(day1.AddDays(42))!.TemplateId);

      saved = await Settings().PostGetMeta_TenantSchedSetting_Save(Save(ids[..2], saved.ustamp));
      Assert.Equal(2, (await Settings().GetMeta_TenantSchedSetting_Effective(tenant.cTenantId, dept.cTenantDeptId)).Steps.Length);
      saved = await Settings().PostGetMeta_TenantSchedSetting_Save(Save([ids[2], ids[1], ids[0]], saved.ustamp));
      await using var db = Core();
      var steps = await db.ta_TenantSchedSettingSteps.AsNoTracking().Where(x => x.cTenantSchedSettingId == saved.cTenantSchedSettingId).OrderBy(x => x.cTenantSchedSettingStepOrder).ToArrayAsync(Ct);
      Assert.Equal(3, steps.Length); Assert.All(steps, x => Assert.Equal(ta_TenantSchedSettingStep.StateActive, x.cTenantSchedSettingStepState));
      Assert.Equal([ids[2], ids[1], ids[0]], steps.Select(x => x.cTenantSchedTemplateId));

      // Moving to a template clears the rotation fields and keeps the steps inactive.
      saved = await Settings().PostGetMeta_TenantSchedSetting_Save(new() {
         TenantId = tenant.cTenantId, DeptId = dept.cTenantDeptId, Enable = TenantSchedEnable.Enabled, Mode = TenantSchedMode.Template, TemplateId = ids[0], StepWeeks = 2, CycleStart = day1, ustamp = saved.ustamp
      });
      Assert.Null(saved.cTenantSchedSettingStepWeeks); Assert.Null(saved.cTenantSchedSettingCycleStart);
      Assert.Equal(3, await db.ta_TenantSchedSettingSteps.CountAsync(x => x.cTenantSchedSettingId == saved.cTenantSchedSettingId && x.cTenantSchedSettingStepState == ta_TenantSchedSettingStep.StateInactive, Ct));
   }

   [Fact]
   public async Task SettingGuardsLevelsTemplatesAndStamps() {
      var tenant = await Tenant("t-guard"); var other = await Tenant("t-guard2");
      var dept = await Dept(tenant.cTenantId, "D"); var foreign = await Dept(other.cTenantId, "F");
      var mine = await Template(tenant.cTenantId, "M"); var theirs = await Template(other.cTenantId, "T");
      var id = mine.Template.cTenantSchedTemplateId;
      Assert.Equal(400, await Status(() => Settings().PostGetMeta_TenantSchedSetting_Save(new() { TenantId = tenant.cTenantId, Enable = TenantSchedEnable.Inherit })));
      Assert.Equal(400, await Status(() => Settings().PostGetMeta_TenantSchedSetting_Save(new() { TenantId = tenant.cTenantId, DeptId = foreign.cTenantDeptId })));
      Assert.Equal(400, await Status(() => Settings().GetMeta_TenantSchedSetting_Effective(tenant.cTenantId, foreign.cTenantDeptId)));
      Assert.Equal(400, await Status(() => Settings().PostGetMeta_TenantSchedSetting_Save(new() { TenantId = tenant.cTenantId, DeptId = dept.cTenantDeptId, PeriodDays = 14 })));
      Assert.Equal(400, await Status(() => Settings().PostGetMeta_TenantSchedSetting_Save(new() { TenantId = tenant.cTenantId, Enable = TenantSchedEnable.Enabled, PeriodDays = 367 })));
      Assert.Equal(400, await Status(() => Settings().PostGetMeta_TenantSchedSetting_Save(new() { TenantId = tenant.cTenantId, Enable = TenantSchedEnable.Enabled, Mode = TenantSchedMode.Template })));
      Assert.Equal(400, await Status(() => Settings().PostGetMeta_TenantSchedSetting_Save(new() { TenantId = tenant.cTenantId, Enable = TenantSchedEnable.Enabled, Mode = TenantSchedMode.Template, TemplateId = theirs.Template.cTenantSchedTemplateId })));
      Assert.Equal(404, await Status(() => Settings().GetMeta_TenantSchedSettings("01KMISSINGMISSINGMISSINGXX")));

      var saved = await Settings().PostGetMeta_TenantSchedSetting_Save(new() { TenantId = tenant.cTenantId, Enable = TenantSchedEnable.Enabled, Mode = TenantSchedMode.Template, TemplateId = id });
      Assert.Equal(409, await Status(() => Settings().PostGetMeta_TenantSchedSetting_Save(new() { TenantId = tenant.cTenantId, Enable = TenantSchedEnable.Disabled })));
      Assert.Equal(409, await Status(() => Settings().PostGetMeta_TenantSchedSetting_Save(new() { TenantId = tenant.cTenantId, Enable = TenantSchedEnable.Disabled, ustamp = saved.ustamp.AddSeconds(-1) })));

      // A deactivated template stays where it is saved, warns, and cannot be chosen anew.
      var edit = await Templates().GetMeta_TenantSchedTemplate(id);
      edit.Template.cTenantSchedTemplateState = ta_TenantSchedTemplate.StateInactive;
      await Templates().PostGetMeta_TenantSchedTemplate_Save(edit);
      Assert.True((await Settings().GetMeta_TenantSchedSetting_Effective(tenant.cTenantId, null)).HasInactiveTemplate);
      saved = await Settings().PostGetMeta_TenantSchedSetting_Save(new() { TenantId = tenant.cTenantId, Enable = TenantSchedEnable.Enabled, Mode = TenantSchedMode.Template, TemplateId = id, ustamp = saved.ustamp });
      Assert.Equal(400, await Status(() => Settings().PostGetMeta_TenantSchedSetting_Save(new() { TenantId = tenant.cTenantId, DeptId = dept.cTenantDeptId, Mode = TenantSchedMode.Template, TemplateId = id })));
   }

   [Fact]
   public async Task AttendanceCodesFollowPositions() {
      var source = await Tenant("t-code"); var target = await Tenant("t-code2");
      var sick = await Codes().PostGetTa_TenantAttCode_New(new() { cTenantId = source.cTenantId, cTenantAttCodeCode = "SCK", cTenantAttCodeName = "Sick", cTenantAttCodeCategory = TenantAttCodeCategory.PaidAbsence, cTenantAttCodeOrder = 30 });
      var present = await Codes().PostGetTa_TenantAttCode_New(new() { cTenantId = source.cTenantId, cTenantAttCodeCode = "PRS", cTenantAttCodeName = "Present", cTenantAttCodeOrder = 10 });
      var off = await Codes().PostGetTa_TenantAttCode_New(new() { cTenantId = source.cTenantId, cTenantAttCodeCode = "OLD", cTenantAttCodeName = "Old", cTenantAttCodeState = ta_TenantAttCode.StateInactive });
      Assert.Equal(["OLD", "PRS", "SCK"], (await Codes().GetVi_TenantAttCodes(source.cTenantId)).Select(x => x.cTenantAttCodeCode));
      Assert.Equal(409, await Status(() => Codes().PostGetTa_TenantAttCode_New(new() { cTenantId = source.cTenantId, cTenantAttCodeCode = "sck", cTenantAttCodeName = "Twice" })));
      Assert.Equal(400, await Status(() => Codes().PostGetTa_TenantAttCode_New(new() { cTenantId = source.cTenantId, cTenantAttCodeCode = "X", cTenantAttCodeName = "X", cTenantAttCodeCategory = (TenantAttCodeCategory)9 })));

      await Codes().PostGetTa_TenantAttCode_New(new() { cTenantId = target.cTenantId, cTenantAttCodeCode = "PRS", cTenantAttCodeName = "Mine" });
      var created = await Codes().PostGetMeta_TenantAttCode_Copy(new() { SourceTenantId = source.cTenantId, TargetTenantId = target.cTenantId, CodeIds = [sick.cTenantAttCodeId, present.cTenantAttCodeId, off.cTenantAttCodeId] });
      var copy = Assert.Single(created);
      Assert.Equal("SCK", copy.cTenantAttCodeCode); Assert.Equal(TenantAttCodeCategory.PaidAbsence, copy.cTenantAttCodeCategory); Assert.Equal(30, copy.cTenantAttCodeOrder);
      Assert.Equal(400, await Status(() => Codes().PostGetMeta_TenantAttCode_Copy(new() { SourceTenantId = source.cTenantId, TargetTenantId = source.cTenantId })));

      var update = new ta_TenantAttCode {
         cTenantAttCodeId = sick.cTenantAttCodeId, cTenantId = sick.cTenantId, cTenantAttCodeCode = "SCK", cTenantAttCodeName = "Sick leave",
         cTenantAttCodeCategory = TenantAttCodeCategory.UnpaidAbsence, cTenantAttCodeState = ta_TenantAttCode.StateInactive, ustamp = sick.ustamp
      };
      var changed = await Codes().PostGetTa_TenantAttCode_Update(update);
      Assert.Equal(TenantAttCodeCategory.UnpaidAbsence, changed.cTenantAttCodeCategory); Assert.Equal(ta_TenantAttCode.StateInactive, changed.cTenantAttCodeState);
      Assert.Equal(409, await Status(() => Codes().PostGetTa_TenantAttCode_Update(update)));
   }

   [Fact]
   public async Task HolidaysAreGlobalFixedAndDated() {
      var year = 2031;
      var fixedDay = await Holidays().PostGetTa_PublicHoliday_New(new() { cPublicHolidayName = "Founders " + year, cPublicHolidayKind = PublicHolidayKind.Fixed, cPublicHolidayMonth = 2, cPublicHolidayDay = 29, cPublicHolidayDate = new DateTime(2030, 1, 1) });
      Assert.Null(fixedDay.cPublicHolidayDate);
      var dated = await Holidays().PostGetTa_PublicHoliday_New(new() { cPublicHolidayName = "Moon " + year, cPublicHolidayKind = PublicHolidayKind.Dated, cPublicHolidayDate = new DateTime(year, 3, 20), cPublicHolidayMonth = 1, cPublicHolidayCollective = true });
      Assert.Null(dated.cPublicHolidayMonth); Assert.True(dated.cPublicHolidayCollective);
      Assert.False(await Holidays().GetMeta_PublicHoliday_HasDated(year + 1)); Assert.True(await Holidays().GetMeta_PublicHoliday_HasDated(year));
      Assert.Equal(400, await Status(() => Holidays().PostGetTa_PublicHoliday_New(new() { cPublicHolidayName = "Bad", cPublicHolidayKind = PublicHolidayKind.Fixed, cPublicHolidayMonth = 2, cPublicHolidayDay = 30 })));
      Assert.Equal(400, await Status(() => Holidays().PostGetTa_PublicHoliday_New(new() { cPublicHolidayName = "Bad", cPublicHolidayKind = PublicHolidayKind.Dated })));
      Assert.Equal(400, await Status(() => Holidays().PostGetTa_PublicHoliday_New(new() { cPublicHolidayName = " ", cPublicHolidayKind = PublicHolidayKind.Dated, cPublicHolidayDate = new DateTime(year, 1, 1) })));
      Assert.Equal(409, await Status(() => Holidays().PostGetTa_PublicHoliday_New(new() { cPublicHolidayName = "Moon " + year, cPublicHolidayKind = PublicHolidayKind.Dated, cPublicHolidayDate = new DateTime(year, 3, 20) })));
      Assert.Equal(409, await Status(() => Holidays().PostGetTa_PublicHoliday_New(new() { cPublicHolidayName = "Founders " + year, cPublicHolidayKind = PublicHolidayKind.Fixed, cPublicHolidayMonth = 2, cPublicHolidayDay = 29 })));

      var names = (await Holidays().GetVi_PublicHolidays(year)).Select(x => x.cPublicHolidayName).ToArray();
      Assert.Equal(["Founders " + year, "Moon " + year], names);
      Assert.DoesNotContain("Moon " + year, (await Holidays().GetVi_PublicHolidays(year + 1)).Select(x => x.cPublicHolidayName));

      var removed = new ta_PublicHoliday {
         cPublicHolidayId = fixedDay.cPublicHolidayId, cPublicHolidayName = fixedDay.cPublicHolidayName, cPublicHolidayKind = fixedDay.cPublicHolidayKind, cPublicHolidayMonth = 2, cPublicHolidayDay = 29,
         cPublicHolidayState = ta_PublicHoliday.StateRemoved, ustamp = fixedDay.ustamp
      };
      await Holidays().PostGetTa_PublicHoliday_Update(removed);
      Assert.Equal(["Moon " + year], (await Holidays().GetVi_PublicHolidays(year)).Select(x => x.cPublicHolidayName));
      Assert.Equal(409, await Status(() => Holidays().PostGetTa_PublicHoliday_Update(removed)));
   }

   [Fact]
   public async Task DatedHolidaysCopyToAnotherYear() {
      const int leap = 2036;
      await Holidays().PostGetTa_PublicHoliday_New(new() { cPublicHolidayName = "Leap", cPublicHolidayKind = PublicHolidayKind.Dated, cPublicHolidayDate = new DateTime(leap, 2, 29) });
      await Holidays().PostGetTa_PublicHoliday_New(new() { cPublicHolidayName = "Plain", cPublicHolidayKind = PublicHolidayKind.Dated, cPublicHolidayDate = new DateTime(leap, 6, 1), cPublicHolidayCollective = true });
      await Holidays().PostGetTa_PublicHoliday_New(new() { cPublicHolidayName = "Plain", cPublicHolidayKind = PublicHolidayKind.Dated, cPublicHolidayDate = new DateTime(leap, 7, 1) });
      await Holidays().PostGetTa_PublicHoliday_New(new() { cPublicHolidayName = "Plain", cPublicHolidayKind = PublicHolidayKind.Dated, cPublicHolidayDate = new DateTime(leap + 1, 7, 1) });
      var result = await Holidays().PostGetMeta_PublicHoliday_CopyDated(leap, leap + 1);
      var created = Assert.Single(result.Created);
      Assert.Equal(new DateTime(leap + 1, 6, 1), created.cPublicHolidayDate); Assert.True(created.cPublicHolidayCollective); Assert.Equal(2, result.Skipped);
      Assert.Equal(400, await Status(() => Holidays().PostGetMeta_PublicHoliday_CopyDated(leap, leap)));
      Assert.Equal(400, await Status(() => Holidays().GetVi_PublicHolidays(1)));
   }
}
