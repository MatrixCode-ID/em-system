use std::collections::HashMap;

use serde::Deserialize;

use super::{ReleaseFile, ReleaseFormatError};

/// Isi `release.json`: kapan rilis diterbitkan dan file apa saja yang menjadi bagiannya
/// (`doc/release-format.md` bagian 2). Nilai bertipe ini selalu sudah lolos semua aturan format, karena
/// satu-satunya cara membuatnya adalah [`Self::from_bytes`].
#[derive(Debug, Clone)]
pub struct ReleaseManifest {
   published_at_utc: String,
   files: Vec<ReleaseFile>,
   index: HashMap<String, usize>,
}

// The wire shape only; ReleaseManifest itself is built through from_bytes, so nothing unvalidated escapes.
#[derive(Deserialize)]
struct ManifestJson {
   #[serde(rename = "publishedAtUtc")]
   published_at_utc: String,
   files: Vec<ReleaseFile>,
}

impl ReleaseManifest {
   // region: Statics

   /// Membaca byte `release.json` dan memeriksa setiap aturan format: waktu terbit, path (termasuk unik
   /// tanpa memandang huruf besar/kecil), ukuran, dan hash. Field yang tidak dikenal diabaikan.
   ///
   /// Byte yang diterima dari luar harus sudah lolos [`super::ReleaseSignature::verify`] lebih dulu
   /// (`doc/release-format.md` bagian 6).
   pub fn from_bytes(bytes: &[u8]) -> Result<Self, ReleaseFormatError> {
      let json: ManifestJson = serde_json::from_slice(bytes)
         .map_err(|x| ReleaseFormatError::new(format!("release.json is not valid: {x}")))?;

      if !is_utc_timestamp(&json.published_at_utc) {
         return Err(ReleaseFormatError::new(format!(
            "'publishedAtUtc' is not an ISO 8601 UTC time: '{}'.",
            json.published_at_utc
         )));
      }

      let mut index = HashMap::with_capacity(json.files.len());
      for (position, file) in json.files.iter().enumerate() {
         if let Some(reason) = ReleaseFile::check_path(&file.path) {
            return Err(ReleaseFormatError::new(format!(
               "Invalid path '{}': {reason}.",
               file.path
            )));
         }
         if index.insert(ReleaseFile::path_key(&file.path), position).is_some() {
            return Err(ReleaseFormatError::new(format!(
               "The path '{}' is listed more than once (ignoring case).",
               file.path
            )));
         }
         if !ReleaseFile::is_valid_sha256(&file.sha256) {
            return Err(ReleaseFormatError::new(format!(
               "'{}' has an invalid sha256 '{}'.",
               file.path, file.sha256
            )));
         }
      }

      Ok(Self {
         published_at_utc: json.published_at_utc,
         files: json.files,
         index,
      })
   }

   // endregion

   // region: Properties

   /// Waktu rilis diterbitkan, persis seperti tertulis (ISO 8601 UTC dengan akhiran `Z`). Hanya informasi.
   pub fn published_at_utc(&self) -> &str {
      &self.published_at_utc
   }

   /// Seluruh file rilis, dalam urutan manifest.
   pub fn files(&self) -> &[ReleaseFile] {
      &self.files
   }

   /// Jumlah ukuran seluruh file rilis, dalam byte.
   pub fn total_size(&self) -> u64 {
      self.files.iter().map(|file| file.size).sum()
   }

   // endregion

   // region: Methods

   /// Mencari file berdasarkan path-nya, tanpa memandang huruf besar/kecil. `None` kalau tidak tercantum.
   pub fn find(&self, path: &str) -> Option<&ReleaseFile> {
      self
         .index
         .get(&ReleaseFile::path_key(path))
         .map(|&position| &self.files[position])
   }

   // endregion
}

// Accepts yyyy-MM-ddTHH:mm:ss, an optional fraction of a second, then a mandatory 'Z'.
fn is_utc_timestamp(text: &str) -> bool {
   let Some(body) = text.strip_suffix('Z') else {
      return false;
   };
   let (main, fraction) = match body.split_once('.') {
      Some((main, fraction)) => (main, Some(fraction)),
      None => (body, None),
   };
   if fraction.is_some_and(|f| f.is_empty() || !f.bytes().all(|b| b.is_ascii_digit())) {
      return false;
   }

   let bytes = main.as_bytes();
   if bytes.len() != 19
      || bytes[4] != b'-'
      || bytes[7] != b'-'
      || bytes[10] != b'T'
      || bytes[13] != b':'
      || bytes[16] != b':'
   {
      return false;
   }
   let number = |from: usize, to: usize| {
      main
         .get(from..to)
         .filter(|s| s.bytes().all(|b| b.is_ascii_digit()))?
         .parse::<u32>()
         .ok()
   };
   let (Some(year), Some(month), Some(day), Some(hour), Some(minute), Some(second)) = (
      number(0, 4),
      number(5, 7),
      number(8, 10),
      number(11, 13),
      number(14, 16),
      number(17, 19),
   ) else {
      return false;
   };

   let leap = year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
   let days_in_month = match month {
      1 | 3 | 5 | 7 | 8 | 10 | 12 => 31,
      4 | 6 | 9 | 11 => 30,
      2 if leap => 29,
      2 => 28,
      _ => return false,
   };
   (1..=days_in_month).contains(&day) && hour < 24 && minute < 60 && second < 60
}

