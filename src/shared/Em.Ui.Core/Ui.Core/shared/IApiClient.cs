using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Kontrak client API. Mewarisi <see cref="IDisposable"/> karena implementasinya memegang koneksi jaringan
   /// yang harus dilepas secara eksplisit — konsumen yang cuma menyimpan interface ini tetap wajib (dan bisa)
   /// men-dispose-nya tanpa perlu tahu tipe konkretnya.
   /// </summary>
   public interface IApiClient : IDisposable
   {
      Task<T> GetAsync<T>(string controller, string action, params object[] args);

      /// <inheritdoc cref="ApiClient.GetStreamAsync(string, string, object[])" />
      Task<Stream> GetStreamAsync(string controller, string action, params object[] args);
      Task PostAsync(string controller, string action, params object[] args);
      Task<T> PostAsync<T>(string controller, string action, params object[] args);

      /// <inheritdoc cref="ApiClient.PostStreamAsync(string, string, Stream, object?)" />
      Task PostStreamAsync(string controller, string action, Stream content, object? payload = null);

      /// <inheritdoc cref="ApiClient.PostStreamAsync{T}(string, string, Stream, object?)" />
      Task<T> PostStreamAsync<T>(string controller, string action, Stream content, object? payload = null);
      string BuildGetUrl(string controller, string action, object?[] args);
      Task<byte[]> HandshakeAsync();
      Task ResetServerPublicKeyAsync();

      #region Session

      // Sesi tinggal di kontrak transport, bukan di kelas konkretnya saja: satu sesi milik satu koneksi,
      // dan pemanggil yang cuma memegang interface ini tetap harus bisa membukanya dan menutupnya.

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
