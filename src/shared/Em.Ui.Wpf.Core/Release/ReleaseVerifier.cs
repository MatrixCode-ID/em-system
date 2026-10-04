using System.Diagnostics;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>Jenis masalah yang ditemukan Verify pada satu file.</summary>
   public enum ReleaseVerifyIssueKind
   {
      /// <summary>Tercantum di <c>release.json</c>, tetapi tidak ada di tujuan.</summary>
      Missing = 0,

      /// <summary>Ukurannya berbeda dari yang tercantum.</summary>
      SizeMismatch = 1,

      /// <summary>Ukurannya sama, tetapi SHA-256-nya berbeda.</summary>
      HashMismatch = 2,

      /// <summary>Ada di <c>binaries/</c> tujuan, tetapi tidak tercantum di <c>release.json</c>.</summary>
      Extra = 3
   }

   /// <summary>Satu masalah yang ditemukan Verify.</summary>
   /// <param name="Path">Path relatif terhadap <c>binaries/</c>.</param>
   /// <param name="Kind">Jenis masalahnya.</param>
   /// <param name="Detail">Keterangan singkat, mis. ukuran yang diharapkan dan yang ditemukan.</param>
   public sealed record ReleaseVerifyIssue(string Path, ReleaseVerifyIssueKind Kind, string Detail);

   /// <summary>Hasil Verify atas satu folder rilis.</summary>
   public sealed class ReleaseVerifyResult
   {
      /// <summary><c>true</c> kalau <c>release.json</c> ada di tujuan.</summary>
      public bool ManifestFound { get; init; }

      /// <summary><c>true</c> kalau <c>release.json.sig</c> ada di tujuan.</summary>
      public bool SignatureFound { get; init; }

      /// <summary>
      /// Hasil pemeriksaan tanda tangan; <c>null</c> kalau tidak diperiksa (tidak ada public key untuk
      /// memeriksanya, atau salah satu file tidak ada).
      /// </summary>
      public ReleaseSignatureStatus? SignatureStatus { get; init; }

      /// <summary>Kenapa <c>release.json</c> atau <c>.sig</c> tidak sah, atau <c>null</c>.</summary>
      public string? FormatError { get; init; }

      /// <summary>Waktu terbit menurut <c>release.json</c>, kalau terbaca.</summary>
      public DateTime? PublishedAtUtc { get; init; }

      /// <summary>Jumlah file yang tercantum di <c>release.json</c>.</summary>
      public int FileCount { get; init; }

      /// <summary>Total ukuran file yang tercantum.</summary>
      public long TotalSize { get; init; }

      /// <summary>Masalah per file, urut path.</summary>
      public IReadOnlyList<ReleaseVerifyIssue> Issues { get; init; } = [];

      /// <summary>
      /// <c>true</c> hanya kalau semuanya lolos: kedua file ada, tanda tangan sah, format sah, dan setiap
      /// file cocok tanpa file tambahan. Tanda tangan yang tidak diperiksa dihitung gagal.
      /// </summary>
      public bool IsSuccess =>
         ManifestFound && SignatureFound && SignatureStatus == ReleaseSignatureStatus.Valid && FormatError is null &&
         Issues.Count == 0;

      /// <summary>Ringkasan satu baris untuk ditampilkan.</summary>
      public string Summary {
         get {
            if (!ManifestFound) return "No release.json at the target.";
            if (IsSuccess) return $"Release verified: {FileCount:N0} files, signature valid.";

            var parts = new List<string>();
            if (!SignatureFound) parts.Add("release.json.sig is missing");
            else if (SignatureStatus is null) parts.Add("signature not checked (no signing key selected)");
            else if (SignatureStatus == ReleaseSignatureStatus.UnknownKey) parts.Add("signed by an unknown key");
            else if (SignatureStatus == ReleaseSignatureStatus.Invalid) parts.Add("signature is INVALID");
            if (FormatError is not null) parts.Add(FormatError);
            if (Issues.Count > 0) parts.Add($"{Issues.Count:N0} file problem(s)");
            return "Verify failed: " + string.Join("; ", parts) + ".";
         }
      }
   }

   /// <summary>
   /// Memeriksa satu folder rilis di tujuan seperti launcher akan memeriksanya: tanda tangan
   /// <c>release.json</c>, lalu ukuran dan SHA-256 setiap file yang tercantum, dan file yang tidak
   /// tercantum.
   /// </summary>
   public static class ReleaseVerifier
   {
      /// <summary>
      /// Menjalankan Verify. File di CDN diunduh dan di-hash sambil mengalir, tanpa disimpan.
      /// </summary>
      /// <param name="target">Tujuan yang diperiksa.</param>
      /// <param name="trustedPublicKeys">
      /// DER SubjectPublicKeyInfo public key yang dipercaya. Kosong berarti tanda tangan tidak bisa
      /// diperiksa, dan hasilnya gagal.
      /// </param>
      /// <param name="progress">Menerima kemajuan; boleh <c>null</c>.</param>
      /// <param name="token">Menghentikan Verify di tengah jalan.</param>
      public static async Task<ReleaseVerifyResult> VerifyAsync(ReleaseTarget target,
         IReadOnlyList<ReadOnlyMemory<byte>> trustedPublicKeys, IProgress<ReleaseProgress>? progress,
         CancellationToken token) {
         progress?.Report(new ReleaseProgress("Reading release.json", 0, 0, null, 0, 0));
         var signatureBytes = await target.ReadFileAsync(ReleaseLayout.SignatureFileName, token).ConfigureAwait(false);
         var manifestBytes = await target.ReadFileAsync(ReleaseLayout.ManifestFileName, token).ConfigureAwait(false);
         if (manifestBytes is null) return new ReleaseVerifyResult { SignatureFound = signatureBytes is not null };

         ReleaseSignatureStatus? signatureStatus = null;
         string? formatError = null;
         if (signatureBytes is not null && trustedPublicKeys.Count > 0) {
            try {
               var signature = ReleaseManifestSerializer.DeserializeSignature(signatureBytes);
               signatureStatus = ReleaseSignature.Check(manifestBytes, signature, trustedPublicKeys);
            }
            catch (ReleaseFormatException x) {
               signatureStatus = ReleaseSignatureStatus.Invalid;
               formatError = x.Message;
            }
         }

         // Unlike a launcher, which stops at a bad signature, Verify goes on to the files: it is a
         // diagnosis, and knowing which files differ is what the publisher needs next.
         ReleaseManifest manifest;
         try {
            manifest = ReleaseManifestSerializer.Deserialize(manifestBytes);
         }
         catch (ReleaseFormatException x) {
            return new ReleaseVerifyResult {
               ManifestFound = true,
               SignatureFound = signatureBytes is not null,
               SignatureStatus = signatureStatus,
               FormatError = x.Message
            };
         }

         var issues = new List<ReleaseVerifyIssue>();
         var total = manifest.Files.Sum(r => r.Size);
         var done = 0L;
         var sinceReport = Stopwatch.StartNew();
         for (var index = 0; index < manifest.Files.Count; index++) {
            var file = manifest.Files[index];
            var before = done;
            var position = index + 1;
            progress?.Report(new ReleaseProgress("Verifying", position, manifest.Files.Count, file.Path, before, total));
            var fileProgress = new InlineProgress<long>(read => {
               if (sinceReport.ElapsedMilliseconds < 100) return;
               sinceReport.Restart();
               progress?.Report(new ReleaseProgress("Verifying", position, manifest.Files.Count, file.Path,
                  before + read, total));
            });

            var found = await target.HashFileAsync(ReleaseTarget.BinaryPath(file.Path), fileProgress, token)
               .ConfigureAwait(false);
            done += file.Size;
            if (found is not { } actual) {
               issues.Add(new ReleaseVerifyIssue(file.Path, ReleaseVerifyIssueKind.Missing, "not found at the target"));
            }
            else if (actual.Size != file.Size) {
               issues.Add(new ReleaseVerifyIssue(file.Path, ReleaseVerifyIssueKind.SizeMismatch,
                  $"expected {file.Size:N0} bytes, found {actual.Size:N0}"));
            }
            else if (!string.Equals(actual.Sha256, file.Sha256, StringComparison.Ordinal)) {
               issues.Add(new ReleaseVerifyIssue(file.Path, ReleaseVerifyIssueKind.HashMismatch,
                  $"expected sha256 {file.Sha256}, found {actual.Sha256}"));
            }
         }

         progress?.Report(new ReleaseProgress("Listing binaries", 0, 0, null, total, total));
         var binaries = await target.ListBinariesAsync(token).ConfigureAwait(false);
         issues.AddRange(binaries
            .Where(r => !r.IsFolder && manifest.Find(r.Path) is null)
            .Select(r => new ReleaseVerifyIssue(r.Path, ReleaseVerifyIssueKind.Extra, "not listed in release.json")));

         return new ReleaseVerifyResult {
            ManifestFound = true,
            SignatureFound = signatureBytes is not null,
            SignatureStatus = signatureStatus,
            FormatError = formatError,
            PublishedAtUtc = manifest.PublishedAtUtc,
            FileCount = manifest.Files.Count,
            TotalSize = total,
            Issues = issues.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToArray()
         };
      }
   }
}
