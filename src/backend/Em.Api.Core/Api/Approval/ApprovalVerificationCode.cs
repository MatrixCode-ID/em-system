using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Generator of signature verification codes: a short code printed on the signature and at the edge of
   /// every page of the document, so a printed document can be traced back to its request.
   /// </summary>
   /// <remarks>
   /// The letters are chosen so a code that is read aloud or retyped does not change meaning: there is no
   /// <c>I</c>, <c>L</c>, <c>O</c>, or <c>U</c>, so nothing is easily mixed up with the digit one, zero, or
   /// with another letter. The code is random, not sequential - from the code of one signature the code
   /// of another cannot be guessed.
   /// </remarks>
   public static class ApprovalVerificationCode
   {
      // Crockford Base32: the digits and the letters, minus I, L, O and U.
      private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

      /// <summary>Length of the verification code, in characters.</summary>
      public const int Length = 10;

      private const int Attempts = 5;

      /// <summary>
      /// Creates a verification code that no step has used yet.
      /// </summary>
      /// <param name="ctx">The core database context, where codes already used are looked up.</param>
      /// <param name="cancellationToken">Cancellation token.</param>
      /// <exception cref="InvalidOperationException">
      /// Thrown when every attempt collides with an existing code - almost impossible with the length of
      /// <see cref="Length"/>, so more likely a sign that something is wrong than bad luck.
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
      /// Creates one new verification code.
      /// </summary>
      /// <remarks>
      /// Whether the code is already used is not checked here - the caller checks it, when writing it,
      /// because only there is there a transaction that can retry if the code happens to be the same. With
      /// the length of <see cref="Length"/>, that chance is very small.
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
