using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// Tujuan rilis di CDN bawaan server yang sedang tersambung. Membaca lewat alamat publik
   /// <c>/cdn/...</c> tanpa login (sama seperti launcher nanti membacanya), dan menulis lewat
   /// <see cref="ICdnServices"/>, yang mensyaratkan claim pengelola CDN.
   /// </summary>
   public sealed class CdnReleaseTarget : ReleaseTarget
   {
      private readonly ICdnServices _service;
      private readonly string _host;
      private readonly HttpClient _client;
      private readonly string _root;

      // Folders known to exist, so a Sync asks the server to create each one only once.
      private readonly HashSet<string> _knownFolders = new(StringComparer.OrdinalIgnoreCase);

      /// <summary>
      /// Membuat tujuan CDN untuk folder rilis <paramref name="releaseFolder"/> di server
      /// <paramref name="connection"/>.
      /// </summary>
      /// <param name="service">Service CDN milik koneksi yang sedang aktif.</param>
      /// <param name="connection">Koneksi aktif; alamat dan pilihan sertifikatnya dipakai untuk <c>/cdn</c>.</param>
      /// <param name="releaseFolder">Path folder rilis di dalam CDN, mis. <c>wpf-release</c>.</param>
      public CdnReleaseTarget(ICdnServices service, ApiConnection connection, string releaseFolder) {
         _service = service;
         _host = connection.Host.TrimEnd('/');
         _client = PublicClients.Get(connection.IgnoreSslErrors);
         _root = NormalizeReleaseFolder(releaseFolder);
      }

      /// <inheritdoc />
      public override string Description => $"{_host}/cdn/{_root}/";

      /// <inheritdoc />
      public override async Task<byte[]?> ReadFileAsync(string path, CancellationToken token) {
         using var response = await GetAsync(path, token).ConfigureAwait(false);
         if (response is null) return null;

         return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
      }

      /// <inheritdoc />
      public override async Task<(long Size, string Sha256)?> HashFileAsync(string path, IProgress<long>? progress,
         CancellationToken token) {
         using var response = await GetAsync(path, token).ConfigureAwait(false);
         if (response is null) return null;

         await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
         return await ReleaseHash.ComputeAsync(stream, progress, token).ConfigureAwait(false);
      }

      /// <inheritdoc />
      public override async Task<IReadOnlyList<ReleaseTargetEntry>> ListBinariesAsync(CancellationToken token) {
         var folder = Full(ReleaseLayout.BinariesFolder);
         CdnEntry[] tree;
         try {
            tree = await _service.GetMeta_CdnTree(folder).ConfigureAwait(false);
         }
         catch (ActionException x) when (x.StatusCode == 404) {
            return [];
         }

         // The server answers with its own spelling of the folder; only the length of the prefix matters.
         var prefixLength = folder.Length + 1;
         return tree.Select(r => new ReleaseTargetEntry(r.Path[prefixLength..], r.IsFolder, r.Size)).ToArray();
      }

      /// <inheritdoc />
      public override async Task EnsureFolderAsync(string folder, CancellationToken token) {
         string? parent = null;
         foreach (var segment in Full(folder).Split('/')) {
            token.ThrowIfCancellationRequested();
            var path = parent is null ? segment : $"{parent}/{segment}";
            if (_knownFolders.Add(path)) {
               try {
                  await _service.PostGetMeta_CdnCreateFolder(parent, segment).ConfigureAwait(false);
               }
               catch (ActionException x) when (x.StatusCode == 409) {
                  // Already there.
               }
               catch {
                  _knownFolders.Remove(path);
                  throw;
               }
            }

            parent = path;
         }
      }

      /// <inheritdoc />
      public override async Task WriteFileAsync(string path, Stream content, IProgress<long>? progress,
         CancellationToken token) {
         // The server writes to a dot-prefixed temporary file and moves it into place, so a cut-off
         // upload leaves the old file as it was.
         var full = Full(path);
         await using var body = new ProgressReadStream(content, progress, token);
         try {
            await _service.PostGetMeta_CdnUpload(new CdnUploadRequest {
               Path = ParentOf(full),
               FileName = full[(full.LastIndexOf('/') + 1)..],
               Overwrite = true
            }, body).ConfigureAwait(false);
         }
         catch (Exception) when (token.IsCancellationRequested) {
            throw new OperationCanceledException(token);
         }
      }

      /// <inheritdoc />
      public override async Task DeleteAsync(string path, bool isFolder, CancellationToken token) {
         token.ThrowIfCancellationRequested();
         try {
            await _service.PostMeta_CdnDelete(Full(path)).ConfigureAwait(false);
         }
         catch (ActionException x) when (x.StatusCode == 404) {
            // Already gone.
         }
      }

      /// <inheritdoc />
      public override async Task<long?> GetMaxFileSizeAsync(CancellationToken token) {
         try {
            return (await _service.GetMeta_CdnFolder(null).ConfigureAwait(false)).MaxFileSize;
         }
         catch (ActionException x) when (x.StatusCode == 404) {
            throw new InvalidOperationException("The CDN is not enabled on this server.", x);
         }
      }

      private string Full(string path) => $"{_root}/{path}";

      // Null for 404; any other failure is thrown. no-cache keeps a proxy from answering with an old
      // release.json, which would defeat the check made right before a Sync.
      private async Task<HttpResponseMessage?> GetAsync(string path, CancellationToken token) {
         var url = $"{_host}/cdn/{string.Join('/', Full(path).Split('/').Select(Uri.EscapeDataString))}";
         var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
         request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

         var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
         if (response.StatusCode == HttpStatusCode.NotFound) {
            response.Dispose();
            return null;
         }

         try {
            response.EnsureSuccessStatusCode();
         }
         catch {
            response.Dispose();
            throw;
         }

         return response;
      }

      // One client per certificate setting for the life of the application, as HttpClient is meant to be
      // used; no timeout of its own, a large file takes as long as it takes.
      private static class PublicClients
      {
         private static readonly Lazy<HttpClient> Validating = new(() => Create(false));
         private static readonly Lazy<HttpClient> Lenient = new(() => Create(true));

         public static HttpClient Get(bool ignoreSslErrors) => ignoreSslErrors ? Lenient.Value : Validating.Value;

         private static HttpClient Create(bool ignoreSslErrors) =>
            new(Defaults.CreateHttpClientHandler(ignoreSslErrors)) { Timeout = Timeout.InfiniteTimeSpan };
      }
   }
}
