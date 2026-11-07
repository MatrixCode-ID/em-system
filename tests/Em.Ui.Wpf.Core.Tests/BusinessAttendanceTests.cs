using System.Runtime.CompilerServices;
using System.Windows.Controls;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Controls;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Navigations;

namespace Em.Ui.Wpf.Core.Tests;

public class BusinessAttendanceTests
{
   private static vi_Tenant Tenant(string id = "T") => new() { cTenantId = id, cTenantName = "Tenant " + id, cTenantCode = id };

   private sealed class Templates : ITenantSchedTemplateServices
   {
      public List<TenantSchedTemplateSet> Sets { get; } = [Build("A", "OFF", "Office", 8), Build("B", "NIG", "Night", 7)];
      public TenantSchedTemplateSet? LastSave { get; private set; }

      private static TenantSchedTemplateSet Build(string id, string code, string name, decimal hours) => new() {
         Template = new() { cTenantSchedTemplateId = id, cTenantId = "T", cTenantSchedTemplateCode = code, cTenantSchedTemplateName = name, cTenantSchedTemplateStart = TimeSpan.FromHours(8), cTenantSchedTemplateBreak = 60, ccTenantSchedTemplateWeekHours = hours * 5 },
         Days = [.. Enumerable.Range(1, 7).Select(d => new ta_TenantSchedTemplateDay { cTenantSchedTemplateId = id, cTenantSchedTemplateDayWeekday = d, cTenantSchedTemplateDayHours = d <= 5 ? hours : 0 })]
      };

      public Task<vi_TenantSchedTemplate[]> GetVi_TenantSchedTemplates(string tenantId) => Task.FromResult(Sets.Where(x => x.Template.cTenantId == tenantId).Select(x => x.Template).ToArray());
      public Task<TenantSchedTemplateSet> GetMeta_TenantSchedTemplate(string templateId) => Task.FromResult(Sets.Single(x => x.Template.cTenantSchedTemplateId == templateId));
      public Task<TenantSchedTemplateSet> PostGetMeta_TenantSchedTemplate_Save(TenantSchedTemplateSet set) {
         LastSave = set;
         if (set.Template.cTenantSchedTemplateId.Length == 0) { set.Template.cTenantSchedTemplateId = "N"; Sets.Add(set); return Task.FromResult(set); }
         Sets[Sets.FindIndex(x => x.Template.cTenantSchedTemplateId == set.Template.cTenantSchedTemplateId)] = set;
         return Task.FromResult(set);
      }
      public Task<TenantSchedTemplateSet> PostGetMeta_TenantSchedTemplate_Duplicate(string templateId) {
         var copy = Build("C", "OFF2", "Office copy", 8); Sets.Add(copy); return Task.FromResult(copy);
      }
   }

   private sealed class Settings : ITenantSchedSettingServices
   {
      public TenantSchedSettingSave? LastSave { get; private set; }
      public string? Inherited { get; private set; }
      public List<TenantSchedSettingRow> Rows { get; } = [
         new() { TenantId = "T", Code = "T", Name = "Tenant T", Effective = new() { Text = "Disabled" } },
         new() { TenantId = "T", DeptId = "D1", Code = "D1", Name = "Production", Effective = new() { Enabled = true, ScheduleSource = "Production", Mode = TenantSchedMode.Template, TemplateName = "Office", HasInactiveTemplate = true, Text = "Enabled" },
            Setting = new() { cTenantSchedSettingId = "S", cTenantId = "T", cTenantDeptId = "D1", cTenantSchedSettingMode = TenantSchedMode.Template, cTenantSchedTemplateId = "A", cTenantSchedSettingEnable = TenantSchedEnable.Enabled, ustamp = new DateTime(2026, 1, 1) } },
         new() { TenantId = "T", DeptId = "D2", ParentDeptId = "D1", Code = "D2", Name = "Line 1", Effective = new() { Enabled = true, ScheduleSource = "Production" } }];
      public Task<TenantSchedSettingRow[]> GetMeta_TenantSchedSettings(string tenantId) => Task.FromResult(Rows.ToArray());
      public Task<TenantSchedEffective> GetMeta_TenantSchedSetting_Effective(string tenantId, string? deptId) => Task.FromResult(new TenantSchedEffective());
      public Task<vi_TenantSchedSetting> PostGetMeta_TenantSchedSetting_Save(TenantSchedSettingSave save) { LastSave = save; return Task.FromResult(new vi_TenantSchedSetting()); }
      public Task<vi_TenantSchedSetting?> PostGetMeta_TenantSchedSetting_UseInherited(string tenantId, string deptId, DateTime ustamp) { Inherited = deptId; return Task.FromResult<vi_TenantSchedSetting?>(null); }
   }

