namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// The fixed names inside a desktop client release folder. The source of truth is
   /// <c>doc/release-format.md</c> section 1; if this code and that document differ, the document is right.
   /// </summary>
   public static class ReleaseLayout
   {
      /// <summary>The subfolder holding the client files, exactly as in the installation folder.</summary>
      public const string BinariesFolder = "binaries";

      /// <summary>The name of the manifest file: the list of files in <see cref="BinariesFolder"/> with their sizes and hashes.</summary>
      public const string ManifestFileName = "release.json";

      /// <summary>The name of the signature file over the manifest bytes.</summary>
      public const string SignatureFileName = "release.json.sig";

      /// <summary>The default release folder name, used as long as the user does not change it.</summary>
      public const string DefaultReleaseFolder = "wpf-release";
   }
}
