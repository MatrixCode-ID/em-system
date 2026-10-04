using System.Reflection;

namespace Em.Api.Core
{
   public class ActionDefinition
   {
      public Em.Shared.HttpMethod HttpMethod { get; set; }
      public string Module { get; set; } = "";
      public string ActionName { get; set; } = "";
      public MethodInfo MethodInfo { get; set; } = null!;
      public Type Type { get; set; } = null!;

      /// <summary>
      /// Diambil dari <c>[GetAction]</c>/<c>[PostAction]</c>: true kalau action boleh diakses tanpa
      /// autentikasi. Ditegakkan <c>EmApp.ProcessRequest</c> - action yang bukan publik dijawab 401
      /// kalau request-nya tidak membawa identitas yang terbukti.
      /// </summary>
      public bool IsPublicAction { get; set; }

      /// <summary>
      /// Diambil dari <c>[GetAction(claim: ...)]</c>/<c>[PostAction(claim: ...)]</c>. <c>null</c> berarti
      /// action ini butuh claim apa pun milik <see cref="Module"/> - bukan berarti tanpa syarat. Terisi
      /// berarti action ini butuh persis claim itu di module yang sama. Ditegakkan
      /// <c>EmApp.ProcessRequest</c> sesudah <see cref="IsPublicAction"/> diperiksa.
      /// </summary>
      public string? RequiredClaim { get; set; }

      /// <summary>
      /// <c>false</c> untuk service yang didaftarkan engine sendiri sebelum callback module manapun
      /// berjalan, satu-satunya yang berdiri di luar pemeriksaan claim per action - alasannya ada di
      /// overload internal <c>EmAppBuilder.AddService&lt;T1, T2&gt;(bool)</c>. Selalu <c>true</c> untuk
      /// action module, termasuk module yang belum punya satu pun claim terdaftar: yang begitu ditolak,
      /// bukan dilewatkan, supaya module yang lupa diberi claim berisik alih-alih diam-diam terbuka.
      /// <para>
      /// <see cref="RequiredClaim"/> tetap ditegakkan walau ini <c>false</c>. Yang dilepas hanya syarat
      /// default "punya claim apa pun di module ini".
      /// </para>
      /// </summary>
      public bool EnforcesClaims { get; set; }

      /// <summary>
      /// Batas waktu khusus milik action ini, diambil apa adanya dari
      /// <c>[GetAction(requestTimeoutSecond: ...)]</c>. <c>null</c> - dan itu yang berlaku untuk
      /// hampir semua action - berarti action ini ikut <c>EmAppBuilder.HttpRequestTimeout</c>.
      /// </summary>
      /// <remarks>
      /// Sengaja disimpan mentah, bukan sudah digabung dengan batas waktu aplikasi: penggabungannya
      /// terjadi per request di <c>EmApp.ProcessRequest</c>. Kalau digabung di sini, hasilnya jadi
      /// bergantung pada urutan penulisan di <c>Program.cs</c> - batas waktu aplikasi dan pendaftaran
      /// module diisi di dalam callback yang sama - dan urutan yang tertukar tidak akan membuat
      /// siapa pun mengeluh.
      /// <para>
      /// Selalu <c>null</c> untuk action <c>POST</c>: engine tidak pernah memutus action tulis, jadi
      /// <c>[PostAction]</c> memang tidak punya cara menyebutkan batas waktu.
      /// </para>
      /// </remarks>
      public TimeSpan? RequestTimeout { get; set; }

      /// <summary>
      /// Posisi parameter bertipe <see cref="Stream"/> di signature action, atau <c>null</c> kalau action
      /// ini bukan action ber-stream. Terisi berarti body request tidak dibaca sebagai JSON: ia diserahkan
      /// apa adanya ke parameter di posisi ini, dan parameter lain (kalau ada) diambil dari header
      /// <see cref="Defaults.StreamPayloadHeader"/>.
      /// </summary>
      public int? StreamParameterIndex { get; set; }

      /// <summary>
      /// Posisi satu-satunya parameter selain stream pada action ber-stream - objek yang diisi dari header
      /// <see cref="Defaults.StreamPayloadHeader"/>. <c>null</c> kalau action-nya hanya menerima stream,
      /// atau kalau action ini bukan action ber-stream.
      /// </summary>
      public int? StreamPayloadParameterIndex { get; set; }

      /// <summary>
      /// <c>true</c> kalau action ini mengembalikan <c>Task&lt;Stream&gt;</c>. Jawabannya lalu bukan
      /// amplop JSON, melainkan isi stream itu apa adanya sebagai <c>application/octet-stream</c>; hanya
      /// kegagalan yang tetap dijawab dengan amplop JSON.
      /// </summary>
      public bool ReturnsStream { get; set; }
   }
}