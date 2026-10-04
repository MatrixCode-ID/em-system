namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// Dilempar saat <c>release.json</c> atau <c>release.json.sig</c> tidak sesuai
   /// <c>doc/release-format.md</c>: JSON-nya rusak, field wajibnya tidak ada, atau salah satu nilainya
   /// melanggar aturan (path, ukuran, hash).
   /// </summary>
   public sealed class ReleaseFormatException(string message, Exception? inner = null) : Exception(message, inner);
}