   private sealed class Codes : ITenantAttCodeServices
   {
      public List<vi_TenantAttCode> Rows { get; } = [
         new() { cTenantAttCodeId = "A", cTenantId = "T", cTenantAttCodeCode = "SCK", cTenantAttCodeName = "Sick", cTenantAttCodeCategory = TenantAttCodeCategory.PaidAbsence },
         new() { cTenantAttCodeId = "C", cTenantId = "S", cTenantAttCodeCode = "SCK", cTenantAttCodeName = "Sick" },
         new() { cTenantAttCodeId = "D", cTenantId = "S", cTenantAttCodeCode = "PRS", cTenantAttCodeName = "Present" },
         new() { cTenantAttCodeId = "E", cTenantId = "S", cTenantAttCodeCode = "OFF", cTenantAttCodeName = "Inactive", cTenantAttCodeState = ta_TenantAttCode.StateInactive }];
      public TenantAttCodeCopy? LastCopy { get; private set; }
      public Task<vi_TenantAttCode> PostGetTa_TenantAttCode_New(ta_TenantAttCode row) {
         var saved = new vi_TenantAttCode { cTenantAttCodeId = "N", cTenantId = row.cTenantId, cTenantAttCodeCode = row.cTenantAttCodeCode, cTenantAttCodeName = row.cTenantAttCodeName, cTenantAttCodeCategory = row.cTenantAttCodeCategory };
         Rows.Add(saved); return Task.FromResult(saved);
      }
      public Task<vi_TenantAttCode> PostGetTa_TenantAttCode_Update(ta_TenantAttCode row) {
         var saved = Rows.Single(x => x.cTenantAttCodeId == row.cTenantAttCodeId);
         saved.cTenantAttCodeState = row.cTenantAttCodeState; return Task.FromResult(saved);
      }
      public Task<vi_TenantAttCode[]> GetVi_TenantAttCodes(string tenantId) => Task.FromResult(Rows.Where(x => x.cTenantId == tenantId).ToArray());
      public Task<vi_TenantAttCode[]> PostGetMeta_TenantAttCode_Copy(TenantAttCodeCopy copy) { LastCopy = copy; return Task.FromResult<vi_TenantAttCode[]>([Rows[0]]); }
   }

   private sealed class Holidays : IPublicHolidayServices
   {
      public List<vi_PublicHoliday> Rows { get; } = [
         new() { cPublicHolidayId = "A", cPublicHolidayName = "New Year", cPublicHolidayKind = PublicHolidayKind.Fixed, cPublicHolidayMonth = 1, cPublicHolidayDay = 1 },
         new() { cPublicHolidayId = "B", cPublicHolidayName = "Moon", cPublicHolidayKind = PublicHolidayKind.Dated, cPublicHolidayDate = new DateTime(DateTime.Today.Year, 3, 20), cPublicHolidayCollective = true }];
      public bool HasDated { get; set; } = true;
      public (int From, int To)? LastCopy { get; private set; }
      public ta_PublicHoliday? LastUpdate { get; private set; }
      public Task<vi_PublicHoliday> PostGetTa_PublicHoliday_New(ta_PublicHoliday row) {
         var saved = new vi_PublicHoliday { cPublicHolidayId = "N", cPublicHolidayName = row.cPublicHolidayName, cPublicHolidayKind = row.cPublicHolidayKind, cPublicHolidayDate = row.cPublicHolidayDate, cPublicHolidayMonth = row.cPublicHolidayMonth, cPublicHolidayDay = row.cPublicHolidayDay };
         Rows.Add(saved); return Task.FromResult(saved);
      }
      public Task<vi_PublicHoliday> PostGetTa_PublicHoliday_Update(ta_PublicHoliday row) {
         LastUpdate = row;
         var saved = Rows.Single(x => x.cPublicHolidayId == row.cPublicHolidayId);
         if (row.cPublicHolidayState == ta_PublicHoliday.StateRemoved) Rows.Remove(saved);
         return Task.FromResult(saved);
      }
      public Task<vi_PublicHoliday[]> GetVi_PublicHolidays(int year) => Task.FromResult(Rows.ToArray());
      public Task<PublicHolidayCopyResult> PostGetMeta_PublicHoliday_CopyDated(int fromYear, int toYear) { LastCopy = (fromYear, toYear); return Task.FromResult(new PublicHolidayCopyResult { Skipped = 2 }); }
      public Task<bool> GetMeta_PublicHoliday_HasDated(int year) => Task.FromResult(HasDated);
   }

