namespace Em.Shared
{
   /// <summary>
   /// Argumen event yang membawa informasi <see cref="System.Exception"/> yang terjadi,
   /// dipakai pada event-event penanganan error (mis. exception handler global di UI).
   /// </summary>
   public class ExceptionEventArgs : EventArgs
   {
      /// <summary>
      /// Membuat argumen event baru dari sebuah exception.
      /// </summary>
      /// <param name="exception">Exception yang terjadi. Tidak boleh <c>null</c>.</param>
      /// <exception cref="ArgumentNullException">Dilempar jika <paramref name="exception"/> <c>null</c>.</exception>
      public ExceptionEventArgs(Exception exception) {
         RaisedException = exception ?? throw new ArgumentNullException(nameof(exception));
      }

      /// <summary>
      /// Exception asli yang menyebabkan event ini dilempar.
      /// </summary>
      public Exception RaisedException { get; }

      /// <summary>
      /// Pesan singkat dari <see cref="RaisedException"/>, untuk kemudahan ditampilkan ke user/log.
      /// </summary>
      public string ExceptionMessage => RaisedException.Message;
   }
}
