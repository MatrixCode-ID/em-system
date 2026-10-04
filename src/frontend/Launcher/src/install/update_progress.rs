use std::sync::Mutex;
use std::sync::atomic::{AtomicBool, AtomicU8, AtomicU64, Ordering};
use std::time::Instant;

/// Kemajuan pekerjaan updater yang dibaca thread lain (jendela progres, CLI) sambil pekerjaannya berjalan,
/// sekaligus saluran untuk membatalkannya.
///
/// Dibagikan lewat `Arc<UpdateProgress>`: thread pekerja menulis, thread UI membaca kira-kira setiap
/// 200 ms. Angka-angkanya atomik, jadi membacanya tidak pernah menunggu pekerja; hanya nama file yang
/// memakai `Mutex`, karena `String` tidak bisa diganti secara atomik.
#[derive(Debug, Default)]
pub struct UpdateProgress {
   phase: AtomicU8,
   total_bytes: AtomicU64,
   done_bytes: AtomicU64,
   transferred_bytes: AtomicU64,
   current_file: Mutex<String>,
   started: Mutex<Option<Instant>>,
   cancelled: AtomicBool,
}

/// Tahap pekerjaan updater. [`UpdateProgress::total_bytes`] dan [`UpdateProgress::done_bytes`] berlaku
/// untuk tahap yang sedang berjalan.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum UpdatePhase {
   /// Belum mulai.
   Idle = 0,
   /// Mengambil dan memverifikasi `release.json`. Ukurannya belum diketahui (tampilkan marquee).
   Checking,
   /// Memeriksa ukuran dan hash file yang sudah terpasang (Repair).
   Verifying,
   /// Menyiapkan file versi baru: menyalin yang sama dari versi aktif, mengunduh sisanya.
   Preparing,
   /// Memindahkan versi baru ke tempatnya dan membersihkan sisa lama. Ukurannya tidak diketahui.
   Finishing,
}

impl UpdateProgress {
   // region: Statics

   /// Kemajuan baru di tahap [`UpdatePhase::Idle`].
   pub fn new() -> Self {
      Self::default()
   }

   // endregion

   // region: Properties

   /// Tahap yang sedang berjalan.
   pub fn phase(&self) -> UpdatePhase {
      match self.phase.load(Ordering::Relaxed) {
         1 => UpdatePhase::Checking,
         2 => UpdatePhase::Verifying,
         3 => UpdatePhase::Preparing,
         4 => UpdatePhase::Finishing,
         _ => UpdatePhase::Idle,
      }
   }

   /// Jumlah byte yang dikerjakan di tahap ini, atau `0` kalau belum diketahui.
   pub fn total_bytes(&self) -> u64 {
      self.total_bytes.load(Ordering::Relaxed)
   }

   /// Jumlah byte yang sudah selesai di tahap ini (disalin, diunduh, atau di-hash).
   pub fn done_bytes(&self) -> u64 {
      self.done_bytes.load(Ordering::Relaxed)
   }

   /// Jumlah byte yang benar-benar diterima dari sumber sejak pekerjaan dimulai, tanpa salinan lokal.
   pub fn transferred_bytes(&self) -> u64 {
      self.transferred_bytes.load(Ordering::Relaxed)
   }

   /// Path file rilis yang sedang dikerjakan, atau kosong.
   pub fn current_file(&self) -> String {
      self.current_file.lock().map(|file| file.clone()).unwrap_or_default()
   }

   /// Rata-rata kecepatan unduhan sejak pekerjaan dimulai, dalam byte per detik.
   pub fn bytes_per_second(&self) -> u64 {
      let Some(started) = self.started.lock().ok().and_then(|started| *started) else {
         return 0;
      };
      let seconds = started.elapsed().as_secs_f64();
      if seconds < 0.5 {
         return 0;
      }
      (self.transferred_bytes() as f64 / seconds) as u64
   }

   /// `true` kalau pembatalan sudah diminta.
   pub fn is_cancelled(&self) -> bool {
      self.cancelled.load(Ordering::Relaxed)
   }

   // endregion

   // region: Methods

   /// Meminta pekerjaan berhenti secepatnya. Updater memeriksanya di antara potongan data, lalu berhenti
   /// dengan kesalahan "dibatalkan"; file yang sudah diunduh tetap disimpan untuk dilanjutkan nanti.
   pub fn cancel(&self) {
      self.cancelled.store(true, Ordering::Relaxed);
   }

   pub(crate) fn begin(&self, phase: UpdatePhase, total_bytes: u64) {
      if let Ok(mut started) = self.started.lock() {
         started.get_or_insert_with(Instant::now);
      }
      self.total_bytes.store(total_bytes, Ordering::Relaxed);
      self.done_bytes.store(0, Ordering::Relaxed);
      self.phase.store(phase as u8, Ordering::Relaxed);
      self.set_file("");
   }

   pub(crate) fn set_file(&self, path: &str) {
      if let Ok(mut file) = self.current_file.lock() {
         file.clear();
         file.push_str(path);
      }
   }

   pub(crate) fn add_done(&self, bytes: u64) {
      self.done_bytes.fetch_add(bytes, Ordering::Relaxed);
   }

   pub(crate) fn remove_done(&self, bytes: u64) {
      self.done_bytes.fetch_sub(bytes, Ordering::Relaxed);
   }

   pub(crate) fn add_transferred(&self, bytes: u64) {
      self.transferred_bytes.fetch_add(bytes, Ordering::Relaxed);
   }

   // endregion
}
