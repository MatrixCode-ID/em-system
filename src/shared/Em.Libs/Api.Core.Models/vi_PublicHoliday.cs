using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>A public holiday as read by the screens.</summary>
[Table("vi_PublicHoliday")]
public class vi_PublicHoliday : ta_PublicHoliday;
