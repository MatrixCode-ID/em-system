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
   /// <summary>Client of the Em API: composes requests, attaches identity headers, and renews tokens.</summary>
   public class ApiClient : IApiClient
   {
      #region Fields

      private const string _ctlName = "core";
      private HttpClient? _httpClient;
      private HttpClient? _streamHttpClient;
      private HttpClientHandler _httpClientHandler = null!;
      private bool _disposed;

      // A single door for all token refreshes. The refresh token is rotated every time it is used, so five
      // requests that hit 401 at the same time without this safeguard would send five refreshes: the first
      // succeeds, and the other four use an already dead token and revoke their own session.
      private readonly SemaphoreSlim _refreshGate = new(1, 1);

      #endregion

      #region Static Initiators and Constructors

      /// <summary>Creates a client for a connection profile with its default handler.</summary>
      public static ApiClient Create(ApiConnection connection) {
         var result = new ApiClient {
            Connection = connection,
            _httpClientHandler = Defaults.DefaultHttpClientHandler,
         };
         return result;
      }

      /// <summary>Creates a client for a connection profile with the given handler, which the client disposes.</summary>
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

      /// <summary>The connection profile this client talks to.</summary>
      public required ApiConnection Connection { get; init; }

      /// <summary>The underlying <see cref="System.Net.Http.HttpClient"/>, created on first use.</summary>
      public HttpClient HttpClient {
         get {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _httpClient ?? ConstructHttpClient();
         }
      }

      /// <summary>
      /// The server's RSA public key, verified through the handshake (raw DER PKCS#1 bytes). Empty until
      /// <see cref="ResetServerPublicKeyAsync"/> has succeeded at least once.
      /// </summary>
      public byte[] ServerPublicKey { get; private set; } = [];

      /// <summary>
      /// Id of the user currently active in the application. Sent with every request together with
      /// <see cref="ActiveUserAccount"/> as a single object in the <see cref="Defaults.UserHeader"/> header.
      /// Both are always filled and cleared together; filling only half would send a header naming a
      /// different person from the one meant.
      /// </summary>
      /// <remarks>
      /// Like the account name, this id is no proof of anything by itself - see
      /// <see cref="ActiveUserAccount"/>.
      /// </remarks>
      public string? ActiveUserId { get; set; }

      /// <summary>
      /// Account name of the user currently active in the application. Sent with every request together with
      /// <see cref="ActiveUserId"/> as a single object in the <see cref="Defaults.UserHeader"/> header; when
      /// both are empty, the header is not sent at all.
      /// </summary>
      /// <remarks>
      /// This name is no proof of anything by itself: the server only treats it as an identity when the
      /// request also carries a debug token that passes verification. Otherwise what decides who the caller is
      /// remains the access token.
      /// </remarks>
      public string? ActiveUserAccount { get; set; }

      #endregion

      #region Session

      /// <summary>
      /// The access token of the session currently held, or <c>null</c> when nobody has signed in through
      /// this connection. Attached automatically as the <c>Authorization</c> header on every request.
      /// </summary>
      public string? AccessToken { get; private set; }

      /// <summary>
      /// The refresh token of the session currently held. This is what is exchanged for a new pair of tokens
      /// when the access token expires; it is rotated every time it is used, so wherever it is stored it must
      /// be overwritten every time <see cref="SessionChanged"/> is raised.
      /// </summary>
      public string? RefreshToken { get; private set; }

      /// <summary>
      /// When the access token currently held expires, in UTC. Not used as a trigger for anything yet - token
      /// renewal here is reactive, that is, when the server answers 401.
      /// </summary>
      public DateTimeOffset AccessTokenExpiresAtUtc { get; private set; }

      /// <summary>
      /// The owner of the session currently held, according to the server that issued it.
      /// </summary>
      public string? SessionUserId { get; private set; }

      /// <summary><c>true</c> when this connection is currently holding a session.</summary>
      public bool HasSession => !string.IsNullOrEmpty(AccessToken);

      /// <summary>
      /// Raised every time the session content changes - when the session is opened, when the result of a
      /// rotation comes in, and when the session is discarded. Whoever stores the refresh token across
      /// restarts listens to this event: without overwriting what is stored, the next restart uses a dead
      /// token.
      /// </summary>
      public event EventHandler? SessionChanged;

      /// <summary>
      /// Raised exactly once when the session dies and cannot be recovered - the refresh failed, or there is
      /// no refresh token to use. <see cref="ApiClient"/> knows nothing about windows or the login screen; it
      /// only announces that what it held is no longer valid.
      /// </summary>
      public event EventHandler? SessionEnded;

      /// <summary>
      /// Installs the session resulting from a sign-in or from a refresh rotation.
      /// </summary>
      /// <param name="token">The pair of tokens newly issued by the server.</param>
      public void SetSession(TokenResult token) {
         ArgumentNullException.ThrowIfNull(token);

         AccessToken = token.AccessToken;
         RefreshToken = token.RefreshToken;
         SessionUserId = token.cUserId;
         AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
         SessionChanged?.Invoke(this, EventArgs.Empty);
      }

      /// <summary>
      /// Discards the session currently held. Tells the server nothing - what ends the session there is the
      /// sign-out action, and that is the caller's business.
      /// </summary>
      public void ClearSession() {
         if (!HasSession && RefreshToken is null) return;

         AccessToken = null;
         RefreshToken = null;
         SessionUserId = null;
         AccessTokenExpiresAtUtc = default;
         SessionChanged?.Invoke(this, EventArgs.Empty);
      }

      // A session that died by itself midway: discarded, then announced once. Kept apart from ClearSession
      // so that a sign-out requested by the user - which handles its own display - does not trigger the same
      // announcement twice.
      private void EndSession() {
         var hadSession = HasSession || RefreshToken is not null;
         ClearSession();
         if (hadSession) SessionEnded?.Invoke(this, EventArgs.Empty);
      }

      #endregion

      #region HTTP Operation Methods

      /// <inheritdoc />
      public Task<T> GetAsync<T>(string controller, string action, params object[] args) {
         var url = BuildGetUrl(controller, action, args);
         return SendAsync<T>(() => BuildRequest(System.Net.Http.HttpMethod.Get, url));
      }

      /// <summary>
      /// Calls a GET action that returns file content, not JSON data. The returned stream is read directly
      /// from the network, so large content is never held in memory in full.
      /// </summary>
      /// <param name="controller">Name of the target module.</param>
      /// <param name="action">Name of the target action.</param>
      /// <param name="args">Arguments of the action, sent as in <see cref="GetAsync{T}"/>.</param>
      /// <returns>
      /// The stream of the server's answer content. The caller must close it; closing it also releases its
      /// connection.
      /// </returns>
      /// <remarks>
      /// This request has no time limit, because how long the download takes depends on the content size.
      /// Handling of an expired token and of server refusal is the same as <see cref="GetAsync{T}"/>: a
      /// refusal is thrown as an <see cref="ActionException"/> before the stream is returned.
      /// </remarks>
      public Task<Stream> GetStreamAsync(string controller, string action, params object[] args) {
         var url = BuildGetUrl(controller, action, args);
         return SendAsync(() => BuildRequest(System.Net.Http.HttpMethod.Get, url),
            request => StreamHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
               .ProcessHttpStreamResult());
      }

      /// <inheritdoc />
      public async Task PostAsync(string controller, string action, params object[] args) {
         var url = $"api/{controller}/{action}";
         var bodyJson = BuildPostBody(args);
         await SendAsync<object?>(() => BuildRequest(System.Net.Http.HttpMethod.Post, url,
            new StringContent(bodyJson, Encoding.UTF8, "application/json")));
      }

      /// <inheritdoc />
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
      /// Calls a streaming action that returns no value: the content of <paramref name="content"/> is sent
      /// raw as the request body, and <paramref name="payload"/> - when present - goes in the
      /// <see cref="Defaults.StreamPayloadHeader"/> header.
      /// </summary>
      /// <param name="controller">Name of the target module.</param>
      /// <param name="action">Name of the target action.</param>
      /// <param name="content">
      /// The stream to send, read from its current position to the end. Not closed by this method - whoever
      /// opened it closes it. When the stream can seek, a request that hits 401 is sent again from the start
      /// after the token is refreshed; otherwise that 401 is thrown as-is.
      /// </param>
      /// <param name="payload">
      /// The object received by the parameter other than the stream in the target action, or <c>null</c>
      /// when the action only accepts a stream.
      /// </param>
      /// <remarks>
      /// This request has no time limit, because how long it takes to send depends on the stream size - to
      /// stop it, make the stream throw <see cref="OperationCanceledException"/> when read. The request is
      /// sent with <c>Expect: 100-continue</c>, so something the server refuses before its body is read (401,
      /// 403, 409, and the like) does not cause the stream content to be sent.
      /// </remarks>
      /// <exception cref="InvalidOperationException">
      /// <paramref name="payload"/> is too large for a header (see
      /// <see cref="StreamPayloadProtocol.MaxHeaderLength"/>). Refused before anything is sent.
      /// </exception>
      public async Task PostStreamAsync(string controller, string action, Stream content, object? payload = null) {
         await PostStreamAsync<object?>(controller, action, content, payload);
      }

      /// <summary>
      /// Calls a streaming action and returns its result. Same as
      /// <see cref="PostStreamAsync(string, string, Stream, object?)"/>, except that the server's answer is
      /// read into <typeparamref name="T"/>.
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
      /// Builds the body of a POST request: the list of arguments in the form of
      /// <see cref="PostMethodPayload"/>, matched by the server by ordinal.
      /// </summary>
      private static string BuildPostBody(object?[] args) {
         List<PostMethodPayload> payloads = [];
         for (int i = 0; i < args.Length; i++) {
            // A null argument is deliberately not sent at all, just like with GET: the server treats a missing
            // ordinal as null/the parameter's default value. Because matching is ordinal, skipping one entry does
            // not shift the position of the arguments after it.
            if (args[i] is null) {
               continue;
            }

            payloads.Add(PostMethodPayload.Build(i, args[i]!));
         }

         return JsonSerializer.Serialize(payloads);
      }

      /// <summary>
      /// Sends one request and, if the server answers 401, refreshes the token and sends it once more. All
      /// request paths go through here so token renewal does not have to be remembered at every call.
      /// </summary>
      /// <param name="requestFactory">
      /// Builder of the request. It is a factory, not a finished object, because
      /// <see cref="HttpRequestMessage"/> cannot be used twice - and for the same reason the body content on
      /// the POST path must be built inside it.
      /// </param>
      private Task<T> SendAsync<T>(Func<HttpRequestMessage> requestFactory) =>
         SendAsync<T>(HttpClient, requestFactory);

      /// <param name="client">The client used to send - the ordinary one, or the one without time limit for streams.</param>
      /// <param name="requestFactory">See the overload without <paramref name="client"/>.</param>
      /// <param name="canRetry">
      /// Asked before the token is refreshed: <c>false</c> means this request cannot be repeated (its body is
      /// a stream that was already read and cannot seek), and its 401 is thrown as-is. <c>null</c> means it
      /// always can.
      /// </param>
      private Task<T> SendAsync<T>(HttpClient client, Func<HttpRequestMessage> requestFactory,
         Func<bool>? canRetry = null) =>
         SendAsync(requestFactory, request => client.SendAsync(request).ProcessHttpResult<T>(), canRetry);

      /// <param name="requestFactory">See the overload without <c>client</c>.</param>
      /// <param name="send">Sends one request and reads its answer - as a JSON envelope or as a stream.</param>
      /// <param name="canRetry">See the overload with <c>client</c>.</param>
      private async Task<T> SendAsync<T>(Func<HttpRequestMessage> requestFactory,
         Func<HttpRequestMessage, Task<T>> send, Func<bool>? canRetry = null) {
         // Without a session there is nothing to refresh, so the request is sent once as-is.
         if (!HasSession) {
            using var plain = requestFactory();
            return await send(plain);
         }

         // Recorded before sending: if after the 401 the value is already different, another request has
         // already refreshed the token first and this one only needs to repeat with the new one.
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

            // Only once, no loop: a second 401 after a new token means the problem is not the token.
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
      /// Makes sure this session holds an access token newer than <paramref name="attemptedToken"/>, by
      /// exchanging the refresh token if needed. Several requests that hit 401 at the same time wait for the
      /// same single refresh.
      /// </summary>
      /// <returns><c>true</c> when after this there is a new access token that can be tried.</returns>
      private async Task<bool> EnsureRefreshedAsync(string? attemptedToken) {
         await _refreshGate.WaitAsync();
         try {
            // Someone already refreshed while we were queued. The refresh token must not be used again - it has
            // been rotated, and using it a second time would kill the session itself.
            if (!string.Equals(AccessToken, attemptedToken, StringComparison.Ordinal)) return HasSession;
            if (string.IsNullOrEmpty(RefreshToken)) return false;

            try {
               SetSession(await SendRefreshAsync(RefreshToken));
               return true;
            }
            catch (Exception) {
               // Why the refresh failed changes nothing here: what is held is no longer valid, and the caller above
               // ends the session.
               return false;
            }
         }
         finally {
            _refreshGate.Release();
         }
      }

      /// <summary>
      /// Exchanges the refresh token for a new pair of tokens. Sent through the raw path - it does not go
      /// through <see cref="SendAsync{T}(Func{HttpRequestMessage})"/> and does not attach the <c>Authorization</c> header - because a
      /// 401 inside it would trigger another refresh, and that recursion has no clear stopping point.
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
      /// Builds one request together with its identity headers. The headers are set per request, not once on
      /// <c>HttpClient.DefaultRequestHeaders</c>, because all of them change while the application runs: the
      /// identity follows the active user, the debug token must be switchable off without a restart, and the
      /// access token changes every time it is rotated - if set once, a request built before a rotation would
      /// still carry the old token.
      /// </summary>
      /// <param name="method">The HTTP method of the request.</param>
      /// <param name="url">The relative URL of the request.</param>
      /// <param name="content">The request body, or <c>null</c> when there is none.</param>
      /// <param name="withAuthorization">
      /// <c>false</c> only for the token refresh path, which is sent precisely when the access token is
      /// already dead and so need not carry it.
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
      /// Runs the handshake with the server again and then updates <see cref="ServerPublicKey"/>. Called when
      /// the API connection is changed or when the server key is suspected to have been rotated, so the local
      /// key does not go stale.
      /// </summary>
      public async Task ResetServerPublicKeyAsync() {
         SetServerPublicKey(await HandshakeAsync());
      }

      internal void SetServerPublicKey(byte[] key) {
         ServerPublicKey = key;
      }

      /// <summary>
      /// Verifies that the host at <see cref="Connection"/> really holds the private key of the public key it
      /// returns: the client sends a random nonce, the server signs it, and the signature is verified here.
      /// The public key is only accepted when verification passes.
      /// </summary>
      /// <returns>The server public key (raw DER PKCS#1 bytes) that is proven to match its private key.</returns>
      /// <exception cref="InvalidOperationException">
      /// Thrown when the server's signature does not match the nonce that was sent - the host is not an EM
      /// server, or its response was altered in transit.
      /// </exception>
      /// <remarks>
      /// The result is deliberately only returned, not stored straight into <see cref="ServerPublicKey"/>; use
      /// <see cref="ResetServerPublicKeyAsync"/> when the key is really meant to be used from then on.
      /// <para>
      /// This handshake proves ownership of the key, not the identity of the host. What guarantees the host
      /// is not forged is TLS - in production the connection must be HTTPS with a validated certificate. If
      /// TLS is bypassed (e.g. <c>ApiConnection.IgnoreSslErrors</c> is on), a party in the middle can insert
      /// its own public key, sign the nonce with its own private key, and still pass verification.
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
         // BaseAddress must end with '/', otherwise the last segment of the host is cut off when combined with
         // a relative URI such as "api/core/Handshake".
         // disposeHandler: false - the handler is released by Dispose itself, so it is still released even if
         // the HttpClient was never created at all.
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

      /// <inheritdoc />
      public Task<DateTimeOffset> GetServerTimeStampOffsetAsync()
         => GetAsync<DateTimeOffset>(_ctlName, "GetTimeStamp");

      /// <inheritdoc />
      public async Task<DateTime> GetServerTimeStampAsync() {
         var result = await GetServerTimeStampOffsetAsync();
         return result.LocalDateTime;
      }
      /// <inheritdoc />
      public Task<Ulid> GetUlidAsync() => GetAsync<Ulid>(_ctlName, "GetUlid");
      /// <inheritdoc />
      public Task<Ulid[]> GetUlidManyAsync(int count) => GetAsync<Ulid[]>(_ctlName, "GetUlidMany", count);

      #region Base Tools

      /// <summary>
      /// Releases this instance's <see cref="HttpClient"/> together with its handler and any TCP connections
      /// still open. Call it when the connection profile is changed or when the client is no longer used;
      /// after this the instance must not be used again. A handler supplied through
      /// <see cref="Create(ApiConnection, HttpClientHandler)"/> is disposed too, so do not share it with
      /// another <see cref="ApiClient"/> instance.
      /// </summary>
      public void Dispose() {
         Dispose(true);
         GC.SuppressFinalize(this);
      }

      /// <summary>Releases the resources of this client.</summary>
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
      /// Builds the relative URL of a GET action: <c>api/{controller}/{action}</c> plus the arguments as named
      /// query string entries <c>par1</c>, <c>par2</c>, and so on in argument order. The server binds the
      /// action's parameters by this ordinal, not by parameter name.
      /// </summary>
      public string BuildGetUrl(string controller, string action, object?[] args) {
         var url = $"api/{controller}/{action}";
         if (args.Length == 0) {
            return url;
         }

         var query = new StringBuilder();
         for (var index = 0; index < args.Length; index++) {
            // A null argument is deliberately not sent at all: the server treats a missing parN as null/the
            // parameter's default value. Because the naming is ordinal, skipping one parN does not shift the
            // position of the arguments after it.
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
      /// Converts one argument into query string text. Always uses <see cref="CultureInfo.InvariantCulture"/>
      /// because the server reads the value back through <c>ConvertFromInvariantString</c> - if the client
      /// used a local culture (e.g. a comma as the decimal separator) the value would be misread on the
      /// server.
      /// </summary>
      private static string? FormatArgument(object? value) => value switch {
         null => null,
         string text => text,
         // The "O" (round-trip) format keeps fractional seconds and the time zone offset, which would be lost
         // if the TypeConverter's default format were used.
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
