using System.Buffers.Text;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>Core utility actions that are open to any caller: the server clock, new ids, and the key handshake.</summary>
   [Module("core")]
   public class ApiCoreServices : ServicesBase, IEmApiCoreServices
   {
      private readonly ApiCoreContext _ctx;

      /// <summary>Creates the service with the core database context.</summary>
      public ApiCoreServices(ApiCoreContext ctx) {
         _ctx = ctx;
      }

      #region API Test Suites
      
      /// <summary>
      /// Returns the server's RSA public key as-is, for provisioning/pinning by an already authenticated
      /// caller (e.g. copying the server key into client configuration).
      /// WARNING: the key from here is not yet proven to really belong to the intended server - there is no
      /// proof of private key ownership accompanying it. A party that needs to validate the key must use
      /// <see cref="Handshake"/>, not this action.
      /// </summary>
      [GetAction] // IsPublicAction is deliberately left false: this is not an anonymous endpoint.
      public async Task<string> GetServerPublicKey() {
         var rsaPairs = await GetServerRsaKeyAsync();
         return rsaPairs.PublicKey;
      }

      /// <summary>
      /// Proves to the client that this server really holds the private key of the public key it returns,
      /// by signing a random nonce sent by the client. The client verifies that signature with the public key
      /// in the result, so a stale public key or a wrong host is detected immediately.
      /// </summary>
      /// <param name="nonce">
      /// Random nonce from the client, Base64Url encoded, between <c>ProbeProtocol.MinNonceLength</c> and
      /// <c>ProbeProtocol.MaxNonceLength</c> bytes long.
      /// </param>
      /// <remarks>
      /// This action is public because it comes before authentication: the client does not yet have a
      /// verified server public key, so it cannot encrypt any credential yet. The server only signs a payload
      /// with the probe domain prefix (see <c>ProbeProtocol</c>) and never decrypts data sent by the client,
      /// so this endpoint cannot act as a decryption oracle.
      /// </remarks>
      [GetAction(IsPublicAction = true)]
      public async Task<ServerHandshakeResult> Handshake(string nonce) {
         var nonceBytes = ProbeProtocol.DecodeNonce(nonce);
         var rsaPairs = await GetServerRsaKeyAsync();
         using var rsa = rsaPairs.CreateRsa();
         return new ServerHandshakeResult {
            PublicKey = rsaPairs.PublicKey,
            Signature = Base64Url.EncodeToString(rsaPairs.SignData(ProbeProtocol.BuildSignaturePayload(nonceBytes))),
            KeySize = rsa.KeySize
         };
      }

      // All three are closed: no path before login calls them - the timestamp and the new row id are all
      // needed inside the workspace. If a pre-login need for them appears later, open them one by one with
      // their reasons, not as a block.
      /// <summary>Gets the current server time.</summary>
      [GetAction]
      public async Task<DateTimeOffset> GetTimeStamp() => 
         await Task.FromResult(DateTimeOffset.Now);

      /// <summary>Generates a new ULID.</summary>
      [GetAction]
      public async Task<Ulid> GetUlid() => await Task.FromResult(Ulid.NewUlid());

      /// <summary>Generates several new ULIDs at once.</summary>
      [GetAction]
      public async Task<Ulid[]> GetUlidMany(int count) {
         var results = new Ulid[count];
         for (var i = 0; i < count; i++) {
            results[i] = Ulid.NewUlid();
         }
         return await Task.FromResult(results);
      }
      
      #endregion
   }
}
