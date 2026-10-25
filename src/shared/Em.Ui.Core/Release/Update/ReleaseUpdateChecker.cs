using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using Em.Ui.Core.Release.Format;

namespace Em.Ui.Core.Release.Update;

public sealed record ReleaseUpdateSource(Uri Address, byte[] PublicKey) {
   public static ReleaseUpdateSource? FromAssembly(Assembly assembly) {
      var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
      var address = metadata.FirstOrDefault(a => a.Key == "EmReleaseAddress")?.Value;
      var key = metadata.FirstOrDefault(a => a.Key == "EmReleasePublicKey")?.Value;
      if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(key)) return null;
      if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !address.EndsWith('/'))
         throw new FormatException("Invalid embedded release address.");
      return new(uri, Convert.FromBase64String(key));
   }
}

public enum ReleaseUpdateStatus { UpToDate, Optional, Mandatory, NotConfigured, Failed }
public sealed record ReleaseUpdateResult(ReleaseUpdateStatus Status, ReleaseManifest? Manifest = null, string? Error = null);

/// <summary>Verifies downloaded bytes before interpreting any Android update policy.</summary>
public sealed class ReleaseUpdateChecker(HttpClient client) {
   public async Task<ReleaseUpdateResult> CheckAsync(ReleaseUpdateSource? source, string applicationId, long installedVersionCode, CancellationToken token = default) {
      if (source is null) return new(ReleaseUpdateStatus.NotConfigured);
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
      timeout.CancelAfter(TimeSpan.FromSeconds(5));
      try {
         var signature = await GetBytesAsync(new Uri(source.Address, "release.json.sig"), 64 * 1024, timeout.Token).ConfigureAwait(false);
         var bytes = await GetBytesAsync(new Uri(source.Address, "release.json"), 2 * 1024 * 1024, timeout.Token).ConfigureAwait(false);
         if (!ReleaseSignature.Verify(bytes, ReleaseManifestSerializer.DeserializeSignature(signature), [source.PublicKey]))
            return new(ReleaseUpdateStatus.Failed, Error: "The release signature is invalid or belongs to another key.");
         var manifest = ReleaseManifestSerializer.Deserialize(bytes);
         if (manifest.Android is not { } android) return new(ReleaseUpdateStatus.Failed, Error: "The release has no Android metadata.");
         if (android.ApplicationId != applicationId) return new(ReleaseUpdateStatus.Failed, Error: "The Android applicationId differs from the installed package.");
         var status = android.VersionCode <= installedVersionCode ? ReleaseUpdateStatus.UpToDate
            : android.MinVersionCode is { } minimum && installedVersionCode < minimum ? ReleaseUpdateStatus.Mandatory : ReleaseUpdateStatus.Optional;
         return new(status, manifest);
      }
      catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or FormatException or ReleaseFormatException or CryptographicException or InvalidOperationException) {
         return new(ReleaseUpdateStatus.Failed, Error: ex is OperationCanceledException ? "The update check timed out or was cancelled." : ex.Message);
      }
   }

   private async Task<byte[]> GetBytesAsync(Uri uri, int maximum, CancellationToken token) {
      using var request = new HttpRequestMessage(HttpMethod.Get, uri);
      request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
      using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
      response.EnsureSuccessStatusCode();
      await response.Content.LoadIntoBufferAsync(maximum, token).ConfigureAwait(false);
      return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
   }

   public async Task DownloadAsync(ReleaseUpdateSource source, ReleaseManifest manifest, string destination, IProgress<double>? progress, CancellationToken token) {
      var android = manifest.Android ?? throw new InvalidOperationException("Android metadata is required.");
      var file = manifest.Find(android.FileName) ?? throw new InvalidOperationException("APK is not listed.");
      try {
         using var response = await client.GetAsync(new Uri(source.Address, "binaries/" + Uri.EscapeDataString(file.Path)), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
         response.EnsureSuccessStatusCode();
         if (response.Content.Headers.ContentLength is { } length && length != file.Size) throw new IOException("APK size differs from the signed manifest.");
         await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
         await using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true)) {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920]; long total = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0) {
               total += count;
               if (total > file.Size) throw new IOException("APK exceeds its signed size.");
               hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
               progress?.Report(file.Size == 0 ? 0 : (double)total / file.Size);
            }
            if (total != file.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != file.Sha256) throw new IOException("APK hash or size differs from the signed manifest.");
         }
      }
      catch { if (File.Exists(destination)) File.Delete(destination); throw; }
   }
}
