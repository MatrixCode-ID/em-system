using System.IO;
using System.Security.Cryptography;

namespace Em.Ui.Wpf.Core.Release;

/// <summary>Creates and validates permanent signing material, passing passwords only through the child environment.</summary>
public static class AndroidKeystore {
   public static IReadOnlyList<string> CreateArguments(string file, string alias, string publisher) => [
      "-genkeypair", "-keystore", file, "-storetype", "PKCS12", "-keyalg", "RSA", "-keysize", "4096", "-validity", "10000",
      "-alias", alias, "-dname", "CN=" + publisher.Replace("\\", "\\\\").Replace(",", "\\,").Replace("+", "\\+"),
      "-storepass:env", "EM_ANDROID_STORE_PASS", "-keypass:env", "EM_ANDROID_KEY_PASS"];

   public static async Task<ReleaseAndroid> CreateAsync(string java, string publisher, CancellationToken token) {
      var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
      var data = new ReleaseAndroid { KeystoreAlias = "release", KeystorePassword = password, KeyPassword = password };
      var folder = Path.Combine(Path.GetTempPath(), "EmAndroidKey-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(folder);
      try {
         var file = Path.Combine(folder, "release.p12");
         await AndroidProcess.CaptureAsync(Keytool(java), CreateArguments(file, data.KeystoreAlias, publisher), EnvironmentFor(data), token);
         data.KeystoreBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(file, token));
         data.CertificateSha256 = await CertificateAsync(java, file, data, folder, token);
         return data;
      }
      finally { Directory.Delete(folder, true); }
   }

   public static async Task<ReleaseAndroid> ImportAsync(string java, string file, string alias, string storePassword, string keyPassword, CancellationToken token) {
      var data = new ReleaseAndroid { KeystoreBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(file, token)),
         KeystoreAlias = alias, KeystorePassword = storePassword, KeyPassword = keyPassword };
      var folder = Path.Combine(Path.GetTempPath(), "EmAndroidKey-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(folder);
      try {
         var temporary = Path.Combine(folder, "import.keystore");
         await File.WriteAllBytesAsync(temporary, Convert.FromBase64String(data.KeystoreBase64), token);
         await AndroidProcess.CaptureAsync(Keytool(java), ["-list", "-keystore", temporary, "-alias", alias, "-storepass:env", "EM_ANDROID_STORE_PASS"], EnvironmentFor(data), token);
         await AndroidProcess.CaptureAsync(Keytool(java), ["-certreq", "-keystore", temporary, "-alias", alias, "-storepass:env", "EM_ANDROID_STORE_PASS", "-keypass:env", "EM_ANDROID_KEY_PASS", "-file", Path.Combine(folder, "request.csr")], EnvironmentFor(data), token);
         data.CertificateSha256 = await CertificateAsync(java, temporary, data, folder, token);
         return data;
      }
      finally { Directory.Delete(folder, true); }
   }

   private static async Task<string> CertificateAsync(string java, string file, ReleaseAndroid data, string folder, CancellationToken token) {
      var certificate = Path.Combine(folder, "certificate.der");
      await AndroidProcess.CaptureAsync(Keytool(java), ["-exportcert", "-keystore", file, "-alias", data.KeystoreAlias, "-storepass:env", "EM_ANDROID_STORE_PASS", "-file", certificate], EnvironmentFor(data), token);
      return Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(certificate, token)));
   }

   public static IReadOnlyDictionary<string, string> EnvironmentFor(ReleaseAndroid data) => new Dictionary<string, string> {
      ["EM_ANDROID_STORE_PASS"] = data.KeystorePassword, ["EM_ANDROID_KEY_PASS"] = data.KeyPassword };
   private static string Keytool(string java) => Path.Combine(java, "bin", "keytool.exe");
}
