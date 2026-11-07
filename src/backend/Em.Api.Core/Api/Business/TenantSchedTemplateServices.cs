using System.Data;
using Em.Api.Core.Log.Audit;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core;

/// <summary>Schedule templates of a tenant, each with its seven weekdays.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantSchedTemplateServices(ApiCoreContext ctx) : ServicesBase, ITenantSchedTemplateServices
{
   private const string DuplicateCode = "The template code is already used in this tenant.";

   #region Views
   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public Task<vi_TenantSchedTemplate[]> GetVi_TenantSchedTemplates(string tenantId) => ctx.vi_TenantSchedTemplates.AsNoTracking()
      .Where(x => x.cTenantId == tenantId).OrderBy(x => x.cTenantSchedTemplateOrder).ThenBy(x => x.cTenantSchedTemplateName).ThenBy(x => x.cTenantSchedTemplateId).ToArrayAsync(AbortToken);
   #endregion

   #region Meta's
   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public async Task<TenantSchedTemplateSet> GetMeta_TenantSchedTemplate(string templateId) =>
      await Read(templateId) ?? throw new ActionException("The template was not found.", 404);

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<TenantSchedTemplateSet> PostGetMeta_TenantSchedTemplate_Save(TenantSchedTemplateSet set) {
      var row = set.Template;
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      await Validate(set);
      var now = DateTime.UtcNow;
      var isNew = string.IsNullOrWhiteSpace(row.cTenantSchedTemplateId);
      ta_TenantSchedTemplate entity;
      if (isNew) {
         entity = new ta_TenantSchedTemplate { cTenantSchedTemplateId = Ulid.NewUlid().ToString(), cTenantId = row.cTenantId, datestamp = now, ustamp = now };
         ctx.ta_TenantSchedTemplates.Add(entity);
      }
      else {
         entity = await ctx.ta_TenantSchedTemplates.SingleOrDefaultAsync(x => x.cTenantSchedTemplateId == row.cTenantSchedTemplateId, AbortToken)
            ?? throw new ActionException("The template was not found.", 404);
         if (!TenantOrg.Same(entity.cTenantId, row.cTenantId)) throw new ActionException("The tenant cannot be changed.", 400);
         if (entity.ustamp != row.ustamp) throw new ActionException("The template changed. Refresh before saving.", 409);
         entity.ustamp = TenantOrg.Stamp(entity.ustamp);
      }
      entity.cTenantSchedTemplateCode = row.cTenantSchedTemplateCode.Trim(); entity.cTenantSchedTemplateName = row.cTenantSchedTemplateName.Trim();
      entity.cTenantSchedTemplateStart = row.cTenantSchedTemplateStart; entity.cTenantSchedTemplateBreak = row.cTenantSchedTemplateBreak;
      entity.cTenantSchedTemplateState = row.cTenantSchedTemplateState; entity.cTenantSchedTemplateOrder = row.cTenantSchedTemplateOrder;
      entity.cTenantSchedTemplateNote = row.cTenantSchedTemplateNote; entity.json_object = row.json_object;
      // The parent row goes first: the model has no navigation that would order the two inserts.
      await TenantOrg.Save(() => ctx.SaveChangesAsync(AbortToken), DuplicateCode);
      var days = (await ctx.ta_TenantSchedTemplateDays.Where(x => x.cTenantSchedTemplateId == entity.cTenantSchedTemplateId).ToArrayAsync(AbortToken))
         .ToDictionary(x => x.cTenantSchedTemplateDayWeekday);
      foreach (var day in set.Days) {
         if (!days.TryGetValue(day.cTenantSchedTemplateDayWeekday, out var target)) {
            target = new ta_TenantSchedTemplateDay {
               cTenantSchedTemplateDayId = Ulid.NewUlid().ToString(), cTenantSchedTemplateId = entity.cTenantSchedTemplateId,
               cTenantSchedTemplateDayWeekday = day.cTenantSchedTemplateDayWeekday, datestamp = now, ustamp = now
            };
            ctx.ta_TenantSchedTemplateDays.Add(target);
         }
         else target.ustamp = now;
         target.cTenantSchedTemplateDayHours = day.cTenantSchedTemplateDayHours;
         target.cTenantSchedTemplateDayBreak = day.cTenantSchedTemplateDayBreak;
      }
      Audit(entity.cTenantSchedTemplateId, isNew ? "tenant.sched.template.created" : "tenant.sched.template.changed",
         (isNew ? "Created" : "Saved") + " schedule template '" + entity.cTenantSchedTemplateCode + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return (await Read(entity.cTenantSchedTemplateId))!;
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<TenantSchedTemplateSet> PostGetMeta_TenantSchedTemplate_Duplicate(string templateId) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var source = await ctx.ta_TenantSchedTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.cTenantSchedTemplateId == templateId, AbortToken)
         ?? throw new ActionException("The template was not found.", 404);
      var days = await ctx.ta_TenantSchedTemplateDays.AsNoTracking().Where(x => x.cTenantSchedTemplateId == templateId).ToArrayAsync(AbortToken);
      var codes = (await ctx.ta_TenantSchedTemplates.AsNoTracking().Where(x => x.cTenantId == source.cTenantId).Select(x => x.cTenantSchedTemplateCode).ToArrayAsync(AbortToken))
         .ToHashSet(StringComparer.OrdinalIgnoreCase);
      var now = DateTime.UtcNow;
      var copy = new ta_TenantSchedTemplate {
         cTenantSchedTemplateId = Ulid.NewUlid().ToString(), cTenantId = source.cTenantId, cTenantSchedTemplateCode = NextCode(source.cTenantSchedTemplateCode, codes),
         cTenantSchedTemplateName = Fit(source.cTenantSchedTemplateName, " copy", 255), cTenantSchedTemplateStart = source.cTenantSchedTemplateStart,
         cTenantSchedTemplateBreak = source.cTenantSchedTemplateBreak, cTenantSchedTemplateState = ta_TenantSchedTemplate.StateActive,
         cTenantSchedTemplateOrder = source.cTenantSchedTemplateOrder, cTenantSchedTemplateNote = source.cTenantSchedTemplateNote,
         json_object = source.json_object, ustamp = now, datestamp = now
      };
      ctx.ta_TenantSchedTemplates.Add(copy);
      await TenantOrg.Save(() => ctx.SaveChangesAsync(AbortToken), DuplicateCode);
      ctx.ta_TenantSchedTemplateDays.AddRange(days.Select(x => new ta_TenantSchedTemplateDay {
         cTenantSchedTemplateDayId = Ulid.NewUlid().ToString(), cTenantSchedTemplateId = copy.cTenantSchedTemplateId, cTenantSchedTemplateDayWeekday = x.cTenantSchedTemplateDayWeekday,
         cTenantSchedTemplateDayHours = x.cTenantSchedTemplateDayHours, cTenantSchedTemplateDayBreak = x.cTenantSchedTemplateDayBreak, ustamp = now, datestamp = now
      }));
      Audit(copy.cTenantSchedTemplateId, "tenant.sched.template.created", "Duplicated schedule template '" + source.cTenantSchedTemplateCode + "' as '" + copy.cTenantSchedTemplateCode + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return (await Read(copy.cTenantSchedTemplateId))!;
   }
   #endregion

   // The first free number that fits the code column: OFF, OFF2, OFF3, ...
   private static string NextCode(string code, HashSet<string> used) {
      for (var number = 2; ; number++) {
         var suffix = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
         var candidate = Fit(code, suffix, ta_TenantSchedTemplate.CodeLength);
         if (!used.Contains(candidate)) return candidate;
      }
   }

   private static string Fit(string text, string suffix, int length) => (text.Length + suffix.Length > length ? text[..(length - suffix.Length)] : text) + suffix;

   private async Task Validate(TenantSchedTemplateSet set) {
      var row = set.Template;
      if (!await ctx.ta_Tenants.AnyAsync(x => x.cTenantId == row.cTenantId, AbortToken)) throw new ActionException("The tenant was not found.", 400);
      if (string.IsNullOrWhiteSpace(row.cTenantSchedTemplateCode)) throw new ActionException("Enter the template code.", 400);
      if (row.cTenantSchedTemplateCode.Trim().Length > ta_TenantSchedTemplate.CodeLength) throw new ActionException($"The template code can have at most {ta_TenantSchedTemplate.CodeLength} characters.", 400);
      if (string.IsNullOrWhiteSpace(row.cTenantSchedTemplateName)) throw new ActionException("Enter the template name.", 400);
      if (row.cTenantSchedTemplateState is not (ta_TenantSchedTemplate.StateActive or ta_TenantSchedTemplate.StateInactive)) throw new ActionException("The template state is not valid.", 400);
      if (row.cTenantSchedTemplateStart < TimeSpan.Zero || row.cTenantSchedTemplateStart >= TimeSpan.FromDays(1)) throw new ActionException("The start time is not valid.", 400);
      if (row.cTenantSchedTemplateBreak < 0) throw new ActionException("The break cannot be negative.", 400);
      if (set.Days.Length != 7 || set.Days.Select(x => x.cTenantSchedTemplateDayWeekday).Distinct().Count() != 7 || set.Days.Any(x => x.cTenantSchedTemplateDayWeekday is < 1 or > 7))
         throw new ActionException("A template needs exactly the weekdays Monday to Sunday.", 400);
      foreach (var day in set.Days) {
         if (day.cTenantSchedTemplateDayHours is < 0 or > 24) throw new ActionException("The hours of a day must be from 0 to 24.", 400);
         if (day.cTenantSchedTemplateDayBreak < 0) throw new ActionException("The break cannot be negative.", 400);
         if (day.cTenantSchedTemplateDayHours * 60 + (day.cTenantSchedTemplateDayBreak ?? row.cTenantSchedTemplateBreak) > 24 * 60)
            throw new ActionException("The hours and the break of a day cannot exceed 24 hours.", 400);
      }
   }

   private async Task<TenantSchedTemplateSet?> Read(string id) {
      var template = await ctx.vi_TenantSchedTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.cTenantSchedTemplateId == id, AbortToken);
      if (template is null) return null;
      var days = await ctx.ta_TenantSchedTemplateDays.AsNoTracking().Where(x => x.cTenantSchedTemplateId == id).OrderBy(x => x.cTenantSchedTemplateDayWeekday).ToArrayAsync(AbortToken);
      return new TenantSchedTemplateSet { Template = template, Days = days };
   }

   private void Audit(string id, string kind, string summary) {
      if (App is not null) GetService<IAuditWriter>()?.Use(ctx).AddEntry(new AuditEntryDraft {
         Group = "System.Settings", Key = id, Kind = kind, Summary = summary, TargetType = "schedule template"
      });
   }
}
