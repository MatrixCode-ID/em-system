namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Parameter command <see cref="DropTarget"/> untuk tempat jatuhan yang menyebut sasarannya lewat
   /// <c>DropTarget.Target</c>: muatannya sekaligus di mana ia dijatuhkan. Dipakai kalau satu command
   /// melayani banyak tempat jatuhan - misalnya setiap baris folder di sebuah daftar - dan perlu tahu
   /// baris mana yang dituju.
   /// </summary>
   public sealed class DropRequest
   {
      /// <summary>
      /// Menyusun permintaan jatuhan.
      /// </summary>
      /// <param name="payload">Muatan yang dijatuhkan.</param>
      /// <param name="target">Sasaran jatuhan, yaitu nilai <c>DropTarget.Target</c> pada elemennya.</param>
      public DropRequest(object payload, object target) {
         Payload = payload;
         Target = target;
      }

      /// <summary>
      /// Muatan yang dijatuhkan: muatan <see cref="DragSource"/>, atau <see cref="DroppedFiles"/> untuk
      /// file dari luar aplikasi.
      /// </summary>
      public object Payload { get; }

      /// <summary>Sasaran jatuhan, yaitu nilai <c>DropTarget.Target</c> pada elemen penerimanya.</summary>
      public object Target { get; }
   }
}
