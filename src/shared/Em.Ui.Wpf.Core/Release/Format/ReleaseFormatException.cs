namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// Thrown when <c>release.json</c> or <c>release.json.sig</c> does not conform to
   /// <c>doc/release-format.md</c>: its JSON is corrupt, a required field is missing, or one of its values
   /// breaks a rule (path, size, hash).
   /// </summary>
   public sealed class ReleaseFormatException(string message, Exception? inner = null) : Exception(message, inner);
}
