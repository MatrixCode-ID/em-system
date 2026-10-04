namespace Em.Api.Core.Models
{
   /// <summary>
   /// Batas jumlah business task yang boleh berjalan bersamaan. Task yang melebihinya tidak ditolak,
   /// melainkan menunggu dengan status <see cref="BusinessTaskStatus.Queued"/> sampai ada yang selesai.
   /// Disimpan di metadata server dan diubah lewat layar Business Task Manager.
   /// </summary>
   public class BusinessTaskLimit
   {
      /// <summary>Apakah <see cref="Limit"/> berlaku untuk seluruh server atau untuk tiap user.</summary>
      public BusinessTaskLimitMode Mode { get; set; }

      /// <summary>Jumlah task yang boleh berjalan bersamaan; minimal <c>1</c>.</summary>
      public int Limit { get; set; } = 1;
   }
}
