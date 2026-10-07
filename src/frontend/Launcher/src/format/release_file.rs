use serde::Deserialize;

/// One release file inside the manifest: its path relative to the `binaries/` folder, its size, and the
/// SHA-256 of its content. The rules for each value are in `doc/release-format.md` section 2; they are
/// checked by [`super::ReleaseManifest::from_bytes`] for the whole manifest at once.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct ReleaseFile {
   /// The path relative to `binaries/`, separated by `/`, which also carries the file name (e.g.
   /// `runtimes/win-x64/native/x.dll`). Compared case-insensitively.
   pub path: String,

   /// The file size in bytes.
   pub size: u64,

   /// The SHA-256 of the file content, 64 lowercase hex characters.
   pub sha256: String,
}

impl ReleaseFile {
   // region: Statics

   /// The reason `path` is not valid as a release file path (`doc/release-format.md` section 2.3), or `None`
   /// when it is valid. Uniqueness is not checked here.
   pub fn check_path(path: &str) -> Option<String> {
      if path.is_empty() {
         return Some("the path is empty".into());
      }
      if path.contains('\\') {
         return Some("the path contains '\\'; use '/'".into());
      }
      if path.starts_with('/') {
         return Some("the path is absolute".into());
      }
      if path.contains(':') {
         return Some("the path contains ':'".into());
      }

      for segment in path.split('/') {
         if segment.is_empty() {
            return Some("the path has an empty segment".into());
         }
         if segment.starts_with('.') {
            return Some(format!("the segment '{segment}' starts with '.'"));
         }
      }

      None
   }

   /// `true` when `hash` is 64 lowercase hex characters.
   pub fn is_valid_sha256(hash: &str) -> bool {
      hash.len() == 64 && hash.bytes().all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
   }

   /// The form of `path` used to compare paths case-insensitively the way the Windows file system does it:
   /// each character is uppercased one at a time, and a character whose uppercase is more than one character
   /// (e.g. `ß`) is left as it is.
   pub fn path_key(path: &str) -> String {
      path
         .chars()
         .map(|c| {
            let mut upper = c.to_uppercase();
            match (upper.next(), upper.next()) {
               (Some(single), None) => single,
               _ => c,
            }
         })
         .collect()
   }

   // endregion

   // region: Methods

   /// `true` when `other` holds the same file: the same size and SHA-256. The path is not compared.
   pub fn has_same_content(&self, other: &ReleaseFile) -> bool {
      self.size == other.size && self.sha256 == other.sha256
   }

   // endregion
}

#[cfg(test)]
mod tests {
   use super::*;

   #[test]
   fn accepts_valid_paths() {
      for path in [
         "a.dll",
         "runtimes/win-x64/native/x.dll",
         "a+b.dll",
         "folder.name/file",
         "x..y",
      ] {
         assert_eq!(ReleaseFile::check_path(path), None, "{path}");
      }
   }

   #[test]
   fn rejects_invalid_paths() {
      for path in [
         "", "a\\b.dll", "/a.dll", "C:/a.dll", "a:b", "a//b", "a/", ".", "..", "../a.dll", "a/../b", "a/./b",
         ".hidden", "a/.git/b",
      ] {
         assert!(ReleaseFile::check_path(path).is_some(), "{path:?} should be rejected");
      }
   }

   #[test]
   fn checks_sha256_shape() {
      let good = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
      assert!(ReleaseFile::is_valid_sha256(good));
      assert!(!ReleaseFile::is_valid_sha256(&good.to_uppercase()));
      assert!(!ReleaseFile::is_valid_sha256(&good[1..]));
      assert!(!ReleaseFile::is_valid_sha256(&format!("{good}0")));
      assert!(!ReleaseFile::is_valid_sha256(&good.replace('e', "g")));
   }

   #[test]
   fn path_key_ignores_case() {
      assert_eq!(
         ReleaseFile::path_key("Sample.App.dll"),
         ReleaseFile::path_key("SAMPLE.app.DLL")
      );
      assert_eq!(ReleaseFile::path_key("straße"), "STRAßE");
   }
}
