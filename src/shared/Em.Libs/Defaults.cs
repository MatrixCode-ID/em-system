using System.Text.Json;

namespace Em
{
   /// <summary>Constants and defaults shared by the API and its clients.</summary>
   public static class Defaults
   {
      /// <summary>
      /// Standard <see cref="JsonSerializerOptions"/> for API communication between backend and UI - used
      /// both when the backend builds a response (<c>Results.Json</c>) and when the UI reads it, so JSON
      /// property naming is consistent on both sides. Deliberately case-sensitive (no naming policy) and
      /// exactly following the C# property names (e.g. <c>ValidResult</c>, not <c>validResult</c>),
      /// because <c>Em.Api.Core</c>/<c>Em.Libs</c> are public NuGet packages whose API can be used by
      /// systems outside this solution - a predictable JSON contract matters more than the camelCase
      /// convention.
      /// </summary>
      public static JsonSerializerOptions ResponseJsonOptions { get; } = new() {
         PropertyNamingPolicy = null,
         PropertyNameCaseInsensitive = false
      };

      /// <summary>Default timeout of an API call, in seconds.</summary>
      public const int StandardTimeoutSeconds = 30;

      /// <summary>
      /// Default HTTP handler for calling the API: plain, with TLS certificate validation left on. Use
      /// <see cref="CreateHttpClientHandler(bool)"/> when validation really must be turned off - stating
      /// that intent is the only way to get it.
      /// </summary>
      public static HttpClientHandler DefaultHttpClientHandler => new();

      /// <summary>
      /// Creates an HTTP handler whose TLS certificate validation can be turned off. Separate from
      /// <see cref="DefaultHttpClientHandler"/> so turning it off is always a choice written by the caller,
      /// not a default nobody chose.
      /// </summary>
      /// <param name="ignoreSslErrors">
      /// <c>true</c> accepts any certificate. This disables TLS as the guarantee of the host's
      /// authenticity, so the RSA handshake on top of it can no longer detect a man in the middle either -
      /// only appropriate for development servers with self-signed certificates.
      /// </param>
      public static HttpClientHandler CreateHttpClientHandler(bool ignoreSslErrors) {
         var handler = new HttpClientHandler();
         if (ignoreSslErrors) {
            handler.ServerCertificateCustomValidationCallback =
               HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
         }

         return handler;
      }

      /// <summary>
      /// Name of the module holding every credential action - sign in, refresh token, sign out and
      /// password matters. Written here so the <c>[Module]</c> attributes on both sides and the refresh
      /// path inside the client use exactly the same word; if each typed its own, a single typo would only
      /// show when the token can never be refreshed.
      /// </summary>
      public const string CredentialModuleName = "core.credential";

      /// <summary>
      /// Name of the engine's built-in administrative tools module, e.g. the CDN manager. Claims of these
      /// tools are written with this name as prefix, and claim matching on the server compares it with the
      /// module where the action is registered - so both sides must use exactly the same word.
      /// </summary>
      public const string AdministrativeToolsModuleName = "Administrative Tools";

      /// <summary>
      /// Name of the module holding every approval action - viewing requests, deciding, withdrawing and
      /// commenting. One module for every document type, because the actions are the same for all of
      /// them; the rights that apply come from the claims of the module that owns the document, checked
      /// inside the action once the document type is known.
      /// </summary>
      public const string ApprovalModuleName = "core.approval";

      /// <summary>User ID of the debugger account.</summary>
      public const string DebuggerUserId = "99999999999999999999999999";

      /// <summary>User ID of the built-in administrator account.</summary>
      public const string AdminUserId = "00000000000000000000000000";

      /// <summary>
      /// Account name of the debugger - the account developers use when running the application without
      /// going through the sign-in screen. Paired with <see cref="AdminUserAccount"/> and here for the same
      /// reason: this account has no user row, so its name cannot be read from there. The name only means
      /// something when the request carries a debug token that passes verification.
      /// </summary>
      public const string DebuggerUserAccount = "debugger";

      /// <summary>
      /// Name of the HTTP header carrying the debug token. This token proves the caller really is an
      /// authorized developer, so they may enter without a password or access token. Written here so the
      /// server that checks it and the client that sends it use exactly the same word.
      /// </summary>
      public const string DebugTokenHeader = "X-Em-Debug-Token";

      /// <summary>
      /// Name of the HTTP header in which the client names the account active on its screen. It holds a
      /// JSON object carrying both the ID and the account name; its shape is written once in
      /// <see cref="Shared.UserHeaderProtocol"/> so the client that builds it and the server that reads it
      /// use exactly the same words.
      /// <para>
      /// This header is never a source of identity by itself - it is only trusted when
      /// <see cref="DebugTokenHeader"/> is sent too and passes verification; otherwise identity still comes
      /// from the access token.
      /// </para>
      /// </summary>
      public const string UserHeader = "X-Em-User";

      /// <summary>
      /// Name of the HTTP header carrying the payload of a streaming action. On such an action the request
      /// body is already used for the raw stream content, so the other parameters - at most one object -
      /// ride in this header. Its content is built and read through
      /// <see cref="Shared.StreamPayloadProtocol"/>, so the client that sends it and the server that reads
      /// it use exactly the same word and shape.
      /// </summary>
      public const string StreamPayloadHeader = "Em-X-StreamPayload";

      /// <summary>
      /// Account name of the built-in administrator, typed on the sign-in screen. This account has no row
      /// in the user table, so its name cannot be read from there - it is written here so the server that
      /// recognizes it at sign-in and the client that displays it use exactly the same word. The name is
      /// also reserved: no regular user may take it.
      /// </summary>
      public const string AdminUserAccount = "admin";

      /// <summary>
      /// Display name of the built-in administrator account - what appears on screen, not what is typed at
      /// sign-in. Paired with <see cref="AdminUserAccount"/> and here for the same reason: no user row
      /// stores it.
      /// </summary>
      public const string AdminUserFullName = "System Administrator";

      /// <summary>
      /// Marker of the password credential type. The value is stored in the credential row, so both sides
      /// must use exactly the same word - which is why it lives here rather than on one side: the server
      /// checks passwords, while the client creates the empty row when a new user is created.
      /// </summary>
      public const string PasswordCredentialType = "PASSWORD";

      /// <summary>
      /// "No end" value for validity columns that do not accept <c>null</c>. One date far ahead, chosen as
      /// the latest date accepted by the SQL Server <c>datetime</c> type, so the "still valid" check stays a
      /// single ordinary date comparison - with no special branch everywhere it is read.
      /// </summary>
      public static readonly DateTime NoExpiry = new(9999, 12, 31);
   }
}