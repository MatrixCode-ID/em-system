namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Muatan yang diterima <see cref="DropTarget"/> saat file atau folder dijatuhkan dari luar
   /// aplikasi, misalnya dari Windows Explorer. Isinya path lokal apa adanya - file maupun folder -
   /// dan command penerimanya yang memutuskan mau diapakan.
   /// </summary>
   public sealed class DroppedFiles
   {
      /// <summary>
      /// Membungkus daftar path hasil jatuhan.
      /// </summary>
      /// <param name="paths">Path lokal file atau folder yang dijatuhkan.</param>
      public DroppedFiles(IReadOnlyList<string> paths) {
         Paths = paths;
      }

      /// <summary>Path lokal file atau folder yang dijatuhkan, sesuai urutan dari sumbernya.</summary>
      public IReadOnlyList<string> Paths { get; }
   }
}
