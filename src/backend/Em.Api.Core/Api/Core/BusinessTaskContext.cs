using Microsoft.Extensions.Logging;

namespace Em.Api.Core
{
   /// <summary>
   /// Semua yang boleh dipakai pekerjaan sebuah business task selama berjalan. Pekerjaan itu berjalan di
   /// luar request yang memulainya, jadi apa pun yang milik request - <c>Request</c>, <c>AbortToken</c>,
   /// <c>GetService</c>, dan service yang memulainya sendiri - tidak boleh dipakai di dalamnya. Gantinya
   /// ada di sini.
   /// </summary>
   public sealed class BusinessTaskContext
   {
      private readonly Action<double?, string> _report;

      internal BusinessTaskContext(CancellationToken cancellationToken, IServiceProvider services, ActionRequest starter,
         ILogger logger, Action<double?, string> report) {
         CancellationToken = cancellationToken;
         Services = services;
         Starter = starter;
         Logger = logger;
         _report = report;
      }

      /// <summary>
      /// Menyala saat task ini dibatalkan user atau saat server dimatikan - tidak pernah karena request
      /// pemulainya selesai. Teruskan ke setiap pemanggilan yang bisa lama; berhenti karena token ini
      /// dicatat sebagai dibatalkan, bukan gagal.
      /// </summary>
      public CancellationToken CancellationToken { get; }

      /// <summary>
      /// Service provider milik task ini sendiri, hidup selama task berjalan. Service scoped (DbContext dan
      /// sejenisnya) diambil dari sini. <see cref="ActionRequest"/> yang diminta lewat provider ini adalah
      /// <see cref="Starter"/>.
      /// </summary>
      public IServiceProvider Services { get; }

      /// <summary>
      /// Identitas user yang memulai task ini, seperti saat ia memulainya. Pakai ini untuk pemeriksaan hak
      /// dan untuk jejak tulis, bukan identitas request lain.
      /// </summary>
      public ActionRequest Starter { get; }

      /// <summary>Logger untuk task ini.</summary>
      public ILogger Logger { get; }

      /// <summary>
      /// Melaporkan kemajuan kepada yang memantau task ini. Murah untuk dipanggil sesering apa pun; yang
      /// dibaca client hanya nilai terakhir.
      /// </summary>
      /// <param name="percent">Kemajuan 0–100, atau <c>null</c> kalau tidak bisa diukur.</param>
      /// <param name="caption">Keterangan singkat langkah yang sedang dikerjakan.</param>
      public void Report(double? percent, string caption) => _report(percent, caption);
   }
}
