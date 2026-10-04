using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Pembuat kode verifikasi tanda tangan: kode pendek yang tercetak pada tanda tangan dan di tepi setiap
   /// halaman dokumennya, sehingga dokumen tercetak bisa dirunut kembali ke request-nya.
   /// </summary>
   /// <remarks>
   /// Hurufnya dipilih supaya kode yang dibacakan atau diketik ulang orang tidak berubah arti: tidak ada
   /// <c>I</c>, <c>L</c>, <c>O</c>, dan <c>U</c>, jadi tidak ada pasangan yang mudah tertukar dengan angka
   /// satu, nol, atau dengan huruf lain. Kodenya acak, bukan berurutan - dari kode satu tanda tangan tidak
   /// bisa diterka kode tanda tangan lain.
   /// </remarks>
   public static class ApprovalVerificationCode
   {
      // Crockford Base32: the digits and the letters, minus I, L, O and U.
      private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

      /// <summary>Panjang kode verifikasi, dalam karakter.</summary>
      public const int Length = 10;

      private const int Attempts = 5;

      /// <summary>
      /// Membuat kode verifikasi yang belum dipakai langkah mana pun.
      /// </summary>
      /// <param name="ctx">Context database inti, tempat kode yang sudah terpakai dicari.</param>
      /// <param name="cancellationToken">Token pembatalan.</param>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau semua percobaan bertabrakan dengan kode yang sudah ada - hampir mustahil dengan
      /// panjang <see cref="Length"/>, jadi lebih mungkin tanda ada yang salah daripada nasib buruk.
      /// </exception>
      internal static async Task<string> CreateUnusedAsync(ApiCoreContext ctx,
         CancellationToken cancellationToken = default) {
         for (var attempt = 0; attempt < Attempts; attempt++) {
            var code = Create();
            if (!await ctx.ta_ApprovalRequestSteps.AnyAsync(r => r.cApprovalRequestStepVerificationCode == code,
                   cancellationToken)) {
               return code;
            }
         }

         throw new InvalidOperationException($"No unused verification code was found in {Attempts} attempts.");
      }

      /// <summary>
      /// Membuat satu kode verifikasi baru.
      /// </summary>
      /// <remarks>
      /// Kode yang sudah terpakai tidak diperiksa di sini - yang memeriksanya adalah pemanggil, saat
      /// menulisnya, karena hanya di sana ada transaksi yang bisa mengulang kalau kodenya kebetulan sama.
      /// Dengan panjang <see cref="Length"/>, kemungkinan itu sangat kecil.
      /// </remarks>
      public static string Create() {
         var buffer = new char[Length];

         for (var i = 0; i < buffer.Length; i++) {
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
         }

         return new string(buffer);
      }
   }
}
