using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>A unit of measure with display values.</summary>
[Table("vi_Unit")]
public class vi_Unit : ta_Unit
{
   /// <summary>Number of products, of every tenant and state, that use this unit.</summary>
   public int ccUnitProductCount { get; set; }
}
