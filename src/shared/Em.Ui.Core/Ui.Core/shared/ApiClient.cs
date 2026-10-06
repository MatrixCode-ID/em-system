using System.Buffers.Text;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Ui.Core.Shared
{
   public class ApiClient : IApiClient
   {
      #region Fields

      private const string _ctlName = "core";
      private HttpClient? _httpClient;
      private HttpClient? _streamHttpClient;
      private HttpClientHandler _httpClientHandler = null!;
      private bool _disposed;

      // Satu pintu untuk seluruh pembaruan token. Refresh token dirotasi setiap dipakai, jadi lima
      // request yang kena 401 bersamaan tanpa pengaman ini akan mengirim lima refresh: yang pertama
      // berhasil, empat sisanya memakai token yang sudah mati dan mencabut sesinya sendiri.
      private readonly SemaphoreSlim _refreshGate = new(1, 1);

      #endregion

      #region Static Initiators and Constructors

      public static ApiClient Create(ApiConnection connection) {
         var result = new ApiClient {
            Connection = connection,
            _httpClientHandler = Defaults.DefaultHttpClientHandler,
         };
         return result;
      }

      public static ApiClient Create(ApiConnection connection, HttpClientHandler httpClientHandler) {
         var result = new ApiClient {
            Connection = connection,
            _httpClientHandler = httpClientHandler
         };
         return result;
      }

      private ApiClient() { }

      #endregion

      #region Properties

      public required ApiConnection Connection { get; init; }

      public HttpClient HttpClient {
         get {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _httpClient ?? ConstructHttpClient();
         }
      }

      /// <summary>
      /// Public key RSA server yang sudah terverifikasi lewat handshake (raw byte DER PKCS#1). Kosong sebelum
      /// <see cref="ResetServerPublicKeyAsync"/> pernah berhasil dijalankan.
      /// </summary>
      public byte[] ServerPublicKey { get; private set; } = [];

      /// <summary>
      /// Id pengguna yang sedang aktif di aplikasi. Dikirim di setiap request bersama
      /// <see cref="ActiveUserAccount"/> sebagai satu objek pada header <see cref="Defaults.UserHeader"/>.
      /// Keduanya selalu diisi dan dikosongkan berbarengan; mengisi separuh berarti mengirim header
      /// yang menyebut orang yang berbeda dari yang dimaksud.
      /// </summary>
      /// <remarks>
      /// Sama seperti nama akunnya, id ini sendiri bukan bukti apa-apa - lihat
      /// <see cref="ActiveUserAccount"/>.
      /// </remarks>
      public string? ActiveUserId { get; set; }

      /// <summary>
      /// Nama akun pengguna yang sedang aktif di aplikasi. Dikirim di setiap request bersama
      /// <see cref="ActiveUserId"/> sebagai satu objek pada header <see cref="Defaults.UserHeader"/>;
      /// kalau keduanya kosong, header-nya tidak dikirim sama sekali.
      /// </summary>
      /// <remarks>
      /// Nama ini sendiri bukan bukti apa-apa: server hanya memperlakukannya sebagai identitas kalau
      /// request-nya juga membawa token debug yang lolos verifikasi. Di luar itu yang menentukan siapa
      /// pemanggilnya tetap access token.
      /// </remarks>
      public string? ActiveUserAccount { get; set; }

      #endregion

      #region Session

      /// <summary>
      /// Access token sesi yang sedang dipegang, atau <c>null</c> kalau belum ada yang masuk lewat
      /// koneksi ini. Terpasang sendiri sebagai header <c>Authorization</c> di setiap request.
      /// </summary>
      public string? AccessToken { get; private set; }

      /// <summary>
      /// Refresh token sesi yang sedang dipegang. Inilah yang ditukar dengan sepasang token baru saat
      /// access token-nya kedaluwarsa; ia dirotasi setiap dipakai, jadi yang tersimpan di mana pun
      /// wajib ikut ditimpa setiap kali <see cref="SessionChanged"/> dipicu.
      /// </summary>
      public string? RefreshToken { get; private set; }

      /// <summary>
      /// Kapan access token yang sedang dipegang kedaluwarsa, dalam UTC. Belum dipakai sebagai pemicu
      /// apa pun - pembaruan token di sini reaktif, yaitu saat server menjawab 401.
      /// </summary>
      public DateTimeOffset AccessTokenExpiresAtUtc { get; private set; }

      /// <summary>
      /// Pemilik sesi yang sedang dipegang, menurut server yang menerbitkannya.
      /// </summary>
      public string? SessionUserId { get; private set; }

      /// <summary><c>true</c> kalau koneksi ini sedang memegang sesi.</summary>
      public bool HasSession => !string.IsNullOrEmpty(AccessToken);

      /// <summary>
      /// Dipicu setiap kali isi sesi berganti - saat sesi dibuka, saat hasil rotasi masuk, dan saat
      /// sesinya dibuang. Yang menyimpan refresh token lintas restart mendengarkan event ini: tanpa
      /// menimpa yang tersimpan, restart berikutnya memakai token mati.
      /// </summary>
      public event EventHandler? SessionChanged;

      /// <summary>
      /// Dipicu tepat sekali saat sesi mati dan tidak bisa dipulihkan - refresh gagal, atau memang
      /// tidak ada refresh token untuk dipakai. <see cref="ApiClient"/> tidak tahu apa-apa soal window
      /// maupun layar login; ia hanya mengumumkan bahwa yang dipegangnya sudah tidak berlaku.
      /// </summary>
      public event EventHandler? SessionEnded;

      /// <summary>
      /// Memasang sesi hasil sign in atau hasil rotasi refresh.
      /// </summary>
      /// <param name="token">Pasangan token yang baru diterbitkan server.</param>
      public void SetSession(TokenResult token) {
         ArgumentNullException.ThrowIfNull(token);

         AccessToken = token.AccessToken;
         RefreshToken = token.RefreshToken;
         SessionUserId = token.cUserId;
         AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
         SessionChanged?.Invoke(this, EventArgs.Empty);
      }

      /// <summary>
      /// Membuang sesi yang sedang dipegang. Tidak memberi tahu server apa pun - yang mengakhiri sesi
      /// di sana adalah action sign out, dan itu urusan pemanggilnya.
      /// </summary>
      public void ClearSession() {
         if (!HasSession && RefreshToken is null) return;

         AccessToken = null;
         RefreshToken = null;
         SessionUserId = null;
         AccessTokenExpiresAtUtc = default;
         SessionChanged?.Invoke(this, EventArgs.Empty);
      }

      // Sesi yang mati sendiri di tengah jalan: dibuang, lalu diumumkan sekali. Dipisahkan dari
      // ClearSession supaya sign out yang diminta user - yang mengurus tampilannya sendiri - tidak
      // ikut memicu pengumuman yang sama dua kali.
      private void EndSession() {
         var hadSession = HasSession || RefreshToken is not null;
         ClearSession();
         if (hadSession) SessionEnded?.Invoke(this, EventArgs.Empty);
      }

      #endregion

      #region HTTP Operation Methods

      public Task<T> GetAsync<T>(string controller, string action, params object[] args) {
         var url = BuildGetUrl(controller, action, args);
         return SendAsync<T>(() => BuildRequest(System.Net.Http.HttpMethod.Get, url));
      }

      /// <summary>
      /// Memanggil action GET yang mengembalikan isi file, bukan data JSON. Stream yang dikembalikan dibaca
      /// langsung dari jaringan, jadi isi yang besar tidak pernah ditampung utuh di memori.
      /// </summary>
      /// <param name="controller">Nama module tujuan.</param>
      /// <param name="action">Nama action tujuan.</param>
      /// <param name="args">Argumen action, dikirim seperti pada <see cref="GetAsync{T}"/>.</param>
      /// <returns>
      /// Stream isi jawaban server. Pemanggil wajib menutupnya; menutupnya ikut melepas koneksinya.
      /// </returns>
      /// <remarks>
      /// Request ini tidak dibatasi waktu, karena lama unduhnya bergantung pada ukuran isinya. Penanganan
      /// token yang kedaluwarsa dan penolakan server sama dengan <see cref="GetAsync{T}"/>: penolakan
      /// dilempar sebagai <see cref="ActionException"/> sebelum stream-nya dikembalikan.
      /// </remarks>
      public Task<Stream> GetStreamAsync(string controller, string action, params object[] args) {
         var url = BuildGetUrl(controller, action, args);
         return SendAsync(() => BuildRequest(System.Net.Http.HttpMethod.Get, url),
            request => StreamHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
               .ProcessHttpStreamResult());
      }

      public async Task PostAsync(string controller, string action, params object[] args) {
         var url = $"api/{controller}/{action}";
         var bodyJson = BuildPostBody(args);
         await SendAsync<object?>(() => BuildRequest(System.Net.Http.HttpMethod.Post, url,
            new StringContent(bodyJson, Encoding.UTF8, "application/json")));
      }

      public Task<T> PostAsync<T>(string controller, string action, params object[] args) {
         var url = $"api/{controller}/{action}";
         var bodyJson = BuildPostBody(args);
         return SendAsync<T>(() => BuildRequest(System.Net.Http.HttpMethod.Post, url,
            new StringContent(bodyJson, Encoding.UTF8, "application/json")));
      }

      /// <summary>
      /// Calls a POST action with its own time limit instead of the connection's standard timeout, for actions that
      /// are known to run long on the server (for example a container deploy).
      /// </summary>
      /// <param name="timeout">How long to wait for the answer before giving up with <see cref="TimeoutException"/>.</param>
      /// <param name="controller">Target module name.</param>
      /// <param name="action">Target action name.</param>
      /// <param name="args">Action arguments, sent as in <see cref="PostAsync{T}(string, string, object[])"/>.</param>
      /// <exception cref="TimeoutException">No answer within <paramref name="timeout"/>. The server may still finish the work.</exception>
      public async Task<T> PostAsync<T>(TimeSpan timeout, string controller, string action, params object[] args) {
         var url = $"api/{controller}/{action}";
         var bodyJson = BuildPostBody(args);
         using var limit = new CancellationTokenSource(timeout);
         try {
            return await SendAsync(() => BuildRequest(System.Net.Http.HttpMethod.Post, url,
                  new StringContent(bodyJson, Encoding.UTF8, "application/json")),
               request => StreamHttpClient.SendAsync(request, limit.Token).ProcessHttpResult<T>());
         }
         catch (OperationCanceledException) when (limit.IsCancellationRequested) {
            throw new TimeoutException($"The server did not answer {action} within {timeout.TotalMinutes:0.#} minutes. It may still be running; check its result later.");
         }
      }

      /// <inheritdoc cref="PostAsync{T}(TimeSpan, string, string, object[])"/>
      public async Task PostAsync(TimeSpan timeout, string controller, string action, params object[] args) {
         await PostAsync<object?>(timeout, controller, action, args);
      }

      /// <summary>
      /// Memanggil action ber-stream yang tidak mengembalikan nilai: isi <paramref name="content"/>
      /// dikirim mentah sebagai body request, dan <paramref name="payload"/> - kalau ada - ikut di
      /// header <see cref="Defaults.StreamPayloadHeader"/>.
      /// </summary>
      /// <param name="controller">Nama module tujuan.</param>
      /// <param name="action">Nama action tujuan.</param>
      /// <param name="content">
      /// Stream yang dikirim, dibaca dari posisinya sekarang sampai habis. Tidak ditutup oleh method ini -
      /// yang membukanya yang menutupnya. Kalau stream-nya bisa di-seek, request yang kena 401 dikirim
      /// ulang dari posisi awal sesudah token diperbarui; kalau tidak, 401 itu dilempar apa adanya.
      /// </param>
      /// <param name="payload">
      /// Objek yang diterima parameter selain stream di action tujuan, atau <c>null</c> kalau action-nya
      /// hanya menerima stream.
      /// </param>
      /// <remarks>
      /// Request ini tidak dibatasi waktu, karena lama kirimnya bergantung pada ukuran stream - untuk
      /// menghentikannya, buat stream-nya melempar <see cref="OperationCanceledException"/> saat dibaca.
      /// Request dikirim dengan <c>Expect: 100-continue</c>, jadi yang ditolak server sebelum body-nya
      /// dibaca (401, 403, 409, dan sejenisnya) tidak membuat isi stream ikut terkirim.
      /// </remarks>
      /// <exception cref="InvalidOperationException">
      /// <paramref name="payload"/> terlalu besar untuk sebuah header (lihat
      /// <see cref="StreamPayloadProtocol.MaxHeaderLength"/>). Ditolak sebelum apa pun dikirim.
      /// </exception>
      public async Task PostStreamAsync(string controller, string action, Stream content, object? payload = null) {
         await PostStreamAsync<object?>(controller, action, content, payload);
      }

      /// <summary>
      /// Memanggil action ber-stream dan mengembalikan hasilnya. Sama dengan
      /// <see cref="PostStreamAsync(string, string, Stream, object?)"/>, hanya saja jawaban server dibaca
      /// menjadi <typeparamref name="T"/>.
      /// </summary>
      /// <inheritdoc cref="PostStreamAsync(string, string, Stream, object?)"/>
      public Task<T> PostStreamAsync<T>(string controller, string action, Stream content, object? payload = null) {
         ArgumentNullException.ThrowIfNull(content);

         var url = $"api/{controller}/{action}";
         // Encoded before anything is sent, so a payload that does not fit a header is refused here and
         // not by the server after a connection has been opened.
         var header = payload is null ? null : StreamPayloadProtocol.Encode(payload);
         long? startPosition = content.CanSeek ? content.Position : null;

         return SendAsync<T>(StreamHttpClient, () => {
            if (startPosition is { } start && content.Position != start) {
               content.Position = start;
            }

            var body = new StreamContent(new NonClosingStream(content));
            body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            var request = BuildRequest(System.Net.Http.HttpMethod.Post, url, body);
            request.Headers.ExpectContinue = true;
            if (header is not null) {
               request.Headers.TryAddWithoutValidation(Defaults.StreamPayloadHeader, header);
            }

            return request;
         }, canRetry: () => startPosition is not null);
      }

      /// <summary>
      /// Menyusun body sebuah request POST: daftar argumen dalam bentuk <see cref="PostMethodPayload"/>,
      /// dicocokkan server berdasarkan nomor urut.
      /// </summary>
      private static string BuildPostBody(object?[] args) {
         List<PostMethodPayload> payloads = [];
         for (int i = 0; i < args.Length; i++) {
            // Argumen null sengaja tidak dikirim sama sekali, sama seperti pada GET: server memperlakukan
            // ordinal yang tidak ada sebagai null/nilai default parameter. Karena pencocokannya ordinal,
            // melewati satu entri tidak menggeser posisi argumen sesudahnya.
            if (args[i] is null) {
               continue;
            }

            payloads.Add(PostMethodPayload.Build(i, args[i]!));
         }

         return JsonSerializer.Serialize(payloads);
      }

      /// <summary>
      /// Mengirim satu request dan, kalau server menjawab 401, memperbarui token lalu mengirimnya sekali
      /// lagi. Seluruh jalur request lewat sini supaya pembaruan token tidak perlu diingat satu per satu
      /// di setiap pemanggilan.
      /// </summary>
      /// <param name="requestFactory">
      /// Penyusun request-nya. Bentuknya factory, bukan objek jadi, karena
      /// <see cref="HttpRequestMessage"/> tidak bisa dipakai dua kali - dan karena itu pula isi body pada
      /// jalur POST harus dibangun di dalamnya.
      /// </param>
      private Task<T> SendAsync<T>(Func<HttpRequestMessage> requestFactory) =>
         SendAsync<T>(HttpClient, requestFactory);

      /// <param name="client">Client yang dipakai mengirim - yang biasa, atau yang tanpa batas waktu untuk stream.</param>
      /// <param name="requestFactory">Lihat overload tanpa <paramref name="client"/>.</param>
      /// <param name="canRetry">
      /// Ditanya sebelum token diperbarui: <c>false</c> berarti request ini tidak bisa diulang (body-nya
      /// stream yang sudah terbaca dan tidak bisa di-seek), dan 401-nya dilempar apa adanya. <c>null</c>
      /// berarti selalu bisa.
      /// </param>
      private Task<T> SendAsync<T>(HttpClient client, Func<HttpRequestMessage> requestFactory,
         Func<bool>? canRetry = null) =>
         SendAsync(requestFactory, request => client.SendAsync(request).ProcessHttpResult<T>(), canRetry);

      /// <param name="requestFactory">Lihat overload tanpa <c>client</c>.</param>
      /// <param name="send">Mengirim satu request dan membaca jawabannya - sebagai amplop JSON atau sebagai stream.</param>
      /// <param name="canRetry">Lihat overload dengan <c>client</c>.</param>
      private async Task<T> SendAsync<T>(Func<HttpRequestMessage> requestFactory,
         Func<HttpRequestMessage, Task<T>> send, Func<bool>? canRetry = null) {
         // Tanpa sesi tidak ada yang bisa diperbarui, jadi request-nya dikirim sekali apa adanya.
         if (!HasSession) {
            using var plain = requestFactory();
            return await send(plain);
         }

         // Dicatat sebelum dikirim: kalau sesudah 401 nilainya sudah berbeda, berarti request lain
         // sudah memperbarui token lebih dulu dan yang ini tinggal mengulang dengan yang baru.
         var attemptedToken = AccessToken;

         try {
            using var request = requestFactory();
            return await send(request);
         }
         catch (ActionException x) when (x.StatusCode == 401 && (canRetry is null || canRetry())) {
            if (!await EnsureRefreshedAsync(attemptedToken)) {
               EndSession();
               throw;
            }

            // Sekali saja, tidak ada loop: 401 kedua sesudah token baru berarti masalahnya bukan token.
            try {
               using var retry = requestFactory();
               return await send(retry);
            }
            catch (ActionException retryFailure) when (retryFailure.StatusCode == 401) {
               EndSession();
               throw;
            }
         }
      }

      /// <summary>
      /// Memastikan sesi ini sudah memegang access token yang lebih baru daripada
      /// <paramref name="attemptedToken"/>, dengan menukar refresh token kalau perlu. Beberapa request
      /// yang kena 401 bersamaan menunggu satu pembaruan yang sama.
      /// </summary>
      /// <returns><c>true</c> kalau sesudah ini ada access token baru yang bisa dicoba.</returns>
      private async Task<bool> EnsureRefreshedAsync(string? attemptedToken) {
         await _refreshGate.WaitAsync();
         try {
            // Ada yang sudah memperbarui sementara kita antre. Tidak boleh memakai refresh token lagi -
            // ia sudah dirotasi, dan memakainya kedua kali justru mematikan sesinya sendiri.
            if (!string.Equals(AccessToken, attemptedToken, StringComparison.Ordinal)) return HasSession;
            if (string.IsNullOrEmpty(RefreshToken)) return false;

            try {
               SetSession(await SendRefreshAsync(RefreshToken));
               return true;
            }
            catch (Exception) {
               // Kenapa refresh-nya gagal tidak mengubah apa pun di sini: yang dipegang sudah tidak
               // berlaku, dan pemanggil di atas yang mengakhiri sesinya.
               return false;
            }
         }
         finally {
            _refreshGate.Release();
         }
      }

      /// <summary>
      /// Menukar refresh token dengan sepasang token baru. Dikirim lewat jalur mentah - tidak melewati
      /// <see cref="SendAsync{T}"/> dan tidak memasang header <c>Authorization</c> - karena 401 di
      /// dalamnya akan memicu pembaruan lagi, dan rekursi itu tidak punya dasar berhenti yang jelas.
      /// </summary>
      private async Task<TokenResult> SendRefreshAsync(string refreshToken) {
         var url =
            $"api/{Defaults.CredentialModuleName}/{nameof(ICredentialServices.PostGetMeta_RefreshToken)}";
         using var request = BuildRequest(System.Net.Http.HttpMethod.Post, url,
            new StringContent(BuildPostBody([refreshToken]), Encoding.UTF8, "application/json"),
            withAuthorization: false);
         return await HttpClient.SendAsync(request).ProcessHttpResult<TokenResult>();
      }

      /// <summary>
      /// Menyusun satu request beserta header identitasnya. Header dipasang per-request, bukan sekali di
      /// <c>HttpClient.DefaultRequestHeaders</c>, karena semuanya berubah saat aplikasi berjalan: identitas
      /// mengikuti pengguna yang sedang aktif, token debug harus bisa dimatikan tanpa restart, dan access
      /// token berganti setiap kali dirotasi - kalau dipasang sekali, request yang disusun sebelum rotasi
      /// akan tetap membawa token yang lama.
      /// </summary>
      /// <param name="withAuthorization">
      /// <c>false</c> hanya untuk jalur pembaruan token, yang justru dikirim ketika access token-nya sudah
      /// mati dan karena itu tidak perlu membawanya.
      /// </param>
      private HttpRequestMessage BuildRequest(System.Net.Http.HttpMethod method, string url,
         HttpContent? content = null, bool withAuthorization = true) {
         var request = new HttpRequestMessage(method, url) {
            Content = content
         };

         if (!string.IsNullOrWhiteSpace(Connection.DebugToken)) {
            request.Headers.TryAddWithoutValidation(Defaults.DebugTokenHeader, Connection.DebugToken);
         }

         if (withAuthorization && !string.IsNullOrWhiteSpace(AccessToken)) {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {AccessToken}");
         }

         if (UserHeaderProtocol.Create(ActiveUserId, ActiveUserAccount) is { Length: > 0 } identity) {
            request.Headers.TryAddWithoutValidation(Defaults.UserHeader, identity);
         }

         return request;
      }

      /// <summary>
      /// Menjalankan ulang handshake ke server lalu memperbarui <see cref="ServerPublicKey"/>. Dipanggil saat
      /// koneksi API berganti/berubah atau saat key server dicurigai sudah di-rotate, sehingga key lokal
      /// tidak basi.
      /// </summary>
      public async Task ResetServerPublicKeyAsync() {
         SetServerPublicKey(await HandshakeAsync());
      }

      internal void SetServerPublicKey(byte[] key) {
         ServerPublicKey = key;
      }

      /// <summary>
      /// Memverifikasi bahwa host pada <see cref="Connection"/> benar-benar memegang private key dari public key
      /// yang dikembalikannya: client mengirim nonce acak, server menandatanganinya, lalu tanda tangan itu
      /// diverifikasi di sini. Public key hanya dianggap sah kalau verifikasi lolos.
      /// </summary>
      /// <returns>Public key server (raw byte DER PKCS#1) yang sudah terbukti cocok dengan private key-nya.</returns>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau tanda tangan server tidak cocok dengan nonce yang dikirim — host-nya bukan server EM,
      /// atau responsnya sudah diubah di tengah jalan.
      /// </exception>
      /// <remarks>
      /// Hasilnya sengaja hanya dikembalikan, tidak langsung disimpan ke <see cref="ServerPublicKey"/>; pakai
      /// <see cref="ResetServerPublicKeyAsync"/> kalau key-nya memang mau dipakai seterusnya.
      /// <para>
      /// Handshake ini membuktikan kepemilikan key, bukan identitas host. Yang menjamin host tidak dipalsukan
      /// adalah TLS — di produksi koneksi wajib HTTPS dengan sertifikat tervalidasi. Kalau TLS dilewati (mis.
      /// <c>ApiConnection.IgnoreSslErrors</c> menyala), pihak di tengah bisa menyisipkan public key miliknya
      /// sendiri, menandatangani nonce dengan private key-nya, dan tetap lolos verifikasi.
      /// </para>
      /// </remarks>
      public async Task<byte[]> HandshakeAsync() {
         var nonce = RandomNumberGenerator.GetBytes(ProbeProtocol.DefaultNonceLength);
         var result =
            await GetAsync<ServerHandshakeResult>(_ctlName, "handshake", ProbeProtocol.EncodeNonce(nonce));
         var serverKey = RsaKeyPair.Create(result.PublicKey);
         if (!serverKey.VerifyData(ProbeProtocol.BuildSignaturePayload(nonce),
                Base64Url.DecodeFromChars(result.Signature))) {
            throw new InvalidOperationException(
               "Handshake failed: the signature over the client nonce is not valid for the public key the server " +
               "returned. The host may not be an EM API server, or the response was tampered with.");
         }

         return ServerPublicKey = serverKey.GetPublicBytes();
      }

      private HttpClient ConstructHttpClient() {
         // BaseAddress wajib berakhiran '/', kalau tidak segmen terakhir host akan terpotong saat digabung
         // dengan URI relatif seperti "api/core/Handshake".
         // disposeHandler: false — handler dilepas sendiri di Dispose, supaya tetap ikut terlepas walau
         // HttpClient belum pernah dibuat sama sekali.
         _httpClient = new HttpClient(_httpClientHandler, false) {
            BaseAddress = BuildBaseAddress(),
            Timeout = TimeSpan.FromSeconds(
               Connection.Timeout > 0 ? Connection.Timeout : Defaults.StandardTimeoutSeconds)
         };
         return _httpClient!;
      }

      // The client for PostStreamAsync and GetStreamAsync: same handler - and so the same connection pool - but no timeout,
      // since how long a stream takes to send depends on its size, not on the server.
      private HttpClient StreamHttpClient {
         get {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _streamHttpClient ??= new HttpClient(_httpClientHandler, false) {
               BaseAddress = BuildBaseAddress(),
               Timeout = Timeout.InfiniteTimeSpan
            };
         }
      }

      private Uri BuildBaseAddress() {
         if (string.IsNullOrWhiteSpace(Connection.Host)) {
            throw new InvalidOperationException($"{nameof(ApiConnection.Host)} is empty; cannot build an HTTP client.");
         }

         return new Uri(Connection.Host.EndsWith('/') ? Connection.Host : $"{Connection.Host}/");
      }

      #endregion

      #region General Tools

      public Task<DateTimeOffset> GetServerTimeStampOffsetAsync()
         => GetAsync<DateTimeOffset>(_ctlName, "GetTimeStamp");

      public async Task<DateTime> GetServerTimeStampAsync() {
         var result = await GetServerTimeStampOffsetAsync();
         return result.LocalDateTime;
      }
      public Task<Ulid> GetUlidAsync() => GetAsync<Ulid>(_ctlName, "GetUlid");
      public Task<Ulid[]> GetUlidManyAsync(int count) => GetAsync<Ulid[]>(_ctlName, "GetUlidMany", count);

      #region Base Tools

      /// <summary>
      /// Melepas <see cref="HttpClient"/> milik instance ini beserta handler dan koneksi TCP yang masih terbuka.
      /// Panggil saat profil koneksi diganti atau saat client sudah tidak dipakai; sesudah ini instance tidak boleh
      /// dipakai lagi. Handler yang disuplai lewat <see cref="Create(ApiConnection, HttpClientHandler)"/> ikut
      /// di-dispose, jadi jangan dipakai bersama-sama dengan instance <see cref="ApiClient"/> lain.
      /// </summary>
      public void Dispose() {
         Dispose(true);
         GC.SuppressFinalize(this);
      }

      protected virtual void Dispose(bool disposing) {
         if (_disposed) {
            return;
         }

         if (disposing) {
            _httpClient?.Dispose();
            _httpClient = null;
            _streamHttpClient?.Dispose();
            _streamHttpClient = null;
            _httpClientHandler.Dispose();
            _refreshGate.Dispose();
         }

         _disposed = true;
      }

      /// <summary>
      /// Menyusun URL relatif untuk action GET: <c>api/{controller}/{action}</c> plus argumen sebagai query
      /// string bernama <c>par1</c>, <c>par2</c>, dan seterusnya sesuai urutan argumen. Server mem-binding
      /// parameter action berdasarkan nomor urut ini, bukan berdasarkan nama parameter.
      /// </summary>
      public string BuildGetUrl(string controller, string action, object?[] args) {
         var url = $"api/{controller}/{action}";
         if (args.Length == 0) {
            return url;
         }

         var query = new StringBuilder();
         for (var index = 0; index < args.Length; index++) {
            // Argumen null sengaja tidak dikirim sama sekali: server memperlakukan parN yang tidak ada sebagai
            // null/nilai default parameter. Karena penamaannya ordinal, melewati satu parN tidak menggeser
            // posisi argumen sesudahnya.
            if (FormatArgument(args[index]) is not { } value) {
               continue;
            }

            query.Append(query.Length == 0 ? '?' : '&')
               .Append("par").Append(index + 1).Append('=')
               .Append(Uri.EscapeDataString(value));
         }

         return $"{url}{query}";
      }

      /// <summary>
      /// Mengubah satu argumen jadi teks query string. Selalu memakai <see cref="CultureInfo.InvariantCulture"/>
      /// karena server membaca ulang nilainya lewat <c>ConvertFromInvariantString</c> — kalau client memakai
      /// culture lokal (mis. koma sebagai pemisah desimal) nilainya akan salah baca di server.
      /// </summary>
      private static string? FormatArgument(object? value) => value switch {
         null => null,
         string text => text,
         // Format "O" (round-trip) mempertahankan pecahan detik dan offset zona waktu, yang akan hilang kalau
         // dibiarkan memakai format default TypeConverter.
         DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
         DateTimeOffset dateOffset => dateOffset.ToString("O", CultureInfo.InvariantCulture),
         IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
         _ => TypeDescriptor.GetConverter(value.GetType()).ConvertToInvariantString(value) ?? value.ToString()
      };

      #endregion

      #endregion

      /// <summary>
      /// Passes everything through to the caller's stream except closing it. StreamContent disposes its
      /// stream together with the request, and the stream belongs to whoever called PostStreamAsync -
      /// who may still need it, for a retry after 401 if nothing else.
      /// </summary>
      private sealed class NonClosingStream(Stream inner) : Stream
      {
         public override bool CanRead => inner.CanRead;
         public override bool CanSeek => inner.CanSeek;
         public override bool CanWrite => false;
         public override long Length => inner.Length;

         public override long Position {
            get => inner.Position;
            set => inner.Position = value;
         }

         public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

         public override int Read(Span<byte> buffer) => inner.Read(buffer);

         public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

         public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

         public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

         public override void Flush() { }

         public override void SetLength(long value) => throw new NotSupportedException();

         public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
      }
   }
}