   private static async Task<BusinessModuleManagerSchedTemplateVm> TemplatePage(Templates service) {
      var vm = new BusinessModuleManagerSchedTemplateVm(service);
      vm.TenantChoices.Add(Tenant());
      vm.SelectedTenant = vm.TenantChoices[0]; await vm.Loading;
      return vm;
   }

   private static async Task<BusinessModuleManagerSchedSettingVm> SettingPage(Settings settings) {
      var vm = new BusinessModuleManagerSchedSettingVm(settings, new Templates());
      vm.TenantChoices.Add(Tenant());
      vm.SelectedTenant = vm.TenantChoices[0]; await vm.Loading;
      return vm;
   }

   [Fact]
   public async Task TemplatePageCreatesWithTheBuiltInHoursAndDerivesDaysOff() {
      var service = new Templates(); var vm = await TemplatePage(service);
      Assert.Equal(2, vm.Rows.Count); Assert.True(vm.HasNoSelection);
      vm.NewTemplateCommand();
      Assert.True(vm.IsDraftSelected); Assert.Equal("08:00", vm.EditStart); Assert.Equal(60, vm.EditBreak); Assert.Equal(7, vm.Days.Count);
      Assert.Equal("Mon", vm.Days[0].Caption); Assert.Equal("Sun", vm.Days[6].Caption);
      Assert.Equal("Days off: Saturday, Sunday", vm.DaysOffCaption); Assert.Equal("40 hours a week", vm.WeekHoursCaption);
      Assert.False(vm.SaveCommandAllowed());
      vm.EditCode = "HALF"; vm.EditName = "Half day"; Assert.True(vm.SaveCommandAllowed());
      vm.EditStart = "25:00"; Assert.False(vm.SaveCommandAllowed());
      vm.EditStart = "07:30"; vm.Days[5].Hours = 5; vm.Days[5].BreakMinutes = 0;
      Assert.Equal("Days off: Sunday", vm.DaysOffCaption);
      await vm.SaveCommand();
      Assert.False(vm.IsDirty); Assert.Equal("N", vm.SelectedRow!.Row.cTenantSchedTemplateId);
      Assert.Equal(TimeSpan.FromMinutes(450), service.LastSave!.Template.cTenantSchedTemplateStart);
      Assert.Equal(0, service.LastSave.Days[5].cTenantSchedTemplateDayBreak); Assert.Null(service.LastSave.Days[0].cTenantSchedTemplateDayBreak);
   }

   [Fact]
   public async Task TemplatePageLoadsDaysDuplicatesAndDeactivates() {
      var service = new Templates(); var vm = await TemplatePage(service);
      vm.SelectedRow = vm.Rows[0]; await vm.Loading;
      Assert.False(vm.IsDirty); Assert.Equal("OFF", vm.EditCode); Assert.Equal(8m, vm.Days[0].Hours);
      vm.Days[0].Hours = 7;
      Assert.True(vm.IsDirty); Assert.False(vm.DuplicateCommandAllowed());
      vm.DiscardChanges();
      Assert.False(vm.IsDirty); Assert.Equal(8m, vm.Days[0].Hours);
      await vm.DuplicateCommand();
      Assert.Equal("C", vm.SelectedRow!.Row.cTenantSchedTemplateId);
      Assert.Equal("Deactivate", vm.ToggleStateCaption);
      await vm.ToggleStateCommand();
      Assert.Equal(ta_TenantSchedTemplate.StateInactive, service.LastSave!.Template.cTenantSchedTemplateState);
   }

