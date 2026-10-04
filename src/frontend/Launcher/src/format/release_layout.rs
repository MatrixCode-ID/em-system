/// Nama-nama tetap di dalam sebuah folder rilis (`doc/release-format.md` bagian 1). Pasangan
/// `ReleaseLayout` di C#; kalau kode ini dan dokumen itu berbeda, dokumennya yang benar.
pub struct ReleaseLayout;

impl ReleaseLayout {
   /// Subfolder berisi file client, persis seperti di folder instalasi.
   pub const BINARIES_FOLDER: &str = "binaries";

   /// Nama file manifest: daftar file di [`Self::BINARIES_FOLDER`] beserta ukuran dan hash-nya.
   pub const MANIFEST_FILE_NAME: &str = "release.json";

   /// Nama file tanda tangan atas byte manifest.
   pub const SIGNATURE_FILE_NAME: &str = "release.json.sig";
}
