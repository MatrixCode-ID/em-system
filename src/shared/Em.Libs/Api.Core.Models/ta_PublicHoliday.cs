using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>An entry of the one calendar shared by every tenant.</summary>
[Table("ta_PublicHoliday")]
public class ta_PublicHoliday
{
   /// <summary>Active entry.</summary>
   public const int StateActive = 0;
   /// <summary>Removed entry; nothing is physically deleted.</summary>
   public const int StateRemoved = -1;
   /// <summary>Holiday ULID; leave empty to let the server create one.</summary>
   [Key] [MaxLength(26)] public string cPublicHolidayId { get; set; } = "";
   /// <summary>Holiday name.</summary>
   [MaxLength(255)] public string cPublicHolidayName { get; set; } = "";
   /// <summary>Fixed every year, or dated for one year.</summary>
   public PublicHolidayKind cPublicHolidayKind { get; set; } = PublicHolidayKind.Dated;
   /// <summary>Month of a fixed holiday.</summary>
   public int? cPublicHolidayMonth { get; set; }
   /// <summary>Day of a fixed holiday.</summary>
   public int? cPublicHolidayDay { get; set; }
   /// <summary>Date of a dated holiday.</summary>
   [Column(TypeName = "date")] public DateTime? cPublicHolidayDate { get; set; }
   /// <summary>Marks collective leave. It is only a marker, with no behaviour.</summary>
   public bool cPublicHolidayCollective { get; set; }
   /// <summary>Active or removed state.</summary>
   public int cPublicHolidayState { get; set; }
   /// <summary>Optional administrator note.</summary>
   [MaxLength(500)] public string? cPublicHolidayNote { get; set; }
   /// <summary>Last update time in UTC, used for optimistic concurrency.</summary>
   public DateTime ustamp { get; set; }
   /// <summary>Creation time in UTC.</summary>
   public DateTime datestamp { get; set; }
   /// <summary>Optional extension data.</summary>
   public string? json_object { get; set; }
}
