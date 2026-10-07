using System.Security.Cryptography;
using System.Text;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Ui.Maui.Shared
{
   public class DebugBuilder
   {
      internal List<ApiConnection> Connections { get; } = [];

      /// <summary>
      /// The name of the debug key, exactly the same as the name registered on the server. Empty when
      /// <see cref="SetDebugKey"/> has never been called.
      /// </summary>
      internal string? DebugKeyName { get; private set; }

      /// <summary>
      /// The developer's debug private key. Its value is never sent to the server - only the token made by
      /// signing with it is sent.
      /// </summary>
      internal string? DebugKey { get; private set; }

      /// <summary>
      /// Sets the debug key this application uses to sign in without going through the login screen. Both
      /// must be a pair with what is registered on the server: exactly the same key name, and a private key
      /// whose counterpart is there as a public key.
      /// <para>
      /// The private key is only used once when the application starts, to sign one token; it is never sent
      /// anywhere itself.
      /// </para>
      /// </summary>
      /// <param name="name">The key name, exactly the same as what is registered on the server.</param>
      /// <param name="privateKey">The RSA private key, as Base64 of the DER PKCS#1 (<c>RSA.ExportRSAPrivateKey</c>).</param>
      /// <exception cref="ArgumentException">Thrown when either argument is empty.</exception>
      public void SetDebugKey(string name, string privateKey) {
         if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Debug key name must not be empty.", nameof(name));
         }

         if (string.IsNullOrWhiteSpace(privateKey)) {
            throw new ArgumentException($"Debug key '{name}' has an empty private key.", nameof(privateKey));
         }

         DebugKeyName = name.Trim();
         DebugKey = privateKey.Trim();
      }

      public void AddDebugConnection(string cnName, string host, bool isDefault, int timeOut = 30) {
         var conn = new ApiConnection() {
            Host = host,
            Timeout = timeOut,
            IgnoreSslErrors = true,
            ProfileName = cnName,
            IsDebugConnection = true,
         };
         Connections.Add(conn);
         if (isDefault) DefaultConnection = conn;
      }

      /// <summary>
      /// Creates the debug token signed by the private key in <see cref="SetDebugKey"/>, or <c>null</c> when
      /// the key was never set. Called once when the application is built, so no ready-made token needs to be
      /// pasted into the source and nothing needs to be replaced periodically.
      /// </summary>
      /// <remarks>
      /// The token that is produced lives for the limit decided by the server. If the application is left
      /// running past that limit, its token dies midway - a restart will issue a new one.
      /// </remarks>
      /// <exception cref="InvalidOperationException">
      /// Thrown when the private key cannot be used to sign. It deliberately throws rather than silently
      /// turning debug mode off: if left silent, the mistake would be looked for elsewhere.
      /// </exception>
      internal string? CreateDebugToken() {
         if (DebugKey is null || DebugKeyName is null) {
            return null;
         }

         return DebugTokenProtocol.Create(DebugKeyName, LoadSigningKey(DebugKeyName, DebugKey), DateTime.UtcNow);
      }

      /// <summary>
      /// Reads the private key and proves that it really can be used to sign - not merely that it has the
      /// right shape - through one round of signing and verifying a trial payload. Its cost is paid once at
      /// startup, and the result is that a key mistake is found before the first window appears.
      /// </summary>
      /// <remarks>
      /// One thing cannot be checked here: whether this key is the pair of the public key registered on the
      /// server. A mismatch is only found on the first request, its symptom is every action being answered
      /// "action not found", and what explains the cause is the server log.
      /// </remarks>
      private static RsaKeyPair LoadSigningKey(string name, string privateKey) {
         // RsaKeyPair has no "private key only" form - its constructor demands a public key. So the private key
         // is imported once here, its public key is derived from it (a DER PKCS#1 private key already holds the
         // public components), and only then is the complete pair composed.
         using var rsa = RSA.Create();
         try {
            rsa.ImportRSAPrivateKey(Convert.FromBase64String(privateKey), out _);
         }
         catch (Exception x) when (x is FormatException or CryptographicException) {
            throw new InvalidOperationException(
               $"Debug key '{name}' is not a readable RSA private key. It must be Base64 of a DER PKCS#1 private key " +
               "(RSA.ExportRSAPrivateKey) - note that what belongs here is the private key, not the public one.", x);
         }

         var keyPair = new RsaKeyPair(Convert.ToBase64String(rsa.ExportRSAPublicKey()), privateKey);
         var probe = Encoding.ASCII.GetBytes($"em.debugkey.probe:{name}");

         try {
            if (keyPair.VerifyData(probe, keyPair.SignData(probe))) {
               return keyPair;
            }
         }
         catch (CryptographicException x) {
            throw new InvalidOperationException(
               $"Debug key '{name}' could not be used to sign with the algorithm this application requires.", x);
         }

         throw new InvalidOperationException(
            $"Debug key '{name}' produced a signature that does not verify against its own public key.");
      }

      internal ApiConnection? DefaultConnection { get; set; }
   }
}
