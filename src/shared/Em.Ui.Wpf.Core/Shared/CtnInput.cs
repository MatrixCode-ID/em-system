using System.Text.RegularExpressions;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Aturan nama dan bentuk perintah docker untuk layar Container Manager. Server tetap yang berwenang
   /// (ia menjawab 400 untuk nama yang tidak sah); aturan di sini hanya supaya tombol konfirmasi dialog
   /// tidak menyala untuk nama yang jelas salah. Konstantanya sengaja ditulis ulang karena kelas aturan
   /// di server <c>internal</c> dan tidak bisa direferensikan dari client.
   /// </summary>
   public static partial class CtnInput
   {
      /// <summary>Panjang maksimum nama root.</summary>
      public const int MaxRootName = 64;

      /// <summary>Panjang maksimum nama container.</summary>
      public const int MaxImageName = 128;

      /// <summary>Panjang maksimum <c>root/nama</c>.</summary>
      public const int MaxFullName = 255;

      /// <summary>Panjang maksimum nama folder.</summary>
      public const int MaxFolderName = 100;

      /// <summary>Panjang maksimum nama robot.</summary>
      public const int MaxRobotName = 100;

      /// <summary>Panjang maksimum deskripsi.</summary>
      public const int MaxDescription = 500;

      /// <summary>Kedalaman folder maksimum yang diterima server.</summary>
      public const int MaxFolderDepth = 8;

      // Komponen nama OCI: huruf kecil dan angka, dipisah ".", "_", "__", atau "-" (boleh berulang).
      [GeneratedRegex(@"^[a-z0-9]+(?:(?:\.|_|__|-+)[a-z0-9]+)*$")]
      private static partial Regex NamePattern();

      [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]*$")]
      private static partial Regex RobotNamePattern();

      /// <summary>Apakah <paramref name="name"/> nama root atau container yang sah dengan panjang maksimum itu.</summary>
      public static bool IsValidName(string? name, int maxLength) =>
         !string.IsNullOrEmpty(name) && name.Length <= maxLength && NamePattern().IsMatch(name);

      /// <summary>Apakah <paramref name="name"/> nama robot yang sah.</summary>
      public static bool IsValidRobotName(string? name) =>
         !string.IsNullOrEmpty(name) && name.Length <= MaxRobotName && RobotNamePattern().IsMatch(name);

      /// <summary>Apakah <paramref name="name"/> nama folder yang sah (hanya soal panjang; bentuknya bebas).</summary>
      public static bool IsValidFolderName(string? name) =>
         !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= MaxFolderName;

      /// <summary>
      /// Mengambil <c>host[:port]</c> dari alamat server: skema, jalur, dan garis miring penutup dibuang,
      /// karena <c>docker</c> tidak menerima skema. Kosong kalau alamatnya tidak terbaca.
      /// </summary>
      public static string RegistryHost(string? serverAddress) {
         if (string.IsNullOrWhiteSpace(serverAddress)) return "";

         var text = serverAddress.Trim();
         if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)) {
            return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
         }

         var schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
         if (schemeEnd >= 0) text = text[(schemeEnd + 3)..];
         var slash = text.IndexOf('/');
         return (slash >= 0 ? text[..slash] : text).TrimEnd('/');
      }

      /// <summary>
      /// Apakah Docker akan menolak <c>docker login</c> ke alamat ini tanpa pengaturan tambahan: Docker
      /// hanya menerima HTTP polos untuk <c>localhost</c> dan alamat loopback.
      /// </summary>
      public static bool IsInsecureRemote(string? serverAddress) {
         if (string.IsNullOrWhiteSpace(serverAddress)) return false;
         if (!Uri.TryCreate(serverAddress.Trim(), UriKind.Absolute, out var uri)) return false;
         if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) return false;

         return !(uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
      }

      /// <summary>
      /// Waktu dari server sebagai UTC. Nilai tanpa <see cref="DateTimeKind"/> dianggap UTC - begitulah
      /// DTO registry ditulis - bukan waktu lokal, yang akan menggeser jamnya bila diubah dengan
      /// <see cref="DateTime.ToUniversalTime"/>.
      /// </summary>
      public static DateTime AsUtc(DateTime value) => value.Kind switch {
         DateTimeKind.Utc => value,
         DateTimeKind.Local => value.ToUniversalTime(),
         _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
      };

      /// <summary>Nama pull lengkap: <c>host/root/nama</c>.</summary>
      public static string PullName(string host, string fullName) => host.Length == 0 ? fullName : $"{host}/{fullName}";

      /// <summary><c>docker pull</c> untuk sebuah tag.</summary>
      public static string DockerPullTag(string host, string fullName, string tag) =>
         $"docker pull {PullName(host, fullName)}:{tag}";

      /// <summary><c>docker pull</c> untuk sebuah digest.</summary>
      public static string DockerPullDigest(string host, string fullName, string digest) =>
         $"docker pull {PullName(host, fullName)}@{digest}";

      /// <summary><c>docker tag</c> dan <c>docker push</c> untuk mengirim image lokal ke container ini.</summary>
      public static string DockerTagPush(string host, string fullName, string tag) =>
         $"docker tag <local-image> {PullName(host, fullName)}:{tag}{Environment.NewLine}" +
         $"docker push {PullName(host, fullName)}:{tag}";

      /// <summary><c>docker login</c> untuk sebuah robot; token ditulis sebagai bagian perintah.</summary>
      public static string DockerLogin(string host, string robot, string token) =>
         $"docker login {host} -u {robot} -p {token}";

      /// <summary>Digest dipotong untuk tampilan: <c>sha256:</c> ditambah dua belas karakter pertama.</summary>
      public static string ShortDigest(string digest) {
         const int Shown = 12;
         var colon = digest.IndexOf(':');
         if (colon < 0) return digest.Length <= Shown ? digest : digest[..Shown] + "…";

         var hash = digest[(colon + 1)..];
         return hash.Length <= Shown ? digest : $"{digest[..(colon + 1)]}{hash[..Shown]}…";
      }
   }
}
