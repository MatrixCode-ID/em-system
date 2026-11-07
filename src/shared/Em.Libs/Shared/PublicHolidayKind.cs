namespace Em.Shared;

/// <summary>How a public holiday falls on the calendar.</summary>
public enum PublicHolidayKind
{
   /// <summary>The same month and day every year.</summary>
   Fixed = 1,
   /// <summary>One date, entered per year. The engine calculates no lunar calendar.</summary>
   Dated = 2
}
