namespace Em.Api.Core.Registry
{
   /// <summary>
   /// In-process lock between garbage collection and requests that link a blob (upload completion, mount).
   /// Held as briefly as possible: GC per blob, upload from moving the file until the link is saved.
   /// Assumes one API instance per registry storage folder.
   /// </summary>
   internal static class CtnBlobGate
   {
      private static readonly SemaphoreSlim Gate = new(1, 1);

      public static async Task<IDisposable> EnterAsync(CancellationToken ct) {
         await Gate.WaitAsync(ct);
         return new Releaser();
      }

      private sealed class Releaser : IDisposable
      {
         private int _released;

         public void Dispose() {
            if (Interlocked.Exchange(ref _released, 1) == 0) Gate.Release();
         }
      }
   }
}
