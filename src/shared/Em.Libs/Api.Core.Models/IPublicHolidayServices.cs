using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>The public holiday calendar shared by every tenant; all HTTP calls require the Business Module Manager claim.</summary>
public interface IPublicHolidayServices : IServices
{
   #region Tables
   /// <summary>Creates an entry.</summary>
   Task<vi_PublicHoliday> PostGetTa_PublicHoliday_New(ta_PublicHoliday row);
   /// <summary>Saves an entry, enforcing its update stamp. Setting the removed state removes it; nothing is deleted.</summary>
   Task<vi_PublicHoliday> PostGetTa_PublicHoliday_Update(ta_PublicHoliday row);
   #endregion
   #region Views
   /// <summary>Lists the active fixed entries plus the active dated entries of one year, in calendar order.</summary>
   Task<vi_PublicHoliday[]> GetVi_PublicHolidays(int year);
   #endregion
   #region Meta's
   /// <summary>
   /// Copies active dated entries to another year, keeping month and day. Skips an entry already there and
   /// 29 February into a non-leap year.
   /// </summary>
   Task<PublicHolidayCopyResult> PostGetMeta_PublicHoliday_CopyDated(int fromYear, int toYear);
   /// <summary>Whether a year has active dated entries, for the warning that it has not been entered yet.</summary>
   Task<bool> GetMeta_PublicHoliday_HasDated(int year);
   #endregion
}
