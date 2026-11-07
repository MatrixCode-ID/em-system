namespace Em.Shared;

/// <summary>
/// The office schedule an enabled tenant uses when no level sets a default: 08:00 start, 60 minutes break and 8 hours
/// Monday to Friday, weekend off. It lives in code, so it needs no database row.
/// </summary>
public static class TenantSchedBuiltIn
{
   /// <summary>Name shown for this schedule.</summary>
   public const string Name = "Built-in";
   /// <summary>Break in minutes.</summary>
   public const int BreakMinutes = 60;
   /// <summary>Working hours of a working day.</summary>
   public const decimal WorkdayHours = 8m;
   /// <summary>Length of an assignment period when the tenant sets none.</summary>
   public const int DefaultPeriodDays = 14;
   /// <summary>Start of the working day.</summary>
   public static TimeSpan Start { get; } = TimeSpan.FromHours(8);

   /// <summary>Working hours of a weekday.</summary>
   /// <param name="weekday">1 Monday to 7 Sunday.</param>
   public static decimal HoursOf(int weekday) => weekday is >= 1 and <= 5 ? WorkdayHours : 0m;
}
