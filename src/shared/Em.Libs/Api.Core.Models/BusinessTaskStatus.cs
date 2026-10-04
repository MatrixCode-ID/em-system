namespace Em.Api.Core.Models
{
   /// <summary>
   /// Tahap yang sedang dijalani sebuah business task, dari masuk antrian sampai selesai. Ini tahap
   /// proses, bukan penanda aktif/nonaktif, jadi tidak ada nilai negatif di sini.
   /// </summary>
   public enum BusinessTaskStatus
   {
      /// <summary>Menunggu giliran karena batas jumlah task yang boleh berjalan bersamaan sudah penuh.</summary>
      Queued = 0,

      /// <summary>Sedang dikerjakan server.</summary>
      Running = 1,

      /// <summary>Selesai tanpa kesalahan.</summary>
      Succeeded = 2,

      /// <summary>
      /// Berhenti karena kesalahan. Task yang gagal tetap tampil sampai di-clear, supaya penyebabnya
      /// sempat dibaca.
      /// </summary>
      Failed = 3,

      /// <summary>Dihentikan atas permintaan user, atau karena server dimatikan.</summary>
      Canceled = 4
   }
}
