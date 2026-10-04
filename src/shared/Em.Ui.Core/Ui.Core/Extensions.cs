using System.Text.Json;
using Em.Shared;

namespace Em.Ui.Core
{
   public static class Extensions
   {
      /// <summary>
      /// Membaca respons HTTP dari sebuah action API sebagai <see cref="ActionResult"/>, lalu mengembalikan
      /// <see cref="ActionResult.Data"/>-nya sebagai <typeparamref name="T"/>. Dipakai supaya pemanggil cukup
      /// mengurusi nilai hasilnya saja, tanpa mengulang pemeriksaan status/error di setiap call site.
      /// </summary>
      /// <typeparam name="T">Tipe data yang diharapkan dari action.</typeparam>
      /// <param name="task">Task pemanggilan HTTP yang akan ditunggu.</param>
      /// <returns>
      /// Data hasil action, atau <c>default</c> kalau action tidak mengembalikan data
      /// (<see cref="ActionResult.HasData"/> bernilai <c>false</c>).
      /// </returns>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau body respons tidak bisa dibaca sebagai <see cref="ActionResult"/>, atau kalau server
      /// menandai hasilnya tidak valid.
      /// </exception>
      internal static async Task<T> ProcessHttpResult<T>(this Task<HttpResponseMessage> task) {
         using var response = await task.ConfigureAwait(false);
         var resultString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

         ActionResult? resultObj;
         try {
            resultObj = JsonSerializer.Deserialize<ActionResult>(resultString, Defaults.ResponseJsonOptions);
         }
         catch (JsonException x) {
            throw new InvalidOperationException(
               $"Server response could not be read as {nameof(ActionResult)} (HTTP {(int)response.StatusCode}). " +
               "The host may not be an EM API server.", x);
         }

         if (resultObj is null) {
            throw new InvalidOperationException("Server returned an empty or invalid response body.");
         }

         if (!resultObj.ValidResult) {
            // The status the action chose is carried through rather than flattened into the message,
            // so a caller can tell a rejection it should act on - a wrong password, an expired token,
            // a request it is not allowed to make - from a server that is simply broken. Matching on
            // the message text instead would break the moment the wording changes.
            throw new ActionException(
               $"Server Error: {resultObj.ErrorMessage ?? "the server reported an error without a message."}",
               resultObj.StatusCode);
         }

         return !resultObj.HasData
            ? default!
            : ((JsonElement)resultObj.Data!).Deserialize<T>(Defaults.ResponseJsonOptions)!;
      }

      /// <summary>
      /// Membaca respons HTTP dari action yang mengembalikan isi file. Jawaban sukses dikembalikan sebagai
      /// stream yang dibaca langsung dari jaringan; jawaban gagal - yang tetap berupa amplop
      /// <see cref="ActionResult"/> - dilempar persis seperti <see cref="ProcessHttpResult{T}"/>.
      /// </summary>
      /// <returns>Stream isi jawaban; menutupnya ikut melepas respons HTTP-nya.</returns>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau server menjawab dengan amplop JSON yang sukses, yang berarti action-nya bukan
      /// action yang mengembalikan isi file.
      /// </exception>
      internal static async Task<Stream> ProcessHttpStreamResult(this Task<HttpResponseMessage> task) {
         var response = await task.ConfigureAwait(false);
         var isEnvelope = string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json",
            StringComparison.OrdinalIgnoreCase);

         if (response.IsSuccessStatusCode && !isEnvelope) {
            try {
               var content = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
               return new ResponseOwningStream(content, response);
            }
            catch {
               response.Dispose();
               throw;
            }
         }

         // Throws the server's own rejection with its status code, and disposes the response.
         await Task.FromResult(response).ProcessHttpResult<JsonElement>().ConfigureAwait(false);
         throw new InvalidOperationException("The server answered with data instead of file content.");
      }

      /// <summary>
      /// Passes reads through to the response content and disposes the response together with it, so the
      /// caller of GetStreamAsync only has one thing to close.
      /// </summary>
      private sealed class ResponseOwningStream(Stream inner, HttpResponseMessage response) : Stream
      {
         public override bool CanRead => inner.CanRead;
         public override bool CanSeek => false;
         public override bool CanWrite => false;
         public override long Length => response.Content.Headers.ContentLength ?? throw new NotSupportedException();

         public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
         }

         public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

         public override int Read(Span<byte> buffer) => inner.Read(buffer);

         public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

         public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

         public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

         public override void Flush() { }

         public override void SetLength(long value) => throw new NotSupportedException();

         public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

         protected override void Dispose(bool disposing) {
            if (disposing) {
               inner.Dispose();
               response.Dispose();
            }

            base.Dispose(disposing);
         }
      }
   }
}