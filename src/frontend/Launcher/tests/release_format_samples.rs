// Runs the reader side of the release format against the shared test vectors in doc/release-format-samples,
// with the outcomes listed in doc/release-format.md section 9.

use std::fs;
use std::path::{Path, PathBuf};

use launcher::format::{
   ReleaseHash, ReleaseLayout, ReleaseManifest, ReleasePublicKey, ReleaseSignature, ReleaseSignatureFile,
   SignatureError,
};

fn samples() -> PathBuf {
   Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../doc/release-format-samples")
}

fn trusted_key() -> ReleasePublicKey {
   let pem = fs::read_to_string(samples().join("public-key.pem")).unwrap();
   ReleasePublicKey::from_pem(&pem).unwrap()
}

struct Expected {
   key_id: String,
   files: Vec<(String, u64, String)>,
}

fn expected() -> Expected {
   let text = fs::read_to_string(samples().join("expected.txt")).unwrap();
   let mut key_id = String::new();
   let mut files = Vec::new();
   for line in text.lines().filter(|line| !line.trim().is_empty()) {
      let parts: Vec<&str> = line.split_whitespace().collect();
      match parts.as_slice() {
         ["keyId", id] => key_id = id.to_string(),
         [path, size, sha256] => files.push((path.to_string(), size.parse().unwrap(), sha256.to_string())),
         _ => panic!("unexpected line in expected.txt: {line}"),
      }
   }
   Expected { key_id, files }
}

// Steps 1 to 4 of section 6: read both files, pick the key, verify, and only then parse the manifest.
fn open(folder: &str) -> Result<ReleaseManifest, SignatureError> {
   let root = samples().join(folder);
   let signature = ReleaseSignatureFile::from_bytes(&fs::read(root.join(ReleaseLayout::SIGNATURE_FILE_NAME)).unwrap())
      .expect("the .sig of every sample is well-formed");
   let manifest_bytes = fs::read(root.join(ReleaseLayout::MANIFEST_FILE_NAME)).unwrap();
   ReleaseSignature::verify(&manifest_bytes, &signature, &[trusted_key()])?;
   Ok(ReleaseManifest::from_bytes(&manifest_bytes).expect("a verified sample manifest is valid"))
}

// Step 5: the files whose size or SHA-256 differ from the manifest.
fn mismatched_files(folder: &str, manifest: &ReleaseManifest) -> Vec<String> {
   let binaries = samples().join(folder).join(ReleaseLayout::BINARIES_FOLDER);
   manifest
      .files()
      .iter()
      .filter(|file| {
         let (size, sha256) = ReleaseHash::compute_file(&binaries.join(&file.path), |_| Ok(())).unwrap();
         size != file.size || sha256 != file.sha256
      })
      .map(|file| file.path.clone())
      .collect()
}

#[test]
fn key_id_matches_expected() {
   assert_eq!(trusted_key().key_id(), expected().key_id);
}

#[test]
fn valid_release_is_accepted_and_matches_expected() {
   let manifest = open("valid").expect("the valid sample must verify");
   assert!(mismatched_files("valid", &manifest).is_empty());

   let mut listed: Vec<(String, u64, String)> = manifest
      .files()
      .iter()
      .map(|file| (file.path.clone(), file.size, file.sha256.clone()))
      .collect();
   listed.sort();
   let mut wanted = expected().files;
   wanted.sort();
   assert_eq!(listed, wanted);

   // The actual bytes on disk hash to the same values, independently of the manifest.
   let binaries = samples().join("valid").join(ReleaseLayout::BINARIES_FOLDER);
   for (path, size, sha256) in &wanted {
      assert_eq!(
         &ReleaseHash::compute_file(&binaries.join(path), |_| Ok(())).unwrap(),
         &(*size, sha256.clone())
      );
   }
}

#[test]
fn tampered_manifest_fails_the_signature() {
   assert_eq!(open("tampered-manifest").err(), Some(SignatureError::Invalid));
}

#[test]
fn tampered_file_passes_the_signature_but_fails_its_hash() {
   let manifest = open("tampered-file").expect("the manifest itself is untouched");
   let mismatched = mismatched_files("tampered-file", &manifest);
   assert_eq!(mismatched.len(), 1, "{mismatched:?}");

   let file = manifest.find(&mismatched[0]).unwrap();
   let path = samples()
      .join("tampered-file")
      .join(ReleaseLayout::BINARIES_FOLDER)
      .join(&file.path);
   let (size, sha256) = ReleaseHash::compute_file(&path, |_| Ok(())).unwrap();
   assert_eq!(
      size, file.size,
      "one byte changed, so the size still matches and only the hash tells"
   );
   assert_ne!(sha256, file.sha256);
}

#[test]
fn unknown_key_is_rejected() {
   assert_eq!(open("unknown-key").err(), Some(SignatureError::UnknownKey));
}
