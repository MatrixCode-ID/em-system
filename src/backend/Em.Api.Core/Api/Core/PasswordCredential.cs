using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Kredensial berupa password: buktinya teks yang diketik pengguna, dan yang disimpan hanya
   /// hasil hash-nya, tidak pernah passwordnya sendiri.
   /// </summary>
   public class PasswordCredential : CredentialProviderBase<string>, ICredentialEnrollment<string>
   {
      /// <summary>Nama jenis kredensial ini.</summary>
      public const string CredentialType = Defaults.PasswordCredentialType;

      #region Initializer

      /// <summary>
      /// Membuat kredensial password milik <paramref name="user"/> beserta data tersimpannya.
      /// Pengguna yang belum pernah mengisi password tetap mendapat kredensial ini, hanya saja
      /// statusnya masih <see cref="CredentialState.Pending"/> sampai passwordnya diisi lewat
      /// <see cref="UpdateCredential"/>.
      /// </summary>
      /// <param name="user">Pengguna yang kredensialnya ingin dipakai.</param>
      /// <param name="services">Service data kredensial milik request yang sedang berjalan.</param>
      /// <returns>Kredensial password yang siap diperiksa atau diisi.</returns>
      public static async Task<PasswordCredential> CreateAsync(ta_User user, CredentialServices services) {
         var result = new PasswordCredential {
            User = user,
            Services = services,
            Hasher = services.GetService<IStringHasher>()
               ?? throw new InvalidOperationException($"No {nameof(IStringHasher)} is registered.")
         };
         await result.InitCredential();
         return result;
      }

      #endregion

      private PasswordCredential() { }

      /// <inheritdoc/>
      public override string Name => CredentialType;

      /// <summary>Komponen hashing yang dipakai menyimpan dan memeriksa password.</summary>
      public required IStringHasher Hasher { get; init; }

      /// <summary>
      /// Mengganti password pengguna dan menandai kredensialnya siap dipakai untuk masuk.
      /// </summary>
      /// <param name="password">Password baru dalam bentuk teks biasa; yang disimpan hanya hash-nya.</param>
      /// <exception cref="ArgumentException">Password yang diberikan kosong.</exception>
      public Task UpdateCredential(string password) {
         if (string.IsNullOrEmpty(password)) {
            throw new ArgumentException("Password must not be empty.", nameof(password));
         }

         return SaveCredentialAsync(data => {
            data.cCredentialSecret = Hasher.HashValue(password);
            data.cCredentialState = CredentialState.Active;
         });
      }

      // Setting a password is all it takes to enroll in this credential, so enrollment is the same
      // operation as changing it. Implemented explicitly to keep one public way of doing it.
      Task ICredentialEnrollment<string>.ConfirmEnrollAsync(string proof) => UpdateCredential(proof);

      protected override async Task InitCredential() {
         // Creating a user writes its password credential along with it, so normally there is one
         // to read here. What is left is the account that predates that, which would otherwise have
         // no credential to edit and no way to ever get one; it is given the same empty, Pending
         // credential a new account is created with. Opt-in credentials - an authenticator app, say
         // - do the opposite and create nothing until their enrollment finishes.
         if (await LoadCredentialAsync() is null) await CreateCredentialAsync();
      }

      protected override Task<bool> ValidateAsync(string payload) =>
         Task.FromResult(Hasher.CompareHashValue(payload, UserCredential?.cCredentialSecret ?? ""));
   }

   /// <summary>
   /// Mendaftarkan <see cref="PasswordCredential"/> sebagai salah satu jenis kredensial yang
   /// dikenal aplikasi.
   /// </summary>
   public class PasswordCredentialFactory : ICredentialProviderFactory
   {
      /// <inheritdoc/>
      public string Name => PasswordCredential.CredentialType;

      /// <inheritdoc/>
      public async Task<CredentialProviderBase> CreateAsync(ta_User user, CredentialServices services) =>
         await PasswordCredential.CreateAsync(user, services);
   }
}
