using System.Diagnostics;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>The kind of problem that Verify found in one file.</summary>
   public enum ReleaseVerifyIssueKind
   {
      /// <summary>Listed in <c>release.json</c>, but not present at the target.</summary>
      Missing = 0,

      /// <summary>Its size is different from what is listed.</summary>
      SizeMismatch = 1,

      /// <summary>Its size is the same, but its SHA-256 is different.</summary>
      HashMismatch = 2,

      /// <summary>Present in the target's <c>binaries/</c>, but not listed in <c>release.json</c>.</summary>
      Extra = 3
   }

   /// <summary>One problem found by Verify.</summary>
   /// <param name="Path">Path relative to <c>binaries/</c>.</param>
   /// <param name="Kind">The kind of the problem.</param>
   /// <param name="Detail">A short note, e.g. the expected size and the size found.</param>
   public sealed record ReleaseVerifyIssue(string Path, ReleaseVerifyIssueKind Kind, string Detail);

   /// <summary>The result of Verify on one release folder.</summary>
   public sealed class ReleaseVerifyResult
   {
      /// <summary><c>true</c> when <c>release.json</c> exists at the target.</summary>
      public bool ManifestFound { get; init; }

      /// <summary><c>true</c> when <c>release.json.sig</c> exists at the target.</summary>
      public bool SignatureFound { get; init; }

      /// <summary>
      /// The result of the signature check; <c>null</c> when it was not checked (there is no public key to
      /// check it with, or one of the files is missing).
      /// </summary>
      public ReleaseSignatureStatus? SignatureStatus { get; init; }

      /// <summary>Why <c>release.json</c> or the <c>.sig</c> is not valid, or <c>null</c>.</summary>
      public string? FormatError { get; init; }

      /// <summary>The issue time according to <c>release.json</c>, when it can be read.</summary>
      public DateTime? PublishedAtUtc { get; init; }

      /// <summary>The number of files listed in <c>release.json</c>.</summary>
      public int FileCount { get; init; }

      /// <summary>The total size of the listed files.</summary>
      public long TotalSize { get; init; }

      /// <summary>Problems per file, ordered by path.</summary>
      public IReadOnlyList<ReleaseVerifyIssue> Issues { get; init; } = [];

      /// <summary>
      /// <c>true</c> only when everything passes: both files exist, the signature is valid, the format is
      /// valid, and every file matches with no extra files. A signature that was not checked counts as failed.
      /// </summary>
      public bool IsSuccess =>
         ManifestFound && SignatureFound && SignatureStatus == ReleaseSignatureStatus.Valid && FormatError is null &&
         Issues.Count == 0;

      /// <summary>A one-line summary for display.</summary>
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
   /// Checks one release folder at the target the way the launcher will check it: the <c>release.json</c>
   /// signature, then the size and SHA-256 of every listed file, and the files that are not listed.
   /// </summary>
   public static class ReleaseVerifier
   {
      /// <summary>
      /// Runs Verify. Files on the CDN are downloaded and hashed while streaming, without being stored.
      /// </summary>
      /// <param name="target">The target being checked.</param>
      /// <param name="trustedPublicKeys">
      /// DER SubjectPublicKeyInfo of the public keys that are trusted. Empty means the signature cannot be
      /// checked, and the result fails.
      /// </param>
      /// <param name="progress">Receives progress; may be <c>null</c>.</param>
      /// <param name="token">Stops Verify midway.</param>
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
