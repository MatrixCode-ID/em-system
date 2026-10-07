/// The fixed names inside a release folder (`doc/release-format.md` section 1). The counterpart of
/// `ReleaseLayout` in C#; when this code and that document differ, the document is right.
pub struct ReleaseLayout;

impl ReleaseLayout {
   /// The subfolder holding the client files, exactly as in the install folder.
   pub const BINARIES_FOLDER: &str = "binaries";

   /// The manifest file name: the list of files in [`Self::BINARIES_FOLDER`] with their sizes and hashes.
   pub const MANIFEST_FILE_NAME: &str = "release.json";

   /// The name of the signature file over the manifest bytes.
   pub const SIGNATURE_FILE_NAME: &str = "release.json.sig";
}
