using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Base class untuk setiap cara pengguna membuktikan dirinya - password, aplikasi authenticator,
   /// dan cara lain yang menyusul. Turunannya cukup menentukan nama jenis kredensialnya, cara
   /// memuat datanya saat dibuat, dan cara memeriksa bukti yang dikirim pengguna; urusan yang sama
   /// untuk semua jenis - membaca, membuat, menyimpan, dan mencabut data kredensial - sudah
   /// disediakan di sini.
   /// <para>
   /// Jangan diturunkan langsung: pakai <see cref="CredentialProviderBase{TPayload}"/> supaya jenis
   /// bukti yang diterima ikut diperiksa compiler.
   /// </para>
   /// </summary>
   public abstract class CredentialProviderBase
   {
      /// <summary>Pengguna yang memiliki kredensial ini.</summary>
      public required ta_User User { get; init; }

      /// <summary>
      /// Service data kredensial milik server, tempat baris kredensial dibaca dan ditulis. Diisi
      /// oleh action yang sedang berjalan, karena hanya dia yang memegang DbContext milik request
      /// tersebut.
      /// </summary>
      public required CredentialServices Services { get; init; }

      /// <summary>
      /// Nama jenis kredensial ini, mis. <c>"PASSWORD"</c>. Dipakai sebagai penanda saat mencari
      /// data kredensial milik pengguna, jadi nilainya harus tetap dan unik antar jenis.
      /// </summary>
      public abstract string Name { get; }

      // The stored credential this provider works on. Null until InitCredential has run, and for
      // credentials that only come into existence once the user has enrolled in them.
      protected ta_UserCredential? UserCredential { get; private set; }

      /// <summary>
      /// Status kredensial ini. Bernilai <see cref="CredentialState.Pending"/> selama penggunanya
      /// belum punya kredensial jenis ini sama sekali.
      /// </summary>
      public CredentialState State => UserCredential?.cCredentialState ?? CredentialState.Pending;

      /// <summary>
      /// Menandakan kredensial ini sudah selesai didaftarkan dan boleh dipakai untuk masuk.
      /// Selama masih <c>false</c>, <see cref="IsValidAsync"/> selalu menolak apa pun buktinya.
      /// </summary>
      public bool IsEnrolled => UserCredential is not null && State >= CredentialState.Inactive;

      /// <summary>
      /// Memeriksa apakah bukti yang dikirim pengguna cocok dengan kredensial yang tersimpan.
      /// Dipakai oleh kode yang tidak tahu jenis kredensial apa yang sedang dipegangnya - alur
      /// login, misalnya, yang menawarkan satu per satu kredensial milik pengguna.
      /// </summary>
      /// <param name="payload">
      /// Bukti dari pengguna. Bentuknya ditentukan masing-masing jenis kredensial: password berupa
      /// teks, aplikasi authenticator berupa kode angka, dan seterusnya.
      /// </param>
      /// <returns><c>true</c> jika buktinya sah.</returns>
      /// <exception cref="ArgumentException">
      /// Jenis <paramref name="payload"/> bukan yang diminta kredensial ini.
      /// </exception>
      public abstract Task<bool> IsValidAsync(object payload);

      /// <summary>
      /// Mencabut kredensial ini sehingga tidak bisa lagi dipakai masuk, tanpa menghapus datanya.
      /// Dipakai misalnya saat perangkat authenticator pengguna hilang.
      /// </summary>
      public async Task RevokeAsync() =>
         await SaveCredentialAsync(data => data.cCredentialState = CredentialState.Revoked);

      // Runs once while the provider is being created: this is where a subclass decides whether it
      // merely reads what is already stored, or also creates a credential for a user who has none.
      protected abstract Task InitCredential();

      // Reads the stored credential of this type for the user and makes it the one this provider
      // works on. Returns null when the user has no credential of this type.
      protected async Task<ta_UserCredential?> LoadCredentialAsync() =>
         UserCredential = await Services.GetTa_UserCredential_ByType(User.cUserId, Name);

      // Creates the credential for this user. Left Pending by default: a credential that was only
      // just created still holds nothing to check a login against.
      protected async Task<ta_UserCredential> CreateCredentialAsync(
         CredentialState state = CredentialState.Pending, string? key = null, string? secret = null) {
         var stamp = await Services.App.GetDateStampAsync();
         var data = new ta_UserCredential {
            cCredentialId = $"{Ulid.NewUlid()}",
            cUserId = User.cUserId,
            cCredentialType = Name,
            cCredentialState = state,
            cCredentialKey = key,
            cCredentialSecret = secret,
            ustamp = stamp,
            datestamp = stamp,
            json_object = null
         };
         await Services.PostTa_UserCredential_New(data);
         UserCredential = data;
         return data;
      }

      // Applies an edit to the stored credential and writes it back. The edit runs on a copy rather
      // than on the data being held, so that a write the server rejects leaves the provider showing
      // what is actually stored instead of a change that never landed.
      protected async Task<ta_UserCredential> SaveCredentialAsync(Action<ta_UserCredential> edit) {
         var current = UserCredential
            ?? throw new InvalidOperationException(
               $"Credential '{Name}' has not been created for user '{User.cUserId}'.");

         var draft = new ta_UserCredential {
            cCredentialId = current.cCredentialId,
            cUserId = current.cUserId,
            cCredentialType = current.cCredentialType,
            cCredentialState = current.cCredentialState,
            cCredentialKey = current.cCredentialKey,
            cCredentialSecret = current.cCredentialSecret,
            ustamp = current.ustamp,
            datestamp = current.datestamp,
            json_object = current.json_object
         };

         edit(draft);
         draft.ustamp = await Services.App.GetDateStampAsync();
         await Services.PostTa_UserCredential_Update(draft);
         UserCredential = draft;
         return draft;
      }
   }

   /// <summary>
   /// Base class untuk jenis kredensial yang buktinya berbentuk <typeparamref name="TPayload"/>.
   /// Selain menjaga jenis bukti tetap cocok, di sini juga dipastikan kredensial yang belum
   /// terdaftar atau sudah dicabut selalu ditolak, sehingga turunannya tinggal mengurus
   /// pemeriksaan isi buktinya saja.
   /// </summary>
   /// <typeparam name="TPayload">
   /// Jenis bukti yang diminta dari pengguna, mis. <see cref="string"/> untuk password.
   /// </typeparam>
   public abstract class CredentialProviderBase<TPayload> : CredentialProviderBase
   {
      /// <inheritdoc/>
      public sealed override Task<bool> IsValidAsync(object payload) =>
         payload is TPayload typed
            ? IsValidAsync(typed)
            : throw new ArgumentException(
               $"Credential '{Name}' expects a payload of type {typeof(TPayload).Name}.", nameof(payload));

      /// <summary>
      /// Memeriksa apakah bukti yang dikirim pengguna cocok dengan kredensial yang tersimpan.
      /// Kredensial yang belum selesai didaftarkan atau sudah dicabut selalu ditolak di sini,
      /// tanpa perlu memeriksa isi buktinya.
      /// </summary>
      /// <param name="payload">Bukti dari pengguna.</param>
      /// <returns><c>true</c> jika buktinya sah.</returns>
      public Task<bool> IsValidAsync(TPayload payload) =>
         IsEnrolled ? ValidateAsync(payload) : Task.FromResult(false);

      // The actual check, reached only for a credential that is enrolled and not revoked. Async on
      // purpose: a one-time code has to record that it was used so the same code cannot be replayed,
      // and that recording is a write.
      protected abstract Task<bool> ValidateAsync(TPayload payload);
   }
}
