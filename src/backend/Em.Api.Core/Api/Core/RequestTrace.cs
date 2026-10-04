using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Kenapa sebuah action berhenti sebelum jawabannya sampai ke pemanggil.
   /// </summary>
   internal enum AbortReason
   {
      /// <summary>
      /// Pemanggilnya pergi - aplikasinya ditutup, jaringannya putus, atau ia sendiri yang memutus.
      /// Tidak ada jawaban yang dikirim, karena tidak ada lagi yang menerimanya.
      /// </summary>
      CallerGone,

      /// <summary>
      /// Batas waktu kerja action habis sementara pemanggilnya masih menunggu. Jawabannya tetap
      /// dikirim - berupa 504 - karena yang menunggu berhak tahu kenapa ia tidak mendapat apa-apa.
      /// </summary>
      TimedOut
   }

   /// <summary>
   /// Jejak satu request di console: sejak ia masuk, saat action-nya mulai dikerjakan, sampai
   /// selesai, ditolak, atau batal - lengkap dengan alamat pemanggil, identitasnya kalau ada, dan
   /// lama prosesnya. Satu objek untuk satu request, dibuat di <c>EmApp.ProcessRequest</c> dan
   /// mati bersama request itu.
   /// </summary>
   /// <remarks>
   /// Internal, bukan publik: ini jejak milik Engine, bukan alat tulis log untuk penulis module -
   /// yang itu tetap lewat <c>ServicesBase.Logger</c>. Seluruh barisnya ditulis dengan kategori
   /// <see cref="LoggerCategory"/>, sehingga bisa dikeraskan atau dimatikan sendiri lewat
   /// <c>appsettings.json</c> tanpa ikut mematikan log yang lain.
   /// </remarks>
   internal sealed class RequestTrace
   {
      /// <summary>
      /// Kategori logger yang dipakai seluruh baris jejak request. Sengaja sebuah nama tetap, bukan
      /// nama tipe: kategori inilah yang diketik orang di <c>appsettings.json</c>, jadi ia tidak
      /// boleh ikut berubah hanya karena kelas ini dipindah atau diganti nama.
      /// </summary>
      internal const string LoggerCategory = "Em.Api.Request";

      private const string UnknownAddress = "an unknown address";

      private const string AnonymousCaller = "anonymous";

      // Nomor urut supaya baris-baris milik satu request bisa dirangkai saat beberapa request
      // berjalan bersamaan - tanpa itu IN, START, dan DONE di console tidak ketahuan mana pasangan
      // mana. Sengaja nomor pendek, bukan TraceIdentifier bawaan ASP.NET Core, karena yang membaca
      // console adalah mata manusia.
      private static int _counter;

      private readonly ILogger _logger;
      private readonly string _id;
      private readonly string _method;
      private readonly string _routeLabel;
      private readonly string _caller;
      private readonly long _receivedTimestamp;

      private long _startedTimestamp;
      private ActionRequest? _request;

      private RequestTrace(ILogger logger, HttpContext http, string routeLabel) {
         _logger = logger;
         _id = (Interlocked.Increment(ref _counter) & 0xFFFFF).ToString("X5");
         _method = http.Request.Method;
         _routeLabel = routeLabel;
         _caller = DescribeCaller(http);
         _receivedTimestamp = Stopwatch.GetTimestamp();
      }

      /// <summary>
      /// Mencatat request yang baru masuk, lalu mengembalikan jejaknya untuk dipakai sampai request
      /// itu selesai. Dipanggil paling depan, sebelum gerbang identitas berjalan, supaya request yang
      /// nanti ditolak pun tetap kelihatan pernah datang.
      /// </summary>
      internal static RequestTrace Begin(HttpContext http, string routeLabel) {
         var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
         var trace = new RequestTrace(logger, http, routeLabel);

         trace._logger.LogInformation("#{Id} IN     {Method} {Route} from {Caller}",
            trace._id, trace._method, trace._routeLabel, trace._caller);

         return trace;
      }

      /// <summary>
      /// Memberi tahu jejak ini siapa pemanggilnya, sesudah gerbang selesai memeriksanya. Tidak
      /// menulis apa-apa sendiri - identitasnya baru muncul di baris berikutnya, karena yang pantas
      /// ditulis hanya identitas yang sudah terbukti.
      /// </summary>
      internal void Identified(ActionRequest request) => _request = request;

      /// <summary>
      /// Mencatat bahwa action-nya mulai dikerjakan, sekaligus menyalakan pengukur waktu proses.
      /// Dipanggil sesudah argumennya terikat, jadi yang diukur benar-benar lama kerja action-nya.
      /// </summary>
      internal void Processing() {
         _startedTimestamp = Stopwatch.GetTimestamp();

         _logger.LogInformation("#{Id} START  {Method} {Route} from {Caller} as {User}",
            _id, _method, _routeLabel, _caller, DescribeUser());
      }

      /// <summary>
      /// Mencatat action yang sudah selesai dikerjakan beserta lama prosesnya. Hasil yang gagal tetap
      /// lewat sini - action-nya memang sempat berjalan - dan dinaikkan tingkatnya sesuai status.
      /// </summary>
      internal void Completed(ActionResult result) {
         _logger.Log(LevelFor(result.StatusCode, result.ValidResult),
            "#{Id} DONE   {Method} {Route} from {Caller} as {User} - {StatusCode} in {Elapsed:0.0} ms (total {Total:0.0} ms)",
            _id, _method, _routeLabel, _caller, DescribeUser(), result.StatusCode,
            Stopwatch.GetElapsedTime(_startedTimestamp).TotalMilliseconds,
            Stopwatch.GetElapsedTime(_receivedTimestamp).TotalMilliseconds);
      }

      /// <summary>
      /// Mencatat action yang berhenti sebelum jawabannya sampai ke pemanggil. Sengaja sebuah baris
      /// tersendiri, bukan <see cref="Completed"/> dengan status lain: <c>DONE 200</c> untuk jawaban
      /// yang tidak pernah sampai justru menyesatkan pada saat log-nya paling dibutuhkan.
      /// </summary>
      /// <remarks>
      /// Tingkatnya <see cref="LogLevel.Warning"/>, bukan <see cref="LogLevel.Error"/>. Pemanggil
      /// yang menutup aplikasinya bukan gangguan server, dan kalau setiap kejadiannya menaikkan
      /// bendera merah, yang terjadi bukan orang jadi waspada melainkan log berhenti dibaca.
      /// </remarks>
      internal void Aborted(AbortReason reason) {
         var described = reason == AbortReason.CallerGone
            ? "the caller went away"
            : "it ran out of time";

         _logger.LogWarning(
            "#{Id} ABORT  {Method} {Route} from {Caller} as {User} - {Reason} after {Elapsed:0.0} ms (total {Total:0.0} ms)",
            _id, _method, _routeLabel, _caller, DescribeUser(), described,
            Stopwatch.GetElapsedTime(_startedTimestamp).TotalMilliseconds,
            Stopwatch.GetElapsedTime(_receivedTimestamp).TotalMilliseconds);
      }

      /// <summary>
      /// Mencatat request yang dijawab tanpa action-nya sempat berjalan - identitasnya ditolak,
      /// action-nya tidak ada, verb-nya salah, atau parameternya tidak bisa diikat.
      /// </summary>
      internal void Rejected(ActionResult result) {
         _logger.Log(LevelFor(result.StatusCode, result.ValidResult),
            "#{Id} REJECT {Method} {Route} from {Caller} as {User} - {StatusCode} {Reason} after {Elapsed:0.0} ms",
            _id, _method, _routeLabel, _caller, DescribeUser(), result.StatusCode,
            result.ErrorMessage ?? "no reason given",
            Stopwatch.GetElapsedTime(_receivedTimestamp).TotalMilliseconds);
      }

      // 500 ke atas adalah kesalahan server, jadi ia pantas Error; 4xx adalah kesalahan pemanggil,
      // cukup Warning supaya console produksi tidak penuh oleh hal yang memang bukan gangguan.
      private static LogLevel LevelFor(int statusCode, bool validResult) =>
         validResult ? LogLevel.Information :
         statusCode >= 500 ? LogLevel.Error : LogLevel.Warning;

      /// <summary>
      /// Menyusun sebutan pemanggil untuk log: nama akun kalau namanya terbukti dari data, kalau
      /// tidak id-nya saja, dan <c>anonymous</c> selama belum ada identitas apa pun. Nama yang
      /// menempel di header tidak pernah dipakai di sini - ia datang dari pemanggil, bukan dari data,
      /// dan log yang mencatat pengakuan sebagai kenyataan lebih buruk daripada log yang diam.
      /// </summary>
      private string DescribeUser() {
         if (_request is not { IsAuthenticated: true } request) {
            return AnonymousCaller;
         }

         var who = request.cUserAccount is { } account
            ? $"{account} ({request.cUserId})"
            : request.cUserId!;

         if (!request.IsDebugRequest) {
            return who;
         }

         return request.IsImpersonating
            ? $"{who} [debug:{request.DebugKeyName}, impersonating]"
            : $"{who} [debug:{request.DebugKeyName}]";
      }

      /// <summary>
      /// Menyusun sebutan alamat pemanggil. Yang di depan selalu alamat client sebenarnya: kalau
      /// request datang lewat proxy tepercaya, <c>UseForwardedHeaders</c> sudah menggantinya dari
      /// <c>X-Forwarded-For</c> sebelum baris ini berjalan, dan alamat proxy yang tergantikan itu
      /// ikut ditulis di belakang <c>via</c> supaya jalur yang dilewatinya tetap kelihatan.
      /// </summary>
      internal static string DescribeCaller(HttpContext http) {
         var client = http.Connection.RemoteIpAddress?.ToString() ?? UnknownAddress;
         var original = http.Request.Headers[ForwardedHeadersDefaults.XOriginalForHeaderName];

         if (original.Count == 0) {
            return client;
         }

         var hops = original
            .SelectMany(r => (r ?? string.Empty)
               .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(StripPort)
            .Where(r => r.Length > 0)
            .ToArray();

         return hops.Length == 0 ? client : $"{client} via {string.Join(" via ", hops)}";
      }

      // Alamat yang ditinggalkan middleware membawa nomor port juga ("10.0.0.2:51324"), dan port
      // sebuah proxy tidak menerangkan apa-apa - yang dicari pembaca log adalah mesinnya.
      private static string StripPort(string value) =>
         IPEndPoint.TryParse(value, out var endpoint) ? endpoint.Address.ToString() : value;
   }
}
