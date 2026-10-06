namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Kunci satu proses antara garbage collection dan request yang menautkan blob (selesai upload, mount).
   /// Dipegang sesingkat mungkin: GC per blob, upload dari pemindahan berkas sampai tautan tersimpan.
   /// Mengandaikan satu instance API per folder storage registry.
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
