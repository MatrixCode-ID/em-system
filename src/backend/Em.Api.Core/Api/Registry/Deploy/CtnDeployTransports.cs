using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Em.Api.Core.Registry.Deploy
{
   /// <summary>Result of one command on the SSH host.</summary>
   internal sealed record CtnShellResult(int ExitCode, string Output, string Error);

   /// <summary>An open SSH session to a Docker host. Abstracted so the deploy flows can be tested without SSH.</summary>
   internal interface ICtnDeployShell : IDisposable
   {
      /// <summary>Runs a shell command; <paramref name="stdin"/> is written to its standard input when given.</summary>
      Task<CtnShellResult> RunAsync(string command, string? stdin, CancellationToken ct);

      /// <summary>Reads a text file; <c>null</c> when it does not exist.</summary>
      Task<string?> ReadFileAsync(string path, CancellationToken ct);

      /// <summary>Creates or overwrites a text file; the mode of an existing file is kept.</summary>
      Task WriteFileAsync(string path, string content, CancellationToken ct);

      /// <summary>Handler that reaches the Docker Engine API through <c>docker system dial-stdio</c>.</summary>
      HttpMessageHandler CreateDockerHandler();
   }

   /// <summary>Creates the connections a deploy needs. The default implementation is <see cref="CtnDeployTransports"/>.</summary>
   internal interface ICtnDeployTransports
   {
      /// <summary>Opens SSH with the pinned host key.</summary>
      /// <exception cref="CtnDeployFingerprintException">No key is pinned, or the server offered another one.</exception>
      Task<ICtnDeployShell> ConnectSshAsync(CtnDeployTarget target, CancellationToken ct);

      /// <summary>HTTP handler for Portainer that trusts a valid certificate or the pinned one, recording the offered one in <paramref name="pin"/>.</summary>
      HttpMessageHandler CreateHttpHandler(CtnTlsPin pin);
   }

   /// <summary>Pinned and offered TLS certificate fingerprint of one Portainer connection.</summary>
   internal sealed class CtnTlsPin(string? pinned)
   {
      public string? Pinned { get; } = string.IsNullOrWhiteSpace(pinned) ? null : pinned.Trim();

      /// <summary>Fingerprint of a certificate the system does not trust; set when it was refused.</summary>
      public string? Offered { get; set; }

      /// <summary>Accepts a certificate the system trusts, or one whose fingerprint equals <see cref="Pinned"/>.</summary>
      public bool Validate(X509Certificate? certificate, SslPolicyErrors errors) {
         if (errors == SslPolicyErrors.None) return true;
         if (certificate is null) return false;

         var fingerprint = Fingerprint(certificate.GetRawCertData());
         if (Pinned is not null && string.Equals(Pinned, fingerprint, StringComparison.OrdinalIgnoreCase)) return true;
         Offered = fingerprint;
         return false;
      }

      /// <summary><c>SHA256:</c> plus the certificate hash as colon-separated hex, as browsers show it.</summary>
      public static string Fingerprint(byte[] certificate) =>
         "SHA256:" + string.Join(":", SHA256.HashData(certificate).Select(b => b.ToString("X2")));
   }

   /// <summary>The server offered a host key or certificate that is not pinned.</summary>
   internal sealed class CtnDeployFingerprintException(string offered, bool mismatch) : Exception(mismatch
      ? $"The server's fingerprint {offered} does not match the pinned one. If the server was reinstalled, run Test connection and accept the new fingerprint."
      : $"The server's fingerprint {offered} is not pinned yet. Run Test connection and accept it.")
   {
      public string Offered { get; } = offered;
   }

   /// <summary>Real transports: SSH.NET and <see cref="SocketsHttpHandler"/>.</summary>
   internal sealed class CtnDeployTransports : ICtnDeployTransports
   {
      public Task<ICtnDeployShell> ConnectSshAsync(CtnDeployTarget target, CancellationToken ct) => SshDeployShell.ConnectAsync(target, ct);

      public HttpMessageHandler CreateHttpHandler(CtnTlsPin pin) => new SocketsHttpHandler {
         AllowAutoRedirect = false,
         ConnectTimeout = TimeSpan.FromSeconds(20),
         SslOptions = new SslClientAuthenticationOptions {
            RemoteCertificateValidationCallback = (_, certificate, _, errors) => pin.Validate(certificate, errors)
         }
      };
   }
}