   [Fact]
   public async Task UnsavedTemplateBlocksSelectionWhenDiscardIsRefused() {
      var vm = await TemplatePage(new Templates());
      vm.ConfirmDiscard = () => false;
      vm.NewTemplateCommand();
      var draft = vm.SelectedRow;
      vm.SelectedRow = vm.Rows[0];
      Assert.Same(draft, vm.SelectedRow);
   }

   [Fact]
   public async Task SettingPageBuildsTheTreeAndOffersTheChoicesOfTheLevel() {
      var vm = await SettingPage(new Settings());
      var tenant = Assert.Single(vm.RootNodes);
      Assert.True(tenant.IsTenant); Assert.Same(tenant, vm.SelectedNode); Assert.True(tenant.IsDisabled);
      var production = Assert.Single(tenant.Children); var line = Assert.Single(production.Children);
      Assert.Equal("Dept", production.LevelCaption); Assert.Equal("Sub-dept", line.LevelCaption); Assert.Equal("Inherits Production", line.Subtitle);
      Assert.Equal(["Enabled", "Disabled"], vm.EnableOptions.Select(x => x.Caption)); Assert.Equal(["Built-in", "Template", "Rotation"], vm.ModeOptions.Select(x => x.Caption));
      Assert.Equal(TenantSchedEnable.Disabled, vm.EditEnable!.Value); Assert.True(vm.IsTenantLevel); Assert.False(vm.IsDirty);

      production.IsSelected = true;
      Assert.Same(production, vm.SelectedNode); Assert.False(tenant.IsSelected);
      Assert.Equal(["Inherit", "Enabled", "Disabled"], vm.EnableOptions.Select(x => x.Caption)); Assert.Equal(["Inherit", "Template", "Rotation"], vm.ModeOptions.Select(x => x.Caption));
      Assert.True(vm.IsTemplateMode); Assert.Equal("A", vm.EditTemplate!.Id); Assert.False(vm.IsTenantLevel); Assert.True(vm.HasTemplateWarning);
   }

   [Fact]
   public async Task SettingPageSavesARotationAndUsesInherited() {
      var settings = new Settings(); var vm = await SettingPage(settings);
      vm.RootNodes[0].Children[0].IsSelected = true;
      Assert.True(vm.UseInheritedCommandAllowed());
      vm.EditMode = vm.ModeOptions.Single(x => x.Value == TenantSchedMode.Rotation);
      Assert.True(vm.IsRotationMode); Assert.Equal(2, vm.Steps.Count); Assert.True(vm.IsDirty); Assert.False(vm.SaveCommandAllowed()); Assert.False(vm.UseInheritedCommandAllowed());
      vm.Steps[0].Template = vm.TemplateOptions[0]; vm.Steps[1].Template = vm.TemplateOptions[1];
      vm.EditStepWeeks = 2; vm.EditCycleStart = new DateTime(2026, 12, 21);
      Assert.True(vm.SaveCommandAllowed());
      vm.AddStepCommand();
      Assert.False(vm.SaveCommandAllowed());
      vm.RemoveStepCommand(vm.Steps[0]);
      Assert.Equal([1, 2], vm.Steps.Select(x => x.Order));
      vm.Steps[1].Template = vm.TemplateOptions[0];
      await vm.SaveCommand();
      Assert.Equal(["B", "A"], settings.LastSave!.StepTemplateIds); Assert.Equal(2, settings.LastSave.StepWeeks); Assert.Equal("D1", settings.LastSave.DeptId);
      Assert.Null(settings.LastSave.PeriodDays); Assert.Null(settings.LastSave.TemplateId);
      await vm.UseInheritedCommand();
      Assert.Equal("D1", settings.Inherited);
   }

