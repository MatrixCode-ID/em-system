using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
namespace Em.Ui.Core;
/// <summary>Public holiday proxy following the active connection.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class PublicHolidayClientService(IEmApp app, Func<ApiClient?> activeClient) : ServiceUiBase(app), IPublicHolidayServices
{
   private void Prepare() => ApiClient = activeClient();
   /// <inheritdoc />
   public Task<vi_PublicHoliday> PostGetTa_PublicHoliday_New(ta_PublicHoliday row) { Prepare(); return PostAsync<vi_PublicHoliday>(nameof(PostGetTa_PublicHoliday_New), row); }
   /// <inheritdoc />
   public Task<vi_PublicHoliday> PostGetTa_PublicHoliday_Update(ta_PublicHoliday row) { Prepare(); return PostAsync<vi_PublicHoliday>(nameof(PostGetTa_PublicHoliday_Update), row); }
   /// <inheritdoc />
   public Task<vi_PublicHoliday[]> GetVi_PublicHolidays(int year) { Prepare(); return GetAsync<vi_PublicHoliday[]>(nameof(GetVi_PublicHolidays), year); }
   /// <inheritdoc />
   public Task<PublicHolidayCopyResult> PostGetMeta_PublicHoliday_CopyDated(int fromYear, int toYear) { Prepare(); return PostAsync<PublicHolidayCopyResult>(nameof(PostGetMeta_PublicHoliday_CopyDated), fromYear, toYear); }
   /// <inheritdoc />
   public Task<bool> GetMeta_PublicHoliday_HasDated(int year) { Prepare(); return GetAsync<bool>(nameof(GetMeta_PublicHoliday_HasDated), year); }
}
