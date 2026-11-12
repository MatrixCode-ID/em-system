using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>A unit of measure, shared by every tenant.</summary>
[Table("ta_Unit")]
public class ta_Unit
{
   /// <summary>Unit code; the key products refer to, so it cannot change after creation.</summary>
   [Key] [MaxLength(50)] public string cUnitId { get; set; } = "";
   /// <summary>Short symbol shown next to a quantity.</summary>
   [MaxLength(50)] public string cUnitSymbol { get; set; } = "";
   /// <summary>Description of the unit.</summary>
   [MaxLength(255)] public string cUnitDesc { get; set; } = "";
   /// <summary>Last update time in UTC, used for optimistic concurrency.</summary>
   public DateTime ustamp { get; set; }
   /// <summary>Creation time in UTC.</summary>
   public DateTime datestamp { get; set; }
}
