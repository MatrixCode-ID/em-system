namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// Nama-nama tetap di dalam sebuah folder rilis client desktop. Sumber kebenarannya adalah
   /// <c>doc/release-format.md</c> bagian 1; kalau kode ini dan dokumen itu berbeda, dokumennya yang benar.
   /// </summary>
   public static class ReleaseLayout
   {
      /// <summary>Subfolder berisi file client, persis seperti di folder instalasi.</summary>
      public const string BinariesFolder = "binaries";

      /// <summary>Nama file manifest: daftar file di <see cref="BinariesFolder"/> beserta ukuran dan hash-nya.</summary>
      public const string ManifestFileName = "release.json";

      /// <summary>Nama file tanda tangan atas byte manifest.</summary>
      public const string SignatureFileName = "release.json.sig";

      /// <summary>Nama folder rilis bawaan, dipakai selama user tidak menggantinya.</summary>
      public const string DefaultReleaseFolder = "wpf-release";
   }
}
