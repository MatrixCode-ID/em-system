using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Em.Api.Core.Models;
using Em.Api.Shared;
using Em.Shared;

namespace Em.Api.Core
{
   public abstract class ServicesBase
   {
      public EmApp App { get; internal set; } = null!;

      public HttpContext HttpContext { get; internal set; } = null!;

      /// <summary>
      /// Logger yang kategorinya otomatis mengikuti tipe service yang sedang berjalan
      /// (mis. <c>IEmApiCoreServices</c>), diisi otomatis oleh Engine sebelum action dipanggil.
      /// </summary>
      public ILogger Logger { get; internal set; } = null!;

      /// <summary>
      /// Seluruh keterangan tentang request yang sedang dikerjakan - siapa pemanggilnya, lewat jalan
      /// mana ia membuktikannya, dan ke action mana ia ditujukan - berikut pemeriksa haknya
      /// (<c>Request.RequireAdmin()</c> dan kawan-kawan). Diisi gerbang sebelum action dipanggil,
      /// jadi di dalam sebuah action ia tidak pernah null.
      /// </summary>
      /// <remarks>
      /// Kalau isinya <see cref="ActionRequest.None"/>, kodenya sedang dipanggil dari luar jalur
      /// request - bukan berarti gerbangnya bolong.
      /// </remarks>
      public ActionRequest Request { get; internal set; } = ActionRequest.None;

      /// <summary>
      /// Menyala kalau pekerjaan action ini sudah tidak ada gunanya diteruskan - entah karena
      /// pemanggilnya pergi (aplikasinya ditutup, jaringannya putus, atau nanti: tombol batal
      /// ditekan), entah karena batas waktu yang ditetapkan
      /// <c>EmAppBuilder.HttpRequestTimeout</c> sudah lewat.
      /// </summary>
      /// <remarks>
      /// Mengamatinya itu <b>pilihan</b>, bukan keharusan: action yang mengabaikannya tetap berjalan
      /// sampai selesai seperti sebelumnya. Yang paling pantas mengamatinya adalah pekerjaan baca
      /// yang panjang - perulangan besar, laporan, ekspor - dengan meneruskannya ke pemanggilan EF
      /// (<c>ToListAsync(AbortToken)</c> dan sejenisnya).
      /// <para>
      /// <b>Action tulis sebaiknya tidak mengamatinya</b> kecuali penulisnya memang tahu di titik
      /// mana berhenti itu aman. Berhenti di tengah rangkaian tulis tidak menghasilkan keadaan
      /// "tidak jadi", melainkan keadaan yang tidak diketahui siapa pun - termasuk oleh pemanggil
      /// yang sudah telanjur pergi. Karena itu engine sendiri tidak pernah membatasi waktu action
      /// <c>POST</c>; di action <c>POST</c> token ini hanya menyala saat pemanggilnya pergi, dan
      /// keputusan menanggapinya sepenuhnya ada pada penulis action.
      /// </para>
      /// <para>
      /// Yang mengisinya hanya gerbang, dan hanya pada service pemilik action yang sedang berjalan.
      /// Kelas lain yang juga turun dari <see cref="ServicesBase"/> tapi diambil lewat DI tetap
      /// memegang <see cref="CancellationToken.None"/> - sama seperti <see cref="Request"/> yang
      /// tetap <see cref="ActionRequest.None"/> di luar jalur request. Kelas pembantu yang
      /// benar-benar perlu mengamatinya bisa meminta <c>IHttpContextAccessor</c> dan membaca
      /// <c>HttpContext.RequestAborted</c> sendiri.
      /// </para>
      /// </remarks>
      public CancellationToken AbortToken { get; internal set; } = CancellationToken.None;

      /// <summary>
      /// Pengguna yang memanggil action ini, sesuai token yang dibawanya. Tetap <c>null</c> untuk
      /// action publik yang memang dipanggil tanpa token. Penerus untuk
      /// <see cref="ActionRequest.cUserId"/> pada <see cref="Request"/>.
      /// </summary>
      public string? CallerUserId => Request.cUserId;

      /// <summary>
      /// Sesi yang menerbitkan token pemanggil - inilah yang diakhiri saat pengguna keluar dari
      /// perangkat ini saja. Penerus untuk <see cref="ActionRequest.cUserSessionId"/> pada
      /// <see cref="Request"/>.
      /// </summary>
      public string? CallerSessionId => Request.cUserSessionId;