#[cfg(test)]
mod tests {
   use super::*;

   const HASH: &str = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

   fn manifest(files: &str) -> String {
      format!(r#"{{ "publishedAtUtc": "2026-09-27T10:15:00Z", "files": [{files}] }}"#)
   }

   fn file(path: &str) -> String {
      format!(r#"{{ "path": "{path}", "size": 1, "sha256": "{HASH}" }}"#)
   }

   #[test]
   fn reads_a_valid_manifest_and_ignores_unknown_fields() {
      let json = format!(
         r#"{{ "publishedAtUtc": "2026-09-27T10:15:00.123Z", "extra": {{ "a": 1 }}, "files": [
            {{ "path": "Sample.App.dll", "size": 45, "sha256": "{HASH}", "note": "x" }},
            {{ "path": "runtimes/win-x64/x.dll", "size": 0, "sha256": "{HASH}" }} ] }}"#
      );
      let manifest = ReleaseManifest::from_bytes(json.as_bytes()).unwrap();
      assert_eq!(manifest.files().len(), 2);
      assert_eq!(manifest.total_size(), 45);
      assert_eq!(manifest.find("SAMPLE.APP.DLL").map(|f| f.size), Some(45));
      assert!(manifest.find("missing.dll").is_none());
   }

   #[test]
   fn accepts_an_empty_file_list() {
      assert!(ReleaseManifest::from_bytes(manifest("").as_bytes()).is_ok());
   }

   #[test]
   fn rejects_invalid_paths() {
      for path in ["", "..", "../a.dll", "a/../b", "a\\\\b", "/a", "C:/a", ".git/x", "a//b"] {
         let json = manifest(&file(path));
         assert!(
            ReleaseManifest::from_bytes(json.as_bytes()).is_err(),
            "{path:?} should be rejected"
         );
      }
   }

   #[test]
   fn rejects_duplicates_that_differ_only_in_case() {
      let json = manifest(&format!("{},{}", file("Lib/A.dll"), file("lib/a.DLL")));
      let error = ReleaseManifest::from_bytes(json.as_bytes()).unwrap_err();
      assert!(error.message().contains("more than once"), "{error}");
   }

   #[test]
   fn rejects_bad_hashes_sizes_and_shapes() {
      let bad = [
         manifest(&format!(
            r#"{{ "path": "a", "size": 1, "sha256": "{}" }}"#,
            HASH.to_uppercase()
         )),
         manifest(&format!(r#"{{ "path": "a", "size": 1, "sha256": "{}" }}"#, &HASH[2..])),
         manifest(&format!(r#"{{ "path": "a", "size": -1, "sha256": "{HASH}" }}"#)),
         manifest(&format!(r#"{{ "path": "a", "size": 1.5, "sha256": "{HASH}" }}"#)),
         manifest(&format!(r#"{{ "path": "a", "sha256": "{HASH}" }}"#)),
         r#"{ "files": [] }"#.to_string(),
         r#"{ "publishedAtUtc": "2026-09-27T10:15:00Z" }"#.to_string(),
         r#"[]"#.to_string(),
         "not json".to_string(),
      ];
      for json in bad {
         assert!(
            ReleaseManifest::from_bytes(json.as_bytes()).is_err(),
            "{json} should be rejected"
         );
      }
   }

   #[test]
   fn checks_the_publish_time() {
      for good in ["2026-09-27T10:15:00Z", "2024-02-29T23:59:59.5Z"] {
         assert!(is_utc_timestamp(good), "{good}");
      }
      for bad in [
         "2026-09-27T10:15:00",
         "2026-09-27T10:15:00+07:00",
         "2026-09-27 10:15:00Z",
         "2026-13-01T00:00:00Z",
         "2025-02-29T00:00:00Z",
         "2026-09-27T24:00:00Z",
         "2026-09-27T10:15:00.Z",
         "2026-9-27T10:15:00Z",
      ] {
         assert!(!is_utc_timestamp(bad), "{bad}");
      }
   }
}
