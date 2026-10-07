using System.Text.Json;
using Em.Shared;

namespace Em.Ui.Core
{
   /// <summary>Extension methods for reading API responses.</summary>
   public static class Extensions
   {
      /// <summary>
      /// Reads the HTTP response of an API action as an <see cref="ActionResult"/>, then returns its
      /// <see cref="ActionResult.Data"/> as <typeparamref name="T"/>. Used so callers only need to deal with
      /// the result value, without repeating the status/error check at every call site.
      /// </summary>
      /// <typeparam name="T">The data type expected from the action.</typeparam>
      /// <param name="task">The HTTP call task to be awaited.</param>
      /// <returns>
      /// The action's result data, or <c>default</c> when the action returns no data
      /// (<see cref="ActionResult.HasData"/> is <c>false</c>).
      /// </returns>
      /// <exception cref="InvalidOperationException">
      /// Thrown when the response body cannot be read as an <see cref="ActionResult"/>, or when the server
      /// marks the result invalid.
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
      /// Reads the HTTP response of an action that returns file content. A successful answer is returned as a
      /// stream read directly from the network; a failed answer - which is still an <see cref="ActionResult"/>
      /// envelope - is thrown exactly like <see cref="ProcessHttpResult{T}"/>.
      /// </summary>
      /// <returns>The stream of the answer content; closing it also releases its HTTP response.</returns>
      /// <exception cref="InvalidOperationException">
      /// Thrown when the server answers with a successful JSON envelope, which means the action is not one
      /// that returns file content.
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