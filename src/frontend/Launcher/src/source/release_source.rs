use std::io::Read;

use super::{FolderSource, HttpSource, SourceError};

/// Tempat folder rilis dibaca: CDN lewat HTTP(S) ([`HttpSource`]) atau folder lokal/jaringan
/// ([`FolderSource`]). Updater hanya bicara dengan trait ini, jadi ia tidak tahu dan tidak peduli dari
/// mana byte-nya datang; test juga bisa memasang sumber buatan sendiri.
///
/// Sumber hanya mengantar byte. Memeriksa tanda tangan, ukuran, dan hash adalah tugas pemanggil
/// (`doc/release-format.md` bagian 6).
pub trait ReleaseSource: Send + Sync {
   /// Alamat folder rilis seperti diisi user, untuk log dan pesan.
   fn address(&self) -> &str;

   /// Membaca seluruh isi file kecil di akar folder rilis, yaitu `release.json` atau `release.json.sig`.
   fn read_file(&self, name: &str) -> Result<Vec<u8>, SourceError>;

   /// Membuka file rilis `path` (relatif terhadap `binaries/`, dipisah `/`) mulai dari byte ke-`offset`,
   /// untuk melanjutkan unduhan yang terputus. Sumber boleh mengabaikan `offset` dan mengirim file dari
   /// awal; [`SourceStream::offset`] memberi tahu mana yang terjadi.
   fn open_binary(&self, path: &str, offset: u64) -> Result<SourceStream, SourceError>;
}

/// Isi file rilis yang sedang dibaca dari sumber, hasil [`ReleaseSource::open_binary`].
pub struct SourceStream {
   /// Posisi byte pertama yang dikirim `reader`: sama dengan `offset` yang diminta, atau `0` kalau sumber
   /// tidak bisa melanjutkan dan mengirim file dari awal. Pemanggil harus membuang byte yang sudah ia punya
   /// kalau nilainya `0`.
   pub offset: u64,

   /// Isi file mulai dari [`Self::offset`] sampai habis.
   pub reader: Box<dyn Read + Send>,
}

/// Membuat sumber untuk alamat folder rilis yang diisi user: alamat berawalan `http://` atau `https://`
/// dibaca lewat [`HttpSource`], selain itu dianggap path folder (lokal atau UNC) dan dibaca lewat
/// [`FolderSource`].
pub fn from_address(address: &str) -> Result<Box<dyn ReleaseSource>, SourceError> {
   let address = address.trim();
   if address.is_empty() {
      return Err(SourceError::unavailable("the release source is empty"));
   }

   let lower = address.to_ascii_lowercase();
   if lower.starts_with("http://") || lower.starts_with("https://") {
      Ok(Box::new(HttpSource::new(address)))
   } else {
      Ok(Box::new(FolderSource::new(address)))
   }
}
