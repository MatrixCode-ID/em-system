use std::error::Error;
use std::fmt::{self, Display, Formatter};
use std::io;

/// Kesalahan saat membaca folder rilis dari sumbernya (CDN atau folder). Bedanya dengan kesalahan format:
/// di sini isi rilisnya belum sempat diperiksa, sumbernya sendiri yang tidak bisa dibaca.
#[derive(Debug)]
pub struct SourceError {
   kind: SourceErrorKind,
   message: String,
}

/// Jenis [`SourceError`].
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SourceErrorKind {
   /// Sumbernya bisa dihubungi, tetapi file yang diminta tidak ada (HTTP 404, file tidak ditemukan).
   NotFound,

   /// Sumbernya tidak bisa dibaca: server mati, jaringan putus, timeout, akses ditolak, atau server menjawab
   /// dengan status yang tidak diharapkan. Biasanya sementara.
   Unavailable,
}

impl SourceError {
   // region: Statics

   /// Kesalahan "file tidak ada" untuk `what` (nama file atau alamat lengkapnya).
   pub fn not_found(what: impl Display) -> Self {
      Self {
         kind: SourceErrorKind::NotFound,
         message: format!("{what} was not found"),
      }
   }

   /// Kesalahan "sumber tidak bisa dibaca" dengan pesan penjelasnya.
   pub fn unavailable(message: impl Into<String>) -> Self {
      Self {
         kind: SourceErrorKind::Unavailable,
         message: message.into(),
      }
   }

   /// Menerjemahkan kesalahan I/O saat membaca `what`: file yang tidak ada menjadi
   /// [`SourceErrorKind::NotFound`], selain itu [`SourceErrorKind::Unavailable`].
   pub fn from_io(what: impl Display, error: &io::Error) -> Self {
      match error.kind() {
         io::ErrorKind::NotFound => Self::not_found(what),
         _ => Self::unavailable(format!("cannot read {what}: {error}")),
      }
   }

   // endregion

   // region: Properties

   /// Jenis kesalahannya.
   pub fn kind(&self) -> SourceErrorKind {
      self.kind
   }

   /// Pesan kesalahannya, dalam bahasa Inggris.
   pub fn message(&self) -> &str {
      &self.message
   }

   // endregion
}

impl Display for SourceError {
   fn fmt(&self, f: &mut Formatter<'_>) -> fmt::Result {
      f.write_str(&self.message)
   }
}

impl Error for SourceError {}
