namespace Em.Api.Core.Models;

/// <summary>What copying dated holidays to another year did.</summary>
public sealed class PublicHolidayCopyResult
{
   /// <summary>The entries created.</summary>
   public vi_PublicHoliday[] Created { get; set; } = [];
   /// <summary>Entries left out: already there, or 29 February into a non-leap year.</summary>
   public int Skipped { get; set; }
}
