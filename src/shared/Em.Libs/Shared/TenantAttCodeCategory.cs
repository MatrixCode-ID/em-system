namespace Em.Shared;

/// <summary>What kind of day an attendance code records. The engine attaches no payroll meaning to it.</summary>
public enum TenantAttCodeCategory
{
   /// <summary>The person was present.</summary>
   Present = 1,
   /// <summary>An absence that is paid.</summary>
   PaidAbsence = 2,
   /// <summary>An absence that is not paid.</summary>
   UnpaidAbsence = 3,
   /// <summary>A day off.</summary>
   DayOff = 4
}
