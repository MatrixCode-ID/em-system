using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Core;
using Em.Ui.Maui.Shared;

// ReSharper disable once CheckNamespace

/// <summary>
/// Kumpulan extension method untuk kebutuhan UI MAUI: konfigurasi mode debug, jalan masuk module ke
/// claim miliknya, inisial nama untuk avatar, dan serialisasi pesan exception.
/// </summary>
public static class Extensions
{
   /// <summary>
   /// Menyalakan mode debug aplikasi: koneksi debug yang dipakai dan key debug yang menandatangani
   /// token-nya. Panggil sekali saat konfigurasi awal aplikasi, biasanya di dalam <c>#if DEBUG</c>.
   /// </summary>
   /// <param name="appBuilder">Builder aplikasi yang sedang dikonfigurasi.</param>
   /// <param name="builder">Callback yang mengisi koneksi dan key debug.</param>
   /// <returns>Builder yang sama, supaya pemanggilannya bisa dirangkai.</returns>
   public static EmAppBuilder AddDebug(this EmAppBuilder appBuilder, Action<DebugBuilder> builder) {
      var obj = new DebugBuilder();
      builder(obj);
      appBuilder.DebugBuilder = obj;
      return appBuilder;
   }

   /// <summary>
   /// Jalan masuk module ke claim milik service-nya sendiri: <c>Services.Claims()["CreateNewItem"]</c>.
   /// Tinggal di sini, bukan di <c>Em.Ui.Core</c>, karena inilah satu-satunya potongan yang perlu
   /// tahu <see cref="EmApp"/> - <c>ClaimCollection</c> sendiri tidak menyentuh MAUI sama sekali.
   /// Objeknya dibentuk ulang setiap pemanggilan; jangan disimpan di field.
   /// </summary>
   /// <param name="services">Service module yang ditanyakan claim-nya.</param>
   /// <exception cref="InvalidOperationException">
   /// Dilempar kalau <paramref name="services"/> bukan turunan <see cref="ServiceMauiBase"/> - pemasangan
   /// yang salah, bukan "tidak punya hak", jadi tidak dijawab sebagai collection kosong.
   /// </exception>
   public static ClaimCollection Claims(this IServices services) =>
      services is ServiceMauiBase svc
         ? new ClaimCollection(svc.ModuleName, svc.App.AllClaims, svc.App.ActiveUser)
         : throw new InvalidOperationException(
            $"Service '{services.GetType().FullName}' is not a MAUI client service, so its claims cannot be resolved.");

   /// <summary>
   /// Menyusun inisial dua huruf dari sebuah nama, untuk dipakai sebagai isi lingkaran avatar -
   /// "SYSTEM DEBUGGER" jadi "SD". Selalu menghasilkan sesuatu: <c>?</c> untuk nama yang kosong,
   /// supaya lingkarannya tidak pernah tampil melompong.
   /// </summary>
   /// <param name="name">Nama yang akan diambil inisialnya.</param>
   /// <returns>Inisial dalam huruf besar, paling banyak dua huruf.</returns>
   /// <remarks>
   /// Dua huruf, bukan satu: satu huruf terlalu mudah dipakai bersama separuh orang di satu
   /// perusahaan. Nama yang cuma sepatah kata tidak punya nama belakang untuk diambil, jadi ia
   /// menyerahkan huruf keduanya sendiri - "debugger" terbaca "DE", bukan "D" yang berdiri sendirian.
   /// </remarks>
   public static string ToInitials(this string? name) {
      var words = (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
      return words.Length switch {
         0 => "?",
         1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
         _ => $"{words[0][0]}{words[^1][0]}".ToUpperInvariant()
      };
   }

   /// <summary>
   /// Menyusun ringkasan pesan dari sebuah exception beserta seluruh inner exception-nya
   /// (termasuk <see cref="AggregateException"/> yang di-flatten), berupa teks bertingkat
   /// (indentasi per level) untuk ditampilkan ke user/log secara ringkas.
   /// </summary>
   /// <param name="x">Exception yang akan diserialisasi.</param>
   /// <returns>Teks ringkasan pesan exception secara bertingkat.</returns>
   public static string SerializedMessagesDefault(this Exception x) {
      var sb = new System.Text.StringBuilder();
      var exceptions = new Stack<(Exception Exception, int Level)>();

      exceptions.Push((x, 0));

      while (exceptions.Count > 0) {
         var (exception, level) = exceptions.Pop();

         if (exception is AggregateException aggregateException) {
            exception = aggregateException.Flatten();
         }

         if (sb.Length > 0) {
            sb.AppendLine();
         }

         if (level > 0) {
            sb.Append(new string(' ', level * 2));
            sb.Append("-> ");
         }

         sb.Append(exception.GetType().Name);

         if (!string.IsNullOrWhiteSpace(exception.Message)) {
            sb.Append(": ");
            sb.Append(exception.Message);
         }

         if (exception is AggregateException flattenedAggregateException) {
            for (var i = flattenedAggregateException.InnerExceptions.Count - 1; i >= 0; i--) {
               exceptions.Push((flattenedAggregateException.InnerExceptions[i], level + 1));
            }
         }
         else if (exception.InnerException is not null) {
            exceptions.Push((exception.InnerException, level + 1));
         }
      }

      return sb.ToString();
   }
}
