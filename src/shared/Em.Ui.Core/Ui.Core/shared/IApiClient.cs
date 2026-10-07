using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Contract of the API client. It inherits <see cref="IDisposable"/> because its implementation holds
   /// network connections that must be released explicitly - a consumer that only keeps this interface is
   /// still required (and able) to dispose it without needing to know the concrete type.
   /// </summary>
   public interface IApiClient : IDisposable
   {
      /// <summary>Calls a GET action and reads its result.</summary>
      Task<T> GetAsync<T>(string controller, string action, params object[] args);

      /// <inheritdoc cref="ApiClient.GetStreamAsync(string, string, object[])" />
      Task<Stream> GetStreamAsync(string controller, string action, params object[] args);
      /// <summary>Calls a POST action that returns no value.</summary>
      Task PostAsync(string controller, string action, params object[] args);
      /// <summary>Calls a POST action and reads its result.</summary>
      Task<T> PostAsync<T>(string controller, string action, params object[] args);

      /// <inheritdoc cref="ApiClient.PostStreamAsync(string, string, Stream, object?)" />
      Task PostStreamAsync(string controller, string action, Stream content, object? payload = null);

      /// <inheritdoc cref="ApiClient.PostStreamAsync{T}(string, string, Stream, object?)" />
      Task<T> PostStreamAsync<T>(string controller, string action, Stream content, object? payload = null);
      /// <summary>Builds the relative URL of a GET action together with its arguments.</summary>
      string BuildGetUrl(string controller, string action, object?[] args);
      /// <summary>Runs the key handshake with the server and returns its verified public key.</summary>
      Task<byte[]> HandshakeAsync();
      /// <summary>Runs the handshake again and stores the verified server public key.</summary>
      Task ResetServerPublicKeyAsync();

      #region Session

      // The session lives in the transport contract, not only in the concrete class: one session belongs to
      // one connection, and a caller that only holds this interface must still be able to open and close it.

      /// <inheritdoc cref="ApiClient.AccessToken" />
      string? AccessToken { get; }

      /// <inheritdoc cref="ApiClient.RefreshToken" />
      string? RefreshToken { get; }

      /// <inheritdoc cref="ApiClient.AccessTokenExpiresAtUtc" />
      DateTimeOffset AccessTokenExpiresAtUtc { get; }

      /// <inheritdoc cref="ApiClient.SessionUserId" />
      string? SessionUserId { get; }

      /// <inheritdoc cref="ApiClient.HasSession" />
      bool HasSession { get; }

      /// <inheritdoc cref="ApiClient.ActiveUserId" />
      string? ActiveUserId { get; set; }

      /// <inheritdoc cref="ApiClient.ActiveUserAccount" />
      string? ActiveUserAccount { get; set; }

      /// <inheritdoc cref="ApiClient.SetSession(TokenResult)" />
      void SetSession(TokenResult token);

      /// <inheritdoc cref="ApiClient.ClearSession" />
      void ClearSession();

      /// <inheritdoc cref="ApiClient.SessionChanged" />
      event EventHandler? SessionChanged;

      /// <inheritdoc cref="ApiClient.SessionEnded" />
      event EventHandler? SessionEnded;

      #endregion
   }
}
