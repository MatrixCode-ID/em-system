namespace Em.Ui.Wpf.Core
{
   /// <summary>
   /// Keterangan berakhirnya sebuah sesi. Dipakai layar login untuk membedakan sesi yang memang
   /// diakhiri user dari sesi yang mati sendiri - yang kedua perlu satu kalimat penjelasan, yang
   /// pertama tidak.
   /// </summary>
   /// <param name="reason">
   /// Alasan sesi berakhir, atau <c>null</c> kalau user sendiri yang keluar. Kalimat ini ditampilkan
   /// sebagai keterangan biasa, bukan sebagai pesan kesalahan: sesi yang habis umurnya bukan
   /// kesalahan user.
   /// </param>
   public sealed class SessionEndedEventArgs(string? reason) : EventArgs
   {
      /// <inheritdoc cref="SessionEndedEventArgs(string?)" path="/param[@name='reason']" />
      public string? Reason { get; } = reason;
   }
}