      /// <summary>
      /// Apakah pemanggil action ini berhak penuh sebagai administrator. Selalu <c>false</c> selama
      /// <see cref="CallerUserId"/> masih kosong - tidak ada identitas, tidak ada hak. Penerus untuk
      /// <see cref="ActionRequest.IsAdmin"/> pada <see cref="Request"/>.
      /// </summary>
      public bool CallerIsAdmin => Request.IsAdmin;

      /// <summary>
      /// Mengambil service dari DI container milik request yang sedang berjalan. Hanya jalur baca:
      /// pendaftaran service seluruhnya terjadi saat startup lewat <c>EmAppBuilder</c>.
      /// </summary>
      public object? GetService(Type serviceType) {
         return App.ServiceProvider.GetService(serviceType);
      }

      /// <inheritdoc cref="GetService(Type)" />
      public T? GetService<T>() {
         return App.ServiceProvider.GetService<T>();
      }

      /// <summary>
      /// Mengambil service dari DI container yang terdaftar dengan key <paramref name="connectionName"/> -
      /// dipakai untuk <see cref="System.Data.IDbConnection"/> milik koneksi selain "Default", yang
      /// terdaftar per nama lewat <c>EmAppBuilder.AddExtraDbConn</c>.
      /// </summary>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau <paramref name="connectionName"/> bukan nama koneksi database yang terdaftar,
      /// menyebut nama-nama yang memang terdaftar - beda dari <see cref="GetService(Type)"/> biasa yang
      /// memang boleh mengembalikan <c>null</c> untuk service yang tidak terdaftar, karena nama koneksi
      /// yang salah ketik di sini tidak boleh menyamar jadi <see cref="NullReferenceException"/> beberapa
      /// baris kemudian.
      /// </exception>
      public object? GetService(Type serviceType, string connectionName) {
         var value = App.ServiceProvider.GetKeyedService(serviceType, connectionName);
         if (value is not null) {
            return value;
         }

         if (GetService<IEmDbConnectionFactory>() is { } connectionFactory &&
             !connectionFactory.ConnectionNames.Contains(connectionName, StringComparer.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
               $"No database connection named '{connectionName}' is registered. Registered connections: {EmDbConnectionFactory.DescribeKnownConnections(connectionFactory.ConnectionNames)}.");
         }

         return value;
      }

      /// <inheritdoc cref="GetService(Type,string)" />
      public T? GetService<T>(string connectionName) {
         return (T?)GetService(typeof(T), connectionName);
      }

      /// <summary>
      /// Membaca nilai metadata berdasarkan key, data core lintas-module yang tersedia
      /// untuk semua service tanpa perlu inject ApiCoreContext/ApiCoreServices sendiri.
      /// </summary>
      public async Task<string?> GetMetaValue(string key) {
         var ctx = GetService<ApiCoreContext>()!;
         return (await ctx.ta_Metas.SingleOrDefaultAsync(r => r.cMetaKey == key))?.cMetaValue;
      }

      /// <summary>
      /// Sama seperti <see cref="GetMetaValue(string)"/>, tapi hasilnya dikonversi ke tipe <typeparamref name="T"/>.
      /// </summary>
      public async Task<T?> GetMetaValue<T>(string key) {
         var rawValue = await GetMetaValue(key);
         if (rawValue is null) {
            return default;
         }

         var nullableType = Nullable.GetUnderlyingType(typeof(T));
         var actualType = nullableType ?? typeof(T);

         if (actualType == typeof(string)) {
            return (T)(object)rawValue;
         }

         if (actualType.IsEnum) {
            return (T)Enum.Parse(actualType, rawValue, ignoreCase: true);
         }

         if (actualType == typeof(Guid)) {
            return (T)(object)Guid.Parse(rawValue);
         }

         var converter = TypeDescriptor.GetConverter(actualType);
         if (converter.CanConvertFrom(typeof(string))) {
            return (T?)converter.ConvertFromInvariantString(rawValue);
         }

         return (T)Convert.ChangeType(rawValue, actualType, CultureInfo.InvariantCulture);
      }

