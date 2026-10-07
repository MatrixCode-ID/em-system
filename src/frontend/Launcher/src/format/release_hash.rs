use std::fs::File;
use std::io::{self, Read};
use std::path::Path;

use sha2::{Digest, Sha256};

const BUFFER_SIZE: usize = 81920;

/// Computes the size and SHA-256 of a release file's content (`doc/release-format.md` section 2.4) while
/// reading it once from start to end, so neither a big file nor a download that is still streaming has to
/// be held in memory. The counterpart of `ReleaseHash` in C#.
pub struct ReleaseHash;

impl ReleaseHash {
   /// Reads `content` to the end and returns its number of bytes together with its SHA-256 in lowercase hex.
   ///
   /// `progress` is called every time some data has been read, with the number of bytes read from `content`
   /// so far. When `progress` returns `Err`, reading stops and that error is passed on; this way the caller
   /// can cancel the hash midway.
   pub fn compute(
      content: &mut impl Read,
      mut progress: impl FnMut(u64) -> io::Result<()>,
   ) -> io::Result<(u64, String)> {
      let mut hash = Sha256::new();
      let mut buffer = vec![0u8; BUFFER_SIZE];
      let mut size = 0u64;
      loop {
         let read = match content.read(&mut buffer) {
            Ok(0) => break,
            Ok(read) => read,
            Err(x) if x.kind() == io::ErrorKind::Interrupted => continue,
            Err(x) => return Err(x),
         };
         hash.update(&buffer[..read]);
         size += read as u64;
         progress(size)?;
      }

      Ok((size, to_hex(&hash.finalize())))
   }

   /// Computes the size and SHA-256 of a file on disk (see [`Self::compute`]).
   pub fn compute_file(path: &Path, progress: impl FnMut(u64) -> io::Result<()>) -> io::Result<(u64, String)> {
      let mut file = File::open(path)?;
      Self::compute(&mut file, progress)
   }

   /// The SHA-256 of a block of bytes in memory, as 64 lowercase hex characters.
   pub fn sha256_hex(bytes: &[u8]) -> String {
      to_hex(&Sha256::digest(bytes))
   }
}

fn to_hex(bytes: &[u8]) -> String {
   const DIGITS: &[u8; 16] = b"0123456789abcdef";
   let mut text = String::with_capacity(bytes.len() * 2);
   for byte in bytes {
      text.push(DIGITS[(byte >> 4) as usize] as char);
      text.push(DIGITS[(byte & 0x0f) as usize] as char);
   }
   text
}

#[cfg(test)]
mod tests {
   use super::*;

   const EMPTY_SHA256: &str = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

   #[test]
   fn hashes_empty_content() {
      let (size, hash) = ReleaseHash::compute(&mut io::empty(), |_| Ok(())).unwrap();
      assert_eq!(size, 0);
      assert_eq!(hash, EMPTY_SHA256);
      assert_eq!(ReleaseHash::sha256_hex(b""), EMPTY_SHA256);
   }

   #[test]
   fn reports_progress_and_can_be_stopped() {
      let data = vec![7u8; BUFFER_SIZE * 2 + 5];

      let mut reported = Vec::new();
      let (size, hash) = ReleaseHash::compute(&mut data.as_slice(), |read| {
         reported.push(read);
         Ok(())
      })
      .unwrap();
      assert_eq!(size, data.len() as u64);
      assert_eq!(hash, ReleaseHash::sha256_hex(&data));
      assert_eq!(reported.last(), Some(&(data.len() as u64)));

      let stopped = ReleaseHash::compute(&mut data.as_slice(), |_| Err(io::ErrorKind::Interrupted.into()));
      assert!(stopped.is_err());
   }
}
