use std::error::Error;
use std::fmt::{self, Display, Formatter};

/// Kesalahan saat `release.json` atau `release.json.sig` tidak sesuai `doc/release-format.md`: JSON-nya
/// rusak, field wajibnya tidak ada, atau salah satu nilainya melanggar aturan (path, ukuran, hash, waktu).
/// Pasangan `ReleaseFormatException` di C#.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ReleaseFormatError {
   message: String,
}

impl ReleaseFormatError {
   /// Membuat kesalahan dengan pesan (bahasa Inggris) yang menjelaskan aturan mana yang dilanggar.
   pub fn new(message: impl Into<String>) -> Self {
      Self {
         message: message.into(),
      }
   }

   /// Pesan kesalahannya.
   pub fn message(&self) -> &str {
      &self.message
   }
}

impl Display for ReleaseFormatError {
   fn fmt(&self, f: &mut Formatter<'_>) -> fmt::Result {
      f.write_str(&self.message)
   }
}

impl Error for ReleaseFormatError {}
