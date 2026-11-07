using System.Data;
using System.Globalization;
using Em.Api.Core.Log.Audit;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core;

/// <summary>Scheduling settings of a tenant and its departments, and what applies to each level after inheritance.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantSchedSettingServices(ApiCoreContext ctx) : ServicesBase, ITenantSchedSettingServices
{
   private const string ChangedMessage = "The setting changed. Refresh before saving.";

   #region Meta's
   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public async Task<TenantSchedSettingRow[]> GetMeta_TenantSchedSettings(string tenantId) {
      var data = await Load(tenantId);
      var rows = new List<TenantSchedSettingRow> { new() {
         TenantId = tenantId, Code = data.Tenant.cTenantCode, Name = data.Tenant.cTenantName,
         Setting = data.Setting(tenantId), Steps = data.OwnSteps(tenantId), Effective = data.Effective(null)
      } };
      rows.AddRange(data.Departments.Values.OrderBy(x => x.cTenantDeptOrder).ThenBy(x => x.cTenantDeptName, StringComparer.OrdinalIgnoreCase).Select(x => new TenantSchedSettingRow {
         TenantId = tenantId, DeptId = x.cTenantDeptId, ParentDeptId = x.cTenantDeptParent_cTenantDeptId, Code = x.cTenantDeptCode, Name = x.cTenantDeptName,
         IsInactive = x.cTenantDeptState < ta_TenantDept.StateActive, Order = x.cTenantDeptOrder,
         Setting = data.Setting(x.cTenantDeptId), Steps = data.OwnSteps(x.cTenantDeptId), Effective = data.Effective(x.cTenantDeptId)
      }));
      return [.. rows];
   }

   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public async Task<TenantSchedEffective> GetMeta_TenantSchedSetting_Effective(string tenantId, string? deptId) {
      var data = await Load(tenantId);
      var id = TenantOrg.Optional(deptId);
      if (id is not null && !data.Departments.ContainsKey(id)) throw new ActionException("The department does not belong to this tenant.", 400);
      return data.Effective(id);
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantSchedSetting> PostGetMeta_TenantSchedSetting_Save(TenantSchedSettingSave save) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var deptId = TenantOrg.Optional(save.DeptId);
      await ValidateLevel(save.TenantId, deptId);
      ValidateValues(save, deptId);
      var key = deptId ?? save.TenantId;
      var row = await ctx.ta_TenantSchedSettings.SingleOrDefaultAsync(x => x.cTenantId == save.TenantId && x.cTenantSchedSettingKey == key, AbortToken);
      if (row is not null ? row.ustamp != save.ustamp : save.ustamp is not null) throw new ActionException(ChangedMessage, 409);
      var steps = row is null ? [] : await ctx.ta_TenantSchedSettingSteps.Where(x => x.cTenantSchedSettingId == row.cTenantSchedSettingId).ToArrayAsync(AbortToken);
      await ValidateTemplates(save, row, steps);
      var now = DateTime.UtcNow;
      if (row is null) {
         row = new ta_TenantSchedSetting {
            cTenantSchedSettingId = Ulid.NewUlid().ToString(), cTenantId = save.TenantId, cTenantDeptId = deptId, cTenantSchedSettingKey = key, datestamp = now, ustamp = now
         };
         ctx.ta_TenantSchedSettings.Add(row);
      }
      else row.ustamp = TenantOrg.Stamp(row.ustamp);
      var isTemplate = save.Mode == TenantSchedMode.Template; var isRotation = save.Mode == TenantSchedMode.Rotation;
      row.cTenantSchedSettingEnable = save.Enable; row.cTenantSchedSettingMode = save.Mode;
      row.cTenantSchedTemplateId = isTemplate ? save.TemplateId : null;
      row.cTenantSchedSettingStepWeeks = isRotation ? save.StepWeeks : null;
      row.cTenantSchedSettingCycleStart = isRotation ? save.CycleStart!.Value.Date : null;
      row.cTenantSchedSettingPeriodDays = deptId is null ? save.PeriodDays : null;
      // The setting goes first: the model has no navigation that would order the step inserts after it.
      await TenantOrg.Save(() => ctx.SaveChangesAsync(AbortToken), ChangedMessage);
      ApplySteps(row, steps, isRotation ? save.StepTemplateIds : [], now);
      Audit(row.cTenantSchedSettingId, "Saved scheduling setting of " + (deptId is null ? "the tenant" : "a department"));
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await Read(row.cTenantSchedSettingId);
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantSchedSetting?> PostGetMeta_TenantSchedSetting_UseInherited(string tenantId, string deptId, DateTime ustamp) {
      if (string.IsNullOrWhiteSpace(deptId)) throw new ActionException("Only a department can inherit.", 400);
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      await ValidateLevel(tenantId, deptId);
      var row = await ctx.ta_TenantSchedSettings.SingleOrDefaultAsync(x => x.cTenantId == tenantId && x.cTenantSchedSettingKey == deptId, AbortToken);
      if (row is null) return null;
      if (row.ustamp != ustamp) throw new ActionException(ChangedMessage, 409);
      var steps = await ctx.ta_TenantSchedSettingSteps.Where(x => x.cTenantSchedSettingId == row.cTenantSchedSettingId).ToArrayAsync(AbortToken);
      row.cTenantSchedSettingEnable = TenantSchedEnable.Inherit; row.cTenantSchedSettingMode = TenantSchedMode.Inherit;
      row.cTenantSchedTemplateId = null; row.cTenantSchedSettingStepWeeks = null; row.cTenantSchedSettingCycleStart = null;
      row.ustamp = TenantOrg.Stamp(row.ustamp);
      ApplySteps(row, steps, [], DateTime.UtcNow);
      Audit(row.cTenantSchedSettingId, "Set a department back to the inherited schedule");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await Read(row.cTenantSchedSettingId);
   }
   #endregion

   // Steps are never deleted: surplus orders go inactive and come back when the rotation grows.
   private void ApplySteps(ta_TenantSchedSetting setting, ta_TenantSchedSettingStep[] saved, string[] templateIds, DateTime now) {
      var byOrder = saved.ToDictionary(x => x.cTenantSchedSettingStepOrder);
      for (var i = 0; i < templateIds.Length; i++) {
         var order = i + 1;
         if (byOrder.Remove(order, out var step)) {
            step.cTenantSchedTemplateId = templateIds[i]; step.cTenantSchedSettingStepState = ta_TenantSchedSettingStep.StateActive; step.ustamp = now;
         }
         else ctx.ta_TenantSchedSettingSteps.Add(new ta_TenantSchedSettingStep {
            cTenantSchedSettingStepId = Ulid.NewUlid().ToString(), cTenantSchedSettingId = setting.cTenantSchedSettingId, cTenantId = setting.cTenantId,
            cTenantSchedSettingStepOrder = order, cTenantSchedTemplateId = templateIds[i], cTenantSchedSettingStepState = ta_TenantSchedSettingStep.StateActive, datestamp = now, ustamp = now
         });
      }
      foreach (var surplus in byOrder.Values.Where(x => x.cTenantSchedSettingStepState != ta_TenantSchedSettingStep.StateInactive)) {
         surplus.cTenantSchedSettingStepState = ta_TenantSchedSettingStep.StateInactive; surplus.ustamp = now;
      }
   }

   private async Task ValidateLevel(string tenantId, string? deptId) {
      if (!await ctx.ta_Tenants.AnyAsync(x => x.cTenantId == tenantId, AbortToken)) throw new ActionException("The tenant was not found.", 400);
      if (deptId is not null && !await ctx.ta_TenantDepts.AnyAsync(x => x.cTenantDeptId == deptId && x.cTenantId == tenantId, AbortToken))
         throw new ActionException("The department does not belong to this tenant.", 400);
   }

   private static void ValidateValues(TenantSchedSettingSave save, string? deptId) {
      if (!Enum.IsDefined(save.Enable)) throw new ActionException("The Enable value is not valid.", 400);
      if (deptId is null && save.Enable == TenantSchedEnable.Inherit) throw new ActionException("The tenant level is either enabled or disabled.", 400);
      if (!Enum.IsDefined(save.Mode)) throw new ActionException("The default schedule is not valid.", 400);
      if (deptId is not null && save.PeriodDays is not null) throw new ActionException("The period length is set on the tenant level only.", 400);
      if (save.PeriodDays is < 1 or > 366) throw new ActionException("The period length must be from 1 to 366 days.", 400);
      if (save.Mode != TenantSchedMode.Rotation) return;
      if (save.StepTemplateIds.Length < 2) throw new ActionException("A rotation needs at least two steps.", 400);
      if (save.StepWeeks is not >= 1) throw new ActionException("A rotation needs at least one week per step.", 400);
      if (save.CycleStart is null) throw new ActionException("A rotation needs its Day 1.", 400);
   }

   // A template that was deactivated later may stay where it is already used; it cannot be newly chosen.
   private async Task ValidateTemplates(TenantSchedSettingSave save, ta_TenantSchedSetting? row, ta_TenantSchedSettingStep[] steps) {
      if (save.Mode == TenantSchedMode.Template) {
         if (string.IsNullOrWhiteSpace(save.TemplateId)) throw new ActionException("Choose a template.", 400);
         await RequireTemplates(save.TenantId, [save.TemplateId], row?.cTenantSchedTemplateId is { } saved ? [saved] : []);
      }
      else if (save.Mode == TenantSchedMode.Rotation) {
         if (save.StepTemplateIds.Any(string.IsNullOrWhiteSpace)) throw new ActionException("Choose a template for every step.", 400);
         await RequireTemplates(save.TenantId, save.StepTemplateIds,
            [.. steps.Where(x => x.cTenantSchedSettingStepState == ta_TenantSchedSettingStep.StateActive).Select(x => x.cTenantSchedTemplateId)]);
      }
   }

   private async Task RequireTemplates(string tenantId, string[] ids, string[] alreadySaved) {
      var found = await ctx.ta_TenantSchedTemplates.AsNoTracking().Where(x => x.cTenantId == tenantId && ids.Contains(x.cTenantSchedTemplateId))
         .ToDictionaryAsync(x => x.cTenantSchedTemplateId, StringComparer.OrdinalIgnoreCase, AbortToken);
      foreach (var id in ids) {
         if (!found.TryGetValue(id, out var template)) throw new ActionException("The template does not belong to this tenant.", 400);
         if (template.cTenantSchedTemplateState != ta_TenantSchedTemplate.StateActive && !alreadySaved.Contains(id, StringComparer.OrdinalIgnoreCase))
            throw new ActionException($"The template '{template.cTenantSchedTemplateCode}' is not active.", 400);
      }
   }

   private Task<vi_TenantSchedSetting> Read(string id) => ctx.vi_TenantSchedSettings.AsNoTracking().SingleAsync(x => x.cTenantSchedSettingId == id, AbortToken);

   private async Task<SchedData> Load(string tenantId) {
      var tenant = await ctx.ta_Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.cTenantId == tenantId, AbortToken)
         ?? throw new ActionException("The tenant was not found.", 404);
      var departments = await ctx.ta_TenantDepts.AsNoTracking().Where(x => x.cTenantId == tenantId).ToDictionaryAsync(x => x.cTenantDeptId, StringComparer.OrdinalIgnoreCase, AbortToken);
      var settings = await ctx.vi_TenantSchedSettings.AsNoTracking().Where(x => x.cTenantId == tenantId).ToDictionaryAsync(x => x.cTenantSchedSettingKey, StringComparer.OrdinalIgnoreCase, AbortToken);
      var templates = await ctx.ta_TenantSchedTemplates.AsNoTracking().Where(x => x.cTenantId == tenantId).ToDictionaryAsync(x => x.cTenantSchedTemplateId, StringComparer.OrdinalIgnoreCase, AbortToken);
      var steps = await ctx.ta_TenantSchedSettingSteps.AsNoTracking().Where(x => x.cTenantId == tenantId && x.cTenantSchedSettingStepState == ta_TenantSchedSettingStep.StateActive)
         .OrderBy(x => x.cTenantSchedSettingStepOrder).ToArrayAsync(AbortToken);
      return new SchedData(tenant, departments, settings, templates, steps.ToLookup(x => x.cTenantSchedSettingId, StringComparer.OrdinalIgnoreCase));
   }

   private void Audit(string id, string summary) {
      if (App is not null) GetService<IAuditWriter>()?.Use(ctx).AddEntry(new AuditEntryDraft {
         Group = "System.Settings", Key = id, Kind = "tenant.sched.setting.changed", Summary = summary, TargetType = "scheduling setting"
      });
   }

   // One tenant read once; every effective result is then calculated in memory by walking the parent chain.
   private sealed record SchedData(ta_Tenant Tenant, Dictionary<string, ta_TenantDept> Departments, Dictionary<string, vi_TenantSchedSetting> Settings,
      Dictionary<string, ta_TenantSchedTemplate> Templates, ILookup<string, ta_TenantSchedSettingStep> Steps)
   {
      internal vi_TenantSchedSetting? Setting(string key) => Settings.GetValueOrDefault(key);

      internal TenantSchedEffectiveStep[] OwnSteps(string key) => Setting(key) is { cTenantSchedSettingMode: TenantSchedMode.Rotation } setting ? StepsOf(setting) : [];

      private TenantSchedEffectiveStep[] StepsOf(ta_TenantSchedSetting setting) => [.. Steps[setting.cTenantSchedSettingId].Select(x => {
         var template = Templates.GetValueOrDefault(x.cTenantSchedTemplateId);
         return new TenantSchedEffectiveStep {
            Order = x.cTenantSchedSettingStepOrder, TemplateId = x.cTenantSchedTemplateId, TemplateCode = template?.cTenantSchedTemplateCode ?? "",
            TemplateName = template?.cTenantSchedTemplateName ?? "", IsTemplateActive = template?.cTenantSchedTemplateState == ta_TenantSchedTemplate.StateActive
         };
      })];

      // Nearest level first, the department itself included. A cycle written outside the screens must not hang the read.
      private IEnumerable<ta_TenantDept> Chain(string? deptId) {
         var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
         for (var id = deptId; id is not null && seen.Add(id) && Departments.TryGetValue(id, out var dept); id = dept.cTenantDeptParent_cTenantDeptId) yield return dept;
      }

      internal TenantSchedEffective Effective(string? deptId) {
         var tenantRow = Setting(Tenant.cTenantId);
         ta_TenantDept? enableLevel = null, scheduleLevel = null;
         vi_TenantSchedSetting? enableRow = null, scheduleRow = null;
         foreach (var dept in Chain(deptId)) {
            if (Setting(dept.cTenantDeptId) is not { } row) continue;
            if (enableRow is null && row.cTenantSchedSettingEnable != TenantSchedEnable.Inherit) { enableRow = row; enableLevel = dept; }
            if (scheduleRow is null && row.cTenantSchedSettingMode != TenantSchedMode.Inherit) { scheduleRow = row; scheduleLevel = dept; }
         }
         enableRow ??= tenantRow; scheduleRow ??= tenantRow;
         var result = new TenantSchedEffective {
            // A tenant without a row is disabled.
            Enabled = enableRow?.cTenantSchedSettingEnable == TenantSchedEnable.Enabled,
            EnableSourceDeptId = enableLevel?.cTenantDeptId, EnableSource = enableLevel?.cTenantDeptName ?? Tenant.cTenantName,
            Mode = scheduleRow?.cTenantSchedSettingMode ?? TenantSchedMode.BuiltIn,
            ScheduleSourceDeptId = scheduleLevel?.cTenantDeptId, ScheduleSource = scheduleLevel?.cTenantDeptName ?? Tenant.cTenantName,
            PeriodDays = tenantRow?.cTenantSchedSettingPeriodDays ?? TenantSchedBuiltIn.DefaultPeriodDays
         };
         if (result.Mode == TenantSchedMode.Template && Templates.GetValueOrDefault(scheduleRow!.cTenantSchedTemplateId ?? "") is { } template) {
            result.TemplateId = template.cTenantSchedTemplateId; result.TemplateCode = template.cTenantSchedTemplateCode; result.TemplateName = template.cTenantSchedTemplateName;
            result.HasInactiveTemplate = template.cTenantSchedTemplateState != ta_TenantSchedTemplate.StateActive;
         }
         else if (result.Mode == TenantSchedMode.Rotation) {
            result.Steps = StepsOf(scheduleRow!); result.StepWeeks = scheduleRow!.cTenantSchedSettingStepWeeks; result.CycleStart = scheduleRow.cTenantSchedSettingCycleStart;
            result.HasInactiveTemplate = result.Steps.Any(x => !x.IsTemplateActive);
         }
         result.Text = Describe(result, deptId);
         return result;
      }

      private static string Describe(TenantSchedEffective result, string? deptId) {
         static string Origin(string? sourceDeptId, string source, string? deptId) => TenantOrg.Same(sourceDeptId, deptId) ? "set on this level" : "inherited from " + source;
         if (!result.Enabled) return "Disabled — " + Origin(result.EnableSourceDeptId, result.EnableSource, deptId) + ".";
         var origin = Origin(result.ScheduleSourceDeptId, result.ScheduleSource, deptId);
         return result.Mode switch {
            TenantSchedMode.Template => $"Enabled · {result.TemplateName} — {origin}.",
            TenantSchedMode.Rotation => $"Enabled · Rotation — {origin}: {string.Join(" → ", result.Steps.Select(x => x.TemplateName))}, " +
               $"{result.StepWeeks} {(result.StepWeeks == 1 ? "week" : "weeks")} per step from {result.CycleStart?.ToString("dd-MMM-yyyy", CultureInfo.GetCultureInfo("en-US"))}.",
            _ => $"Enabled · {TenantSchedBuiltIn.Name} — {origin}: 08:00, {TenantSchedBuiltIn.BreakMinutes} minutes break, 8 hours Monday to Friday, weekend off."
         };
      }
   }
}
