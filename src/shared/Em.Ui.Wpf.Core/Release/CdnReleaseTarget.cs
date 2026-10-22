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
   /// A release target on the built-in CDN of the server that is currently connected. It reads through the
   /// public <c>/cdn/...</c> address without login (just as the launcher will later read it), and writes
   /// through <see cref="ICdnReleaseServices"/>, which requires the Release Manager claim.
   /// </summary>
   public sealed class CdnReleaseTarget : ReleaseTarget
   {
      private readonly ICdnReleaseServices _service;
      private readonly string _appId;
      private readonly string? _publicAddress;
      private readonly string _host;
      private readonly HttpClient _client;
      private readonly string _root;

      // Folders known to exist, so a Sync asks the server to create each one only once.
      private readonly HashSet<string> _knownFolders = new(StringComparer.OrdinalIgnoreCase);

      /// <summary>
      /// Creates a CDN target for release folder <paramref name="releaseFolder"/> on server
      /// <paramref name="connection"/>.
      /// </summary>
      /// <param name="service">The CDN service of the connection that is currently active.</param>
      /// <param name="connection">The active connection; its address and certificate choice are used for <c>/cdn</c>.</param>
      /// <param name="releaseFolder">The path of the release folder inside the CDN, e.g. <c>wpf-release</c>.</param>
      /// <param name="appId">Owning product identity.</param>
      /// <param name="publicAddress">Optional client-facing release URL.</param>
      public CdnReleaseTarget(ICdnReleaseServices service, ApiConnection connection, string releaseFolder, string appId, string? publicAddress = null) {
         _appId = appId;
         _publicAddress = string.IsNullOrWhiteSpace(publicAddress) ? null : publicAddress.TrimEnd('/') + "/";
         _service = service;
         _host = connection.Host.TrimEnd('/');
         _client = PublicClients.Get(connection.IgnoreSslErrors);
         _root = NormalizeReleaseFolder(releaseFolder);
      }

      /// <inheritdoc />
      public override string Description => _publicAddress ?? $"{_host}/cdn/{_root}/";

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
            tree = await _service.GetMeta_CdnReleaseTree(folder, _appId).ConfigureAwait(false);
         }
         catch (ActionException x) when (x.StatusCode == 404) {
            return [];
         }

         // The server answers with its own spelling of the folder; only the length of the prefix matters.
         var prefixLength = folder.Length + 1;
         return tree.Select(r => new ReleaseTargetEntry(r.Path[prefixLength..], r.IsFolder, r.Size)).ToArray();
      }

      /// <summary>
      /// Whether the release folder holds a <c>release.json</c>, asked through the server rather than the
      /// public address - so a public address that points elsewhere can be told apart from a new release.
      /// </summary>
      public async Task<bool> HasManifestOnServerAsync(CancellationToken token) {
         token.ThrowIfCancellationRequested();
         CdnEntry[] tree;
         try {
            tree = await _service.GetMeta_CdnReleaseTree(_root, _appId).ConfigureAwait(false);
         }
         catch (ActionException x) when (x.StatusCode == 404) {
            return false;
         }

         var prefixLength = _root.Length + 1;
         return tree.Any(r => !r.IsFolder && r.Path.Length > prefixLength &&
            string.Equals(r.Path[prefixLength..], ReleaseLayout.ManifestFileName, StringComparison.OrdinalIgnoreCase));
      }

      /// <inheritdoc />
      public override async Task EnsureFolderAsync(string folder, CancellationToken token) {
         string? parent = _root;
         foreach (var segment in folder.Split('/', StringSplitOptions.RemoveEmptyEntries)) {
            token.ThrowIfCancellationRequested();
            var path = parent is null ? segment : $"{parent}/{segment}";
            if (_knownFolders.Add(path)) {
               try {
                  await _service.PostGetMeta_CdnReleaseCreateFolder(parent, segment, _appId).ConfigureAwait(false);
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
         // The server writes to a private temporary file and moves it into place, so a cut-off
         // upload leaves the old file as it was.
         var full = Full(path);
         await using var body = new ProgressReadStream(content, progress, token);
         try {
            await _service.PostGetMeta_CdnReleaseUpload(new CdnReleaseUploadRequest { AppId = _appId, Upload = new CdnUploadRequest {
               Path = ParentOf(full),
               FileName = full[(full.LastIndexOf('/') + 1)..],
               Overwrite = true
            } }, body).ConfigureAwait(false);
         }
         catch (Exception) when (token.IsCancellationRequested) {
            throw new OperationCanceledException(token);
         }
      }

      /// <inheritdoc />
      public override async Task DeleteAsync(string path, bool isFolder, CancellationToken token) {
         token.ThrowIfCancellationRequested();
         try {
            await _service.PostMeta_CdnReleaseDelete(Full(path), _appId).ConfigureAwait(false);
         }
         catch (ActionException x) when (x.StatusCode == 404) {
            // Already gone.
         }
      }

      /// <inheritdoc />
      public override async Task<long?> GetMaxFileSizeAsync(CancellationToken token) {
         await _service.PostGetMeta_CdnReleaseManage(_root, _appId).ConfigureAwait(false);
         await HideFromListingAsync(_root).ConfigureAwait(false);
         return null;
      }

      /// <summary>
      /// Turns the public listing off for the release folder, <c>binaries/</c> and every folder inside it, so
      /// the application components can only be fetched by direct link. Folder settings are not inherited,
      /// hence one settings file per folder; it is rewritten on every Sync, replacing manual changes.
      /// </summary>
      public override async Task CompleteSyncAsync(CancellationToken token) {
         var binaries = Full(ReleaseLayout.BinariesFolder);
         var folders = new List<string> { _root, binaries };
         folders.AddRange((await ListBinariesAsync(token).ConfigureAwait(false))
            .Where(r => r.IsFolder).Select(r => $"{binaries}/{r.Path}"));
         foreach (var folder in folders) {
            token.ThrowIfCancellationRequested();
            await HideFromListingAsync(folder).ConfigureAwait(false);
         }
      }

      private Task HideFromListingAsync(string folder) =>
         _service.PostMeta_CdnReleaseFolderSettings(new CdnReleaseFolderSettingsRequest {
            AppId = _appId, Path = folder, Settings = new CdnFolderSettings { PublicListing = false }
         });

      /// <inheritdoc />
      public override Task PreflightAsync(IReadOnlyList<CdnSyncFile> files, CancellationToken token) =>
         _service.PostMeta_CdnReleasePreflight(new CdnSyncPlan { Path = _root, AppId = _appId, Files = files.ToArray() });

      private string Full(string path) => $"{_root}/{path}";

      // Null for 404; any other failure is thrown. no-cache keeps a proxy from answering with an old
      // release.json, which would defeat the check made right before a Sync.
      private async Task<HttpResponseMessage?> GetAsync(string path, CancellationToken token) {
         var url = Description + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
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
