use std::error::Error;
use std::fmt::{self, Display, Formatter};
use std::io;
use std::path::Path;

use crate::format::{ReleaseFormatError, SignatureError};
use crate::source::SourceError;

/// Alasan install, update, atau repair tidak selesai. Apa pun alasannya, versi aktif tidak berubah:
/// versi baru baru dipakai setelah semua filenya lolos pemeriksaan.
#[derive(Debug)]
pub enum UpdateError {
   /// Sumber rilis tidak bisa dibaca, atau `release.json`/`.sig` tidak ada di sana.
   Source(SourceError),

   /// Tanda tangan `release.json` ditolak (langkah 2–3 `doc/release-format.md` bagian 6).
   Signature(SignatureError),

   /// `release.json` atau `.sig` tidak sesuai format (langkah 4).
   Format(ReleaseFormatError),

   /// Ukuran atau SHA-256 file dari sumber tidak sama dengan manifest (langkah 5). Biasanya rilis sedang
   /// di-Sync ulang di server; update ini dibatalkan dan dicoba lagi nanti (bagian 7).
   ServerChanged {
      /// Path file rilis yang tidak cocok.
      path: String,
   },

   /// Gagal membaca atau menulis di folder instalasi.
   Io {
      /// Apa yang sedang dikerjakan saat gagal, dalam bahasa Inggris.
      action: String,
      /// Kesalahan aslinya.
      error: io::Error,
   },

   /// Dibatalkan lewat [`super::UpdateProgress::cancel`].
   Cancelled,
}

impl UpdateError {
   /// Membuat pembungkus kesalahan I/O untuk `map_err`, dengan keterangan apa yang sedang dikerjakan pada
   /// `path`, misalnya `.map_err(UpdateError::io("create", &folder))`.
   pub fn io(action: &str, path: &Path) -> impl FnOnce(io::Error) -> UpdateError {
      let action = format!("cannot {action} {}", path.display());
      move |error| UpdateError::Io { action, error }
   }
}

impl Display for UpdateError {
   fn fmt(&self, f: &mut Formatter<'_>) -> fmt::Result {
      match self {
         Self::Source(error) => write!(f, "{error}"),
         Self::Signature(error) => write!(f, "{error}"),
         Self::Format(error) => write!(f, "{error}"),
         Self::ServerChanged { path } => write!(
            f,
            "'{path}' does not match the release manifest; the release changed on the server, try again later"
         ),
         Self::Io { action, error } => write!(f, "{action}: {error}"),
         Self::Cancelled => f.write_str("cancelled"),
      }
   }
}

impl Error for UpdateError {}

impl From<SourceError> for UpdateError {
   fn from(error: SourceError) -> Self {
      Self::Source(error)
   }
}

impl From<SignatureError> for UpdateError {
   fn from(error: SignatureError) -> Self {
      Self::Signature(error)
   }
}

impl From<ReleaseFormatError> for UpdateError {
   fn from(error: ReleaseFormatError) -> Self {
      Self::Format(error)
   }
}
