using System.Text;
using Em.Api.Core.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Em.Api.Core.Registry.Deploy
{
   /// <summary>
   /// SSH session to a Docker host (SSH.NET): pinned host key, key or password login, commands with captured output,
   /// SFTP for files, and <c>docker system dial-stdio</c> as transport for the Docker Engine API.
   /// </summary>
   internal sealed class SshDeployShell : ICtnDeployShell
   {
      private readonly SshClient _ssh;
      private readonly SftpClient _sftp;

      private SshDeployShell(SshClient ssh, SftpClient sftp) {
         _ssh = ssh;
         _sftp = sftp;
      }

      /// <summary><c>SHA256:</c> plus the unpadded Base64 hash, the format <c>ssh-keygen -lf</c> prints.</summary>
      public static string Fingerprint(string sha256Base64) => "SHA256:" + sha256Base64;

      public static async Task<ICtnDeployShell> ConnectAsync(CtnDeployTarget target, CancellationToken ct) {
         var info = new ConnectionInfo(target.Host, target.Port ?? 22, target.User ?? "", Authentication(target)) {
            Timeout = TimeSpan.FromSeconds(20)
         };
         var pinned = string.IsNullOrWhiteSpace(target.Fingerprint) ? null : target.Fingerprint.Trim();
         string? offered = null;

         void Check(object? sender, HostKeyEventArgs e) {
            var fingerprint = Fingerprint(e.FingerPrintSHA256);
            e.CanTrust = pinned is not null && string.Equals(pinned, fingerprint, StringComparison.Ordinal);
            if (!e.CanTrust) offered = fingerprint;
         }

         var ssh = new SshClient(info);
         var sftp = new SftpClient(info);
         ssh.HostKeyReceived += Check;
         sftp.HostKeyReceived += Check;
         try {
            await ssh.ConnectAsync(ct);
            await sftp.ConnectAsync(ct);
            return new SshDeployShell(ssh, sftp);
         }
         catch (Exception) when (offered is not null) {
            ssh.Dispose();
            sftp.Dispose();
            throw new CtnDeployFingerprintException(offered, pinned is not null);
         }
         catch {
            ssh.Dispose();
            sftp.Dispose();
            throw;
         }
      }

      private static AuthenticationMethod Authentication(CtnDeployTarget target) {
         var user = target.User ?? "";
         if (target.Auth == CtnDeployAuth.SshPassword) return new PasswordAuthenticationMethod(user, target.Secret ?? "");

         var key = new MemoryStream(Encoding.UTF8.GetBytes(target.Secret ?? ""));
         var file = string.IsNullOrEmpty(target.Passphrase) ? new PrivateKeyFile(key) : new PrivateKeyFile(key, target.Passphrase);
         return new PrivateKeyAuthenticationMethod(user, file);
      }

      public async Task<CtnShellResult> RunAsync(string command, string? stdin, CancellationToken ct) {
         using var cmd = _ssh.CreateCommand(command);
         var execution = cmd.ExecuteAsync(ct);
         if (stdin is not null) {
            using var input = cmd.CreateInputStream();
            var bytes = Encoding.UTF8.GetBytes(stdin);
            await input.WriteAsync(bytes, ct);
         }

         await execution;
         return new CtnShellResult(cmd.ExitStatus ?? -1, cmd.Result, cmd.Error);
      }

      public Task<string?> ReadFileAsync(string path, CancellationToken ct) => Task.Run(() => {
         if (!_sftp.Exists(path)) return null;
         return (string?)_sftp.ReadAllText(path, Encoding.UTF8);
      }, ct);

      public Task WriteFileAsync(string path, string content, CancellationToken ct) => Task.Run(() => {
         using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(content));
         _sftp.UploadFile(stream, path, true);
      }, ct);

      public HttpMessageHandler CreateDockerHandler() => new SocketsHttpHandler {
         // One dial-stdio process per HTTP connection; the engine closes nothing on its own.
         PooledConnectionLifetime = TimeSpan.FromMinutes(5),
         ConnectCallback = (_, _) => ValueTask.FromResult<Stream>(new DialStdioStream(_ssh.CreateCommand("docker system dial-stdio")))
      };

      public void Dispose() {
         _sftp.Dispose();
         _ssh.Dispose();
      }

      /// <summary>
      /// Duplex stream over one <c>docker system dial-stdio</c> command: writes go to its standard input, reads come
      /// from its standard output. Disposing closes standard input, which ends the command.
      /// </summary>
      private sealed class DialStdioStream : Stream
      {
         private readonly SshCommand _command;
         private readonly Stream _input;
         private readonly Task _execution;

         public DialStdioStream(SshCommand command) {
            _command = command;
            _execution = command.ExecuteAsync(CancellationToken.None);
            _input = command.CreateInputStream();
         }

         public override bool CanRead => true;
         public override bool CanWrite => true;
         public override bool CanSeek => false;
         public override long Length => throw new NotSupportedException();
         public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

         public override int Read(byte[] buffer, int offset, int count) => _command.OutputStream.Read(buffer, offset, count);

         public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            _command.OutputStream.ReadAsync(buffer, offset, count, ct);

         public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            _command.OutputStream.ReadAsync(buffer, ct);

         public override void Write(byte[] buffer, int offset, int count) => _input.Write(buffer, offset, count);

         public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            _input.WriteAsync(buffer, offset, count, ct);

         public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => _input.WriteAsync(buffer, ct);

         public override void Flush() => _input.Flush();
         public override Task FlushAsync(CancellationToken ct) => _input.FlushAsync(ct);
         public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
         public override void SetLength(long value) => throw new NotSupportedException();

         protected override void Dispose(bool disposing) {
            if (disposing) {
               try {
                  _input.Dispose();
                  _execution.Wait(TimeSpan.FromSeconds(5));
               }
               catch (Exception) {
                  // The connection is going away; a command that ended badly changes nothing.
               }

               _command.Dispose();
            }

            base.Dispose(disposing);
         }
      }
   }
}
