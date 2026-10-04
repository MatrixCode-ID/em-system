namespace Em.Ui.Maui.Shared
{
   /// <summary>
   /// Argumen event yang bisa membatalkan (cancel) sebuah aksi yang sedang berjalan, dipakai
   /// mis. pada event <c>UiCommandBase.CommandExecuting</c> untuk membatalkan eksekusi command.
   /// </summary>
   public class CancelEventArgs : EventArgs
   {
      /// <summary>
      /// Instance kosong dengan nilai default (tidak dibatalkan), untuk kemudahan pemakaian.
      /// </summary>
      public static new CancelEventArgs Empty => new();

      /// <summary>
      /// Set <c>true</c> oleh listener untuk membatalkan aksi yang sedang diproses.
      /// </summary>
      public bool Cancel { get; set; }

      /// <summary>
      /// Deskripsi alasan pembatalan, dipakai sebagai pesan default jika <see cref="ThrowException"/> aktif
      /// tanpa <see cref="ExceptionToThrow"/> yang eksplisit.
      /// </summary>
      public string Description { get; set; } = "";

      /// <summary>
      /// Data tambahan bebas yang bisa dititipkan listener ke pemroses event.
      /// </summary>
      public object? Tag { get; set; }

      /// <summary>
      /// Jika <c>true</c>, pemroses event akan melempar exception (lihat <see cref="ExceptionToThrow"/>)
      /// alih-alih hanya membatalkan aksi secara diam-diam.
      /// </summary>
      public bool ThrowException { get; set; }

      /// <summary>
      /// Exception spesifik yang akan dilempar jika <see cref="ThrowException"/> bernilai <c>true</c>.
      /// Jika <c>null</c>, dipakai <see cref="OperationCanceledException"/> dengan pesan dari <see cref="Description"/>.
      /// </summary>
      public Exception? ExceptionToThrow { get; set; }
   }
}