      /// <summary>
      /// Menyimpan (insert/update) nilai metadata untuk sebuah key.
      /// </summary>
      protected async Task SetMetaValue(string key, string value, string description = "") {
         var ctx = GetService<ApiCoreContext>()!;
         // Tracked on purpose: an existing row is updated by editing it in place below, and
         // reads are no-tracking by default.
         var meta = await ctx.ta_Metas.AsTracking().SingleOrDefaultAsync(r => r.cMetaKey == key);

         if (meta is null) {
            ctx.ta_Metas.Add(new ta_Meta {
               cMetaKey = key,
               cMetaValue = value,
               cMetaDescription = description,
               ustamp = DateTime.UtcNow,
            });
         }
         else {
            meta.cMetaValue = value;
            meta.ustamp = DateTime.UtcNow;
         }

         await ctx.SaveChangesAsync();
      }
      
      /// <summary>
      /// Mengambil pasangan public/private key RSA server dari metadata. Jika belum ada, atau
      /// <paramref name="keySize"/> yang diminta berbeda dari yang tersimpan, sebuah pasangan
      /// key baru otomatis di-generate dengan ukuran tersebut dan disimpan (menggantikan yang lama).
      /// </summary>
      public async Task<RsaKeyPair> GetServerRsaKeyAsync(int keySize = 2048) {
         const string ServerRsaPublicKeyMetaKey = "ServerRsaPublicKey";
         const string ServerRsaPrivateKeyMetaKey = "ServerRsaPrivateKey";
         const string ServerRsaKeySizeMetaKey = "ServerRsaKeySize";

         var ctx = GetService<ApiCoreContext>()!;
         var metaKeys = new[] { ServerRsaPublicKeyMetaKey, ServerRsaPrivateKeyMetaKey, ServerRsaKeySizeMetaKey };

         async Task<(string? PublicKey, string? PrivateKey, int? KeySize)> ReadRsaMeta() {
            var existing = await ctx.ta_Metas
               .Where(r => metaKeys.Contains(r.cMetaKey))
               .ToDictionaryAsync(r => r.cMetaKey, r => r.cMetaValue);

            existing.TryGetValue(ServerRsaPublicKeyMetaKey, out var publicKey);
            existing.TryGetValue(ServerRsaPrivateKeyMetaKey, out var privateKey);
            int? readKeySize = existing.TryGetValue(ServerRsaKeySizeMetaKey, out var keySizeRaw) &&
                               int.TryParse(keySizeRaw, out var parsed)
               ? parsed
               : null;

            return (
               string.IsNullOrWhiteSpace(publicKey) ? null : publicKey,
               string.IsNullOrWhiteSpace(privateKey) ? null : privateKey,
               readKeySize
            );
         }

         var existingMeta = await ReadRsaMeta();
         if (existingMeta is { PublicKey: not null, PrivateKey: not null } && existingMeta.KeySize == keySize) {
            return new RsaKeyPair(existingMeta.PublicKey, existingMeta.PrivateKey);
         }

         using var rsa = RSA.Create(keySize);
         var publicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());
         var privateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey());

         try {
            await SetMetaValue(ServerRsaPublicKeyMetaKey, publicKey, "Auto-generated server RSA public key.");
            await SetMetaValue(ServerRsaPrivateKeyMetaKey, privateKey, "Auto-generated server RSA private key.");
            await SetMetaValue(ServerRsaKeySizeMetaKey, $"{keySize}", "Server RSA key size.");
         }
         catch (DbUpdateException) {
            var winner = await ReadRsaMeta();
            if (winner is { PublicKey: not null, PrivateKey: not null }) {
               return new RsaKeyPair(winner.PublicKey, winner.PrivateKey);
            }

            throw;
         }

