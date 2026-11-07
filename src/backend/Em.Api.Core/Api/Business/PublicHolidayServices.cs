using System.Data;
using Em.Api.Core.Log.Audit;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core;

/// <summary>The one public holiday calendar of an installation, shared by every tenant.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class PublicHolidayServices(ApiCoreContext ctx) : ServicesBase, IPublicHolidayServices
{
   private const int FirstYear = 1900;
   private const int LastYear = 2199;
   // A leap year accepts 29 February, which a fixed holiday may use.
   private const int AnyLeapYear = 2024;

   #region Tables
   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_PublicHoliday> PostGetTa_PublicHoliday_New(ta_PublicHoliday row) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      Normalize(row);
      await RequireFree(row);
      var now = DateTime.UtcNow;
      var entity = new ta_PublicHoliday {
         cPublicHolidayId = string.IsNullOrWhiteSpace(row.cPublicHolidayId) ? Ulid.NewUlid().ToString() : row.cPublicHolidayId,
         cPublicHolidayName = row.cPublicHolidayName, cPublicHolidayKind = row.cPublicHolidayKind, cPublicHolidayMonth = row.cPublicHolidayMonth,
         cPublicHolidayDay = row.cPublicHolidayDay, cPublicHolidayDate = row.cPublicHolidayDate, cPublicHolidayCollective = row.cPublicHolidayCollective,
         cPublicHolidayState = row.cPublicHolidayState, cPublicHolidayNote = row.cPublicHolidayNote, json_object = row.json_object, ustamp = now, datestamp = now
      };
      ctx.ta_PublicHolidays.Add(entity);
      await ctx.SaveChangesAsync(AbortToken);
      Audit(entity.cPublicHolidayId, "public.holiday.created", "Created public holiday '" + entity.cPublicHolidayName + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await Read(entity.cPublicHolidayId);
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_PublicHoliday> PostGetTa_PublicHoliday_Update(ta_PublicHoliday row) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var entity = await ctx.ta_PublicHolidays.SingleOrDefaultAsync(x => x.cPublicHolidayId == row.cPublicHolidayId, AbortToken)
         ?? throw new ActionException("The holiday was not found.", 404);
      if (entity.ustamp != row.ustamp) throw new ActionException("The holiday changed. Refresh before saving.", 409);
      Normalize(row);
      await RequireFree(row);
      entity.cPublicHolidayName = row.cPublicHolidayName; entity.cPublicHolidayKind = row.cPublicHolidayKind; entity.cPublicHolidayMonth = row.cPublicHolidayMonth;
      entity.cPublicHolidayDay = row.cPublicHolidayDay; entity.cPublicHolidayDate = row.cPublicHolidayDate; entity.cPublicHolidayCollective = row.cPublicHolidayCollective;
      entity.cPublicHolidayState = row.cPublicHolidayState; entity.cPublicHolidayNote = row.cPublicHolidayNote; entity.json_object = row.json_object;
      entity.ustamp = TenantOrg.Stamp(entity.ustamp);
      Audit(entity.cPublicHolidayId, "public.holiday.changed", (row.cPublicHolidayState == ta_PublicHoliday.StateActive ? "Saved" : "Removed") + " public holiday '" + entity.cPublicHolidayName + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await Read(entity.cPublicHolidayId);
   }
   #endregion

   #region Views
   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_PublicHoliday[]> GetVi_PublicHolidays(int year) {
      RequireYear(year);
      var (start, end) = YearRange(year);
      var rows = await ctx.vi_PublicHolidays.AsNoTracking().Where(x => x.cPublicHolidayState == ta_PublicHoliday.StateActive &&
         (x.cPublicHolidayKind == PublicHolidayKind.Fixed || (x.cPublicHolidayDate >= start && x.cPublicHolidayDate < end))).ToArrayAsync(AbortToken);
      return [.. rows.OrderBy(x => x.cPublicHolidayDate?.Month ?? x.cPublicHolidayMonth).ThenBy(x => x.cPublicHolidayDate?.Day ?? x.cPublicHolidayDay)
         .ThenBy(x => x.cPublicHolidayName, StringComparer.OrdinalIgnoreCase)];
   }
   #endregion

   #region Meta's
   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<PublicHolidayCopyResult> PostGetMeta_PublicHoliday_CopyDated(int fromYear, int toYear) {
      RequireYear(fromYear); RequireYear(toYear);
      if (fromYear == toYear) throw new ActionException("Choose another year to copy from.", 400);
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var sources = await DatedOf(fromYear).ToArrayAsync(AbortToken);
      var present = (await DatedOf(toYear).ToArrayAsync(AbortToken)).Select(Key).ToHashSet();
      var now = DateTime.UtcNow;
      var created = new List<ta_PublicHoliday>();
      foreach (var source in sources.OrderBy(x => x.cPublicHolidayDate)) {
         var date = source.cPublicHolidayDate!.Value;
         if (date is { Month: 2, Day: 29 } && !DateTime.IsLeapYear(toYear)) continue;
         var copy = new ta_PublicHoliday {
            cPublicHolidayId = Ulid.NewUlid().ToString(), cPublicHolidayName = source.cPublicHolidayName, cPublicHolidayKind = PublicHolidayKind.Dated,
            cPublicHolidayDate = new DateTime(toYear, date.Month, date.Day), cPublicHolidayCollective = source.cPublicHolidayCollective,
            cPublicHolidayState = ta_PublicHoliday.StateActive, cPublicHolidayNote = source.cPublicHolidayNote, ustamp = now, datestamp = now
         };
         if (present.Add(Key(copy))) created.Add(copy);
      }
      ctx.ta_PublicHolidays.AddRange(created);
      await ctx.SaveChangesAsync(AbortToken);
      foreach (var row in created) Audit(row.cPublicHolidayId, "public.holiday.created", "Copied public holiday '" + row.cPublicHolidayName + "' to " + toYear);
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      var ids = created.Select(x => x.cPublicHolidayId).ToArray();
      return new PublicHolidayCopyResult {
         Created = await ctx.vi_PublicHolidays.AsNoTracking().Where(x => ids.Contains(x.cPublicHolidayId)).OrderBy(x => x.cPublicHolidayDate).ToArrayAsync(AbortToken),
         Skipped = sources.Length - created.Count
      };
   }

   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public Task<bool> GetMeta_PublicHoliday_HasDated(int year) {
      RequireYear(year);
      return DatedOf(year).AnyAsync(AbortToken);
   }
   #endregion

   private IQueryable<ta_PublicHoliday> DatedOf(int year) {
      var (start, end) = YearRange(year);
      return ctx.ta_PublicHolidays.AsNoTracking().Where(x => x.cPublicHolidayState == ta_PublicHoliday.StateActive && x.cPublicHolidayKind == PublicHolidayKind.Dated &&
         x.cPublicHolidayDate >= start && x.cPublicHolidayDate < end);
   }

   private static (DateTime Start, DateTime End) YearRange(int year) => (new DateTime(year, 1, 1), new DateTime(year + 1, 1, 1));

   private static (string Name, DateTime Date) Key(ta_PublicHoliday row) => (row.cPublicHolidayName.Trim().ToUpperInvariant(), row.cPublicHolidayDate!.Value.Date);

   private static void RequireYear(int year) {
      if (year is < FirstYear or > LastYear) throw new ActionException($"Enter a year from {FirstYear} to {LastYear}.", 400);
   }

   // Validates the entry and clears the fields of the other kind.
   private static void Normalize(ta_PublicHoliday row) {
      if (string.IsNullOrWhiteSpace(row.cPublicHolidayName)) throw new ActionException("Enter the holiday name.", 400);
      row.cPublicHolidayName = row.cPublicHolidayName.Trim();
      if (row.cPublicHolidayState is not (ta_PublicHoliday.StateActive or ta_PublicHoliday.StateRemoved)) throw new ActionException("The holiday state is not valid.", 400);
      switch (row.cPublicHolidayKind) {
         case PublicHolidayKind.Fixed:
            if (row.cPublicHolidayMonth is not (>= 1 and <= 12) || row.cPublicHolidayDay is not >= 1 || row.cPublicHolidayDay > DateTime.DaysInMonth(AnyLeapYear, row.cPublicHolidayMonth.Value))
               throw new ActionException("Enter a valid month and day.", 400);
            row.cPublicHolidayDate = null;
            break;
         case PublicHolidayKind.Dated:
            if (row.cPublicHolidayDate is not { } date) throw new ActionException("Enter the holiday date.", 400);
            RequireYear(date.Year);
            row.cPublicHolidayDate = date.Date; row.cPublicHolidayMonth = null; row.cPublicHolidayDay = null;
            break;
         default:
            throw new ActionException("The holiday kind is not valid.", 400);
      }
   }

   private async Task RequireFree(ta_PublicHoliday row) {
      if (row.cPublicHolidayState != ta_PublicHoliday.StateActive) return;
      var same = ctx.ta_PublicHolidays.AsNoTracking().Where(x => x.cPublicHolidayId != row.cPublicHolidayId && x.cPublicHolidayState == ta_PublicHoliday.StateActive &&
         x.cPublicHolidayName == row.cPublicHolidayName && x.cPublicHolidayKind == row.cPublicHolidayKind);
      var taken = row.cPublicHolidayKind == PublicHolidayKind.Fixed
         ? await same.AnyAsync(x => x.cPublicHolidayMonth == row.cPublicHolidayMonth && x.cPublicHolidayDay == row.cPublicHolidayDay, AbortToken)
         : await same.AnyAsync(x => x.cPublicHolidayDate == row.cPublicHolidayDate, AbortToken);
      if (taken) throw new ActionException("This holiday is already on the calendar.", 409);
   }

   private Task<vi_PublicHoliday> Read(string id) => ctx.vi_PublicHolidays.AsNoTracking().SingleAsync(x => x.cPublicHolidayId == id, AbortToken);

   private void Audit(string id, string kind, string summary) {
      if (App is not null) GetService<IAuditWriter>()?.Use(ctx).AddEntry(new AuditEntryDraft {
         Group = "System.Settings", Key = id, Kind = kind, Summary = summary, TargetType = "public holiday"
      });
   }
}