   [Fact]
   public async Task TenantLevelSavesPeriodDaysAndRefusedDiscardKeepsTheLevel() {
      var settings = new Settings(); var vm = await SettingPage(settings);
      vm.EditEnable = vm.EnableOptions[0]; vm.EditPeriodDays = 30;
      Assert.False(vm.UseInheritedCommandAllowed());
      vm.ConfirmDiscard = () => false;
      var tenant = vm.SelectedNode;
      vm.RootNodes[0].Children[0].IsSelected = true;
      Assert.Same(tenant, vm.SelectedNode); Assert.True(tenant!.IsSelected);
      await vm.SaveCommand();
      Assert.Equal(TenantSchedEnable.Enabled, settings.LastSave!.Enable); Assert.Equal(TenantSchedMode.BuiltIn, settings.LastSave.Mode); Assert.Equal(30, settings.LastSave.PeriodDays); Assert.Null(settings.LastSave.ustamp);
   }

   [Fact]
   public async Task CodePageCreatesWithCategoryAndDeactivates() {
      var service = new Codes(); var vm = new BusinessModuleManagerAttCodeVm(service);
      vm.TenantChoices.Add(Tenant());
      vm.SelectedTenant = vm.TenantChoices[0]; await vm.Loading;
      Assert.Single(vm.Rows); Assert.False(vm.CopyFromTenantCommandAllowed());
      vm.NewCodeCommand();
      Assert.Equal(TenantAttCodeCategory.Present, vm.EditCategory!.Value); Assert.False(vm.SaveCommandAllowed());
      vm.EditCode = "TRP"; vm.EditName = "Trip"; vm.EditCategory = vm.CategoryOptions[3];
      await vm.SaveCommand();
      Assert.Equal(TenantAttCodeCategory.DayOff, vm.SelectedRow!.cTenantAttCodeCategory); Assert.False(vm.IsDirty);
      vm.SelectedRow = vm.Rows[0];
      await vm.ToggleStateCommand();
      Assert.Equal("Activate", vm.ToggleStateCaption);
   }

   [WpfFact]
   public async Task CopyDialogOffersActiveSourcesAndLocksExistingCodes() {
      var service = new Codes();
      new CopyAttCodesDialog().Close();
      var vm = new CopyAttCodesDialogVm();
      var target = Tenant(); var source = Tenant("S");
      vm.Configure(service, target, [target, source], service.Rows.Where(x => x.cTenantId == "T"));
      await vm.Loading;
      Assert.Same(source, Assert.Single(vm.SourceTenants));
      Assert.Equal(["SCK", "PRS"], vm.Items.Select(x => x.Code.cTenantAttCodeCode));
      Assert.True(vm.Items[0].Exists); Assert.False(vm.CopyCommandAllowed());
      vm.Items[1].IsChecked = true;
      Assert.Equal("Copy 1 code", vm.CopyCaption);
      vm.CopyCommand();
      Assert.Equal(["D"], vm.Result!.CodeIds); Assert.Equal("T", vm.Result.TargetTenantId);
   }

   [Fact]
   public async Task HolidayPageWarnsCopiesAndRemoves() {
      var service = new Holidays { HasDated = false }; var vm = new BusinessModuleManagerHolidayVm(service);
      await vm.ReloadAsync();
      Assert.Equal("2 entries · 1 fixed, 1 dated", vm.Summary); Assert.True(vm.HasWarning); Assert.Contains(vm.SelectedYear.ToString(), vm.Warning);
      Assert.Equal("01 Jan", vm.Rows[0].DateCaption); Assert.Equal("Fixed", vm.Rows[0].KindCaption); Assert.True(vm.Rows[1].IsCollective);

      vm.CopyFromYear = vm.SelectedYear; Assert.False(vm.CopyDatedCommandAllowed());
      vm.CopyFromYear = vm.SelectedYear - 1;
      await vm.CopyDatedCommand();
      Assert.Equal((vm.SelectedYear - 1, vm.SelectedYear), service.LastCopy); Assert.Contains("2 skipped", vm.Status);

      vm.SelectedRow = vm.Rows[1];
      vm.ConfirmRemove = _ => false;
      await vm.RemoveCommand();
      Assert.Equal(2, service.Rows.Count);
      vm.ConfirmRemove = _ => true;
      await vm.RemoveCommand();
      Assert.Equal(ta_PublicHoliday.StateRemoved, service.LastUpdate!.cPublicHolidayState); Assert.Single(vm.Rows);
   }

