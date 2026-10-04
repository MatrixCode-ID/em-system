use serde::Deserialize;

/// Satu file rilis di dalam manifest: path-nya relatif terhadap folder `binaries/`, ukuran, dan SHA-256
/// isinya. Aturan setiap nilai ada di `doc/release-format.md` bagian 2; pemeriksaannya dilakukan
/// [`super::ReleaseManifest::from_bytes`] untuk seluruh manifest sekaligus.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct ReleaseFile {
   /// Path relatif terhadap `binaries/`, dipisah `/`, sekaligus membawa nama filenya (mis.
   /// `runtimes/win-x64/native/x.dll`). Dibandingkan tanpa memandang huruf besar/kecil.
   pub path: String,

   /// Ukuran file dalam byte.
   pub size: u64,

   /// SHA-256 isi file, 64 karakter hex huruf kecil.
   pub sha256: String,
}

impl ReleaseFile {
   // region: Statics

   /// Alasan `path` tidak sah sebagai path file rilis (`doc/release-format.md` bagian 2.3), atau `None`
   /// kalau sah. Keunikan tidak ikut diperiksa di sini.
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

   /// `true` kalau `hash` berupa 64 karakter hex huruf kecil.
   pub fn is_valid_sha256(hash: &str) -> bool {
      hash.len() == 64 && hash.bytes().all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
   }

   /// Bentuk `path` yang dipakai untuk membandingkan path tanpa memandang huruf besar/kecil, dengan cara
   /// yang sama seperti sistem berkas Windows: setiap karakter diubah ke huruf besar satu per satu, dan
   /// karakter yang huruf besarnya lebih dari satu karakter (mis. `ß`) dibiarkan apa adanya.
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

   /// `true` kalau `other` berisi file yang sama: ukuran dan SHA-256-nya sama. Path tidak ikut dibandingkan.
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