         return new RsaKeyPair(publicKey, privateKey);
      }

      #region Business Task

      private BusinessTaskRunner BusinessTasks => App.ServiceProvider.GetRequiredService<BusinessTaskRunner>();

      /// <summary>
      /// Memulai business task tanpa hasil yang perlu diambil (mis. membuat archive, mengirim email), lalu
      /// langsung kembali tanpa menunggu pekerjaannya selesai.
      /// </summary>
      /// <remarks>
      /// <para>
      /// Pekerjaannya berjalan <b>di luar request</b> ini, bahkan setelah client-nya ditutup. Karena itu,
      /// di dalam pekerjaan itu jangan memakai apa pun milik action ini: <c>this</c>,
      /// <see cref="Request"/>, <see cref="AbortToken"/>, <see cref="GetService{T}()"/>, maupun service yang
      /// diambil dari request. Pakai <see cref="BusinessTaskContext.Services"/> untuk service (DbContext dan
      /// sejenisnya) dan <see cref="BusinessTaskContext.Starter"/> untuk identitas pemulainya. Nilai yang
      /// dibutuhkan dari request (argumen action, isi request) salin ke variabel lokal sebelum memanggil
      /// method ini.
      /// </para>
      /// <para>
      /// <see cref="BusinessTaskContext.CancellationToken"/> hanya menyala saat task dibatalkan atau server
      /// dimatikan. Teruskan ke setiap pemanggilan yang bisa lama; berhenti karenanya dicatat sebagai
      /// dibatalkan. Exception lain membuat task tercatat gagal, dengan pesan exception sebagai pesan
      /// kesalahannya, dan task gagal tampil sampai di-clear.
      /// </para>
      /// <para>
      /// <see cref="BusinessTaskOptions.Scope"/> menentukan siapa yang melihat dan mengurus task ini: task
      /// personal milik pemulainya dan tampil di daftar task pribadinya; task global milik layar module ini,
      /// yang menanyakan statusnya lewat <see cref="FindBusinessTask"/>. Task mungkin menunggu dengan status
      /// antri kalau batas jumlah task yang berjalan bersamaan sudah penuh.
      /// </para>
      /// </remarks>
      /// <param name="options">Kunci, judul, dan cakupan task.</param>
      /// <param name="work">Pekerjaannya.</param>
      /// <returns>Potret task yang baru dimulai.</returns>
      /// <exception cref="ActionException">
      /// 409 kalau task dengan kunci yang sama masih antri atau berjalan; pesannya menyebut siapa yang
      /// memulainya. 401 kalau action ini dipanggil tanpa identitas.
      /// </exception>
      protected BusinessTaskInfo StartBusinessTask(BusinessTaskOptions options, Func<BusinessTaskContext, Task> work) {
         ArgumentNullException.ThrowIfNull(work);
         return BusinessTasks.Start(options, BusinessTaskOutputKind.None, Request, ModuleName, async (ctx, _) => {
            await work(ctx);
            return null;
         });
      }

      /// <summary>
      /// Memulai business task yang hasilnya data JSON (mis. memuat data invoice setahun). Nilai yang
      /// dikembalikan pekerjaannya disimpan server sampai di-clear, dan diambil pemiliknya lewat daftar
      /// task pribadinya.
      /// </summary>
      /// <remarks>
      /// Aturan pemakaian pekerjaannya sama dengan
      /// <see cref="StartBusinessTask(BusinessTaskOptions, Func{BusinessTaskContext, Task})"/>: berjalan di
      /// luar request, jadi hanya boleh memakai isi <see cref="BusinessTaskContext"/>.
      /// </remarks>
      /// <param name="options">Kunci, judul, dan cakupan task.</param>
      /// <param name="work">Pekerjaannya; nilai kembaliannya menjadi hasil task.</param>
      /// <returns>Potret task yang baru dimulai.</returns>
      /// <exception cref="ActionException">409 kalau task dengan kunci yang sama masih antri atau berjalan.</exception>
      protected BusinessTaskInfo StartBusinessTask<TResult>(BusinessTaskOptions options,
         Func<BusinessTaskContext, Task<TResult>> work) {
         ArgumentNullException.ThrowIfNull(work);
         return BusinessTasks.Start(options, BusinessTaskOutputKind.Json, Request, ModuleName, async (ctx, folder) => {
            var result = await work(ctx);
            Directory.CreateDirectory(folder);
            var path = BusinessTaskRunner.ResultPath(folder, BusinessTaskOutputKind.Json);
            await using var file = File.Create(path);
            await System.Text.Json.JsonSerializer.SerializeAsync(file, result, Defaults.ResponseJsonOptions,
               ctx.CancellationToken);
            return file.Length;
         });
      }

      /// <summary>
      /// Memulai business task yang hasilnya sebuah file (mis. Excel). Pekerjaannya menulis isi file itu ke
      /// stream yang diberikan; stream-nya milik engine dan ditutup engine.
      /// <see cref="BusinessTaskOptions.ResultFileName"/> wajib diisi.
      /// </summary>
      /// <remarks>
      /// Aturan pemakaian pekerjaannya sama dengan
      /// <see cref="StartBusinessTask(BusinessTaskOptions, Func{BusinessTaskContext, Task})"/>: berjalan di
      /// luar request, jadi hanya boleh memakai isi <see cref="BusinessTaskContext"/>.
      /// </remarks>
      /// <param name="options">Kunci, judul, cakupan, dan nama file hasil.</param>
      /// <param name="writeResult">Pekerjaannya, yang menulis hasil ke stream yang diberikan.</param>
      /// <returns>Potret task yang baru dimulai.</returns>
      /// <exception cref="ActionException">409 kalau task dengan kunci yang sama masih antri atau berjalan.</exception>
      protected BusinessTaskInfo StartBusinessTask(BusinessTaskOptions options,
         Func<BusinessTaskContext, Stream, Task> writeResult) {
         ArgumentNullException.ThrowIfNull(writeResult);
         return BusinessTasks.Start(options, BusinessTaskOutputKind.File, Request, ModuleName, async (ctx, folder) => {
            Directory.CreateDirectory(folder);
            var path = BusinessTaskRunner.ResultPath(folder, BusinessTaskOutputKind.File);
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
               FileOptions.Asynchronous);
            await writeResult(ctx, file);
            await file.FlushAsync(ctx.CancellationToken);
            return file.Length;
         });
      }

      /// <summary>
      /// Task berkunci <paramref name="key"/> yang masih hidup, atau kalau tidak ada, yang terakhir gagal
      /// dan belum di-clear; <c>null</c> kalau tidak ada keduanya. Untuk
      /// <see cref="BusinessTaskScope.Personal"/> yang dicari milik pemanggil action ini.
      /// </summary>
      /// <remarks>
      /// Method ini tidak memeriksa hak apa pun: siapa yang boleh bertanya diserahkan pada claim action yang
      /// memanggilnya. Karena itu flag <see cref="BusinessTaskInfo.CanCancel"/> dan
      /// <see cref="BusinessTaskInfo.CanClear"/> di hasilnya hanya mengikuti status task;
      /// <see cref="BusinessTaskInfo.CanReadResult"/> tetap hanya untuk pemilik dan administrator.
      /// </remarks>
      protected BusinessTaskInfo? FindBusinessTask(string key, BusinessTaskScope scope = BusinessTaskScope.Global) =>
         BusinessTasks.Find(key, scope, Request);

      /// <summary>
      /// Sama dengan <see cref="FindBusinessTask"/>, untuk semua kunci yang berawalan
      /// <paramref name="keyPrefix"/>: satu hasil per kunci.
      /// </summary>
      /// <remarks>
      /// Method ini tidak memeriksa hak apa pun: siapa yang boleh bertanya diserahkan pada claim action yang
      /// memanggilnya.
      /// </remarks>
      protected BusinessTaskInfo[] FindBusinessTasks(string keyPrefix, BusinessTaskScope scope = BusinessTaskScope.Global) =>
         BusinessTasks.FindMany(keyPrefix, scope, Request);

      /// <summary>
      /// Membatalkan task berkunci <paramref name="key"/> yang masih hidup. Task yang sedang berjalan baru
      /// benar-benar berhenti saat pekerjaannya mengamati token pembatalan.
      /// </summary>
      /// <remarks>
      /// Method ini tidak memeriksa siapa pemulai task-nya: siapa yang boleh membatalkan diserahkan pada
      /// claim action yang memanggilnya.
      /// </remarks>
      /// <exception cref="ActionException">404 kalau tidak ada task berkunci itu, 409 kalau sudah selesai.</exception>
      protected void CancelBusinessTask(string key, BusinessTaskScope scope = BusinessTaskScope.Global) =>
         BusinessTasks.CancelByKey(key, scope, Request);

      /// <summary>
      /// Membersihkan task berkunci <paramref name="key"/> yang sudah selesai, beserta hasilnya yang
      /// tersimpan.
      /// </summary>
      /// <remarks>
      /// Method ini tidak memeriksa siapa pemulai task-nya: siapa yang boleh membersihkan diserahkan pada
      /// claim action yang memanggilnya.
      /// </remarks>
      /// <exception cref="ActionException">404 kalau tidak ada task berkunci itu, 409 kalau masih hidup.</exception>
      protected void ClearBusinessTask(string key, BusinessTaskScope scope = BusinessTaskScope.Global) =>
         BusinessTasks.ClearByKey(key, scope, Request);

      // Only action services start tasks, and every one of them carries [Module].
      private string ModuleName => ModuleAttribute.ResolveName(GetType());

      #endregion
   }
}