   [Fact]
   public async Task HolidayPageEditsBothKinds() {
      var service = new Holidays(); var vm = new BusinessModuleManagerHolidayVm(service);
      await vm.ReloadAsync();
      vm.NewHolidayCommand();
      Assert.True(vm.IsDatedKind); Assert.Equal(new DateTime(vm.SelectedYear, 1, 1), vm.EditDate); Assert.False(vm.SaveCommandAllowed());
      vm.EditName = "Fest"; Assert.True(vm.SaveCommandAllowed());
      vm.EditKind = vm.KindOptions[0];
      Assert.True(vm.IsFixedKind); Assert.False(vm.SaveCommandAllowed());
      vm.EditMonth = 8; vm.EditDay = 17;
      await vm.SaveCommand();
      var saved = service.Rows.Single(x => x.cPublicHolidayId == "N");
      Assert.Equal(PublicHolidayKind.Fixed, saved.cPublicHolidayKind); Assert.Equal(8, saved.cPublicHolidayMonth); Assert.Null(saved.cPublicHolidayDate);
   }

   [Fact]
   public void DrawerListsTheAttendanceFolder() {
      var vm = new BusinessModuleManagerVm(new BusinessModuleManagerTests.StubService());
      Assert.Equal(["Organization (3)", "People (1)", "Attendance (4)"], vm.PageNodes.Select(n => n.Caption));
      Assert.Equal(["Scheduling settings", "Schedule templates", "Attendance codes", "Public holidays"], vm.PageNodes[2].Children.Select(n => n.Title));
      vm.PageNodes[2].Children[1].IsSelected = true;
      Assert.True(vm.IsSchedTemplatesTab); Assert.False(vm.IsTenantsTab);
      vm.PageNodes[2].Children[3].IsSelected = true;
      Assert.True(vm.IsHolidaysTab);
   }

   [WpfFact]
   public void ScreenLoadsTheNewPagesWithoutRendering() {
      var view = new BusinessModuleManager((EmApp)RuntimeHelpers.GetUninitializedObject(typeof(EmApp)));
      foreach (var name in (string[])["schedLevelTree", "schedEnablePicker", "schedModePicker", "schedSteps", "schedPeriodDays", "schedStepWeeks", "schedTemplateList", "schedTemplateDays", "schedTemplateBreak", "attCodeList", "attCodeCategory", "holidayList", "holidayYear", "holidayCopyYear", "holidayKind"])
         Assert.NotNull(view.FindName(name));
      Assert.IsType<SpinBox>(view.FindName("schedPeriodDays"));
      Assert.IsType<TreeView>(view.FindName("schedLevelTree"));
   }

   [WpfFact]
   public void SpinBoxClampsRoundsAndMayStayEmpty() {
      var box = new SpinBox { Minimum = 0, Maximum = 24, Step = 0.5m, DecimalPlaces = 2, Value = 7.5m };
      Assert.Equal("7.5", box.Text);
      SpinBox.IncreaseCommand.Execute(null, box);
      Assert.Equal(8m, box.Value); Assert.Equal("8", box.Text);
      box.Value = 30; Assert.Equal(24m, box.Value);
      SpinBox.IncreaseCommand.Execute(null, box); Assert.Equal(24m, box.Value);
      box.Value = 1.239m; Assert.Equal(1.24m, box.Value);
      box.Text = "6.25"; Assert.Equal(6.25m, box.Value);
      box.Text = ""; Assert.Equal(6.25m, box.Value);

      var optional = new SpinBox { Minimum = 0, Maximum = 1440, Step = 15, AllowEmpty = true, Value = 60 };
      optional.Text = ""; Assert.Null(optional.Value);
      SpinBox.IncreaseCommand.Execute(null, optional); Assert.Equal(0m, optional.Value);
      SpinBox.IncreaseCommand.Execute(null, optional); Assert.Equal(15m, optional.Value);
      SpinBox.DecreaseCommand.Execute(null, optional); SpinBox.DecreaseCommand.Execute(null, optional); Assert.Equal(0m, optional.Value);
      optional.Value = null; Assert.Equal("", optional.Text);
   }
}
