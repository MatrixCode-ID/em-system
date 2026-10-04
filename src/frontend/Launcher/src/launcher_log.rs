use std::fmt::Display;
use std::fs::{self, File, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::{Mutex, MutexGuard, TryLockError};

const MAX_LOG_SIZE: u64 = 1024 * 1024;

// One log per process; None until open() is called, and then every write goes nowhere. Tests never open it.
static LOG: Mutex<Option<LogFile>> = Mutex::new(None);

struct LogFile {
   path: PathBuf,
   file: File,
}

/// Log launcher di `launcher.log` folder instalasi. Satu log untuk seluruh proses: dibuka sekali lewat
/// [`Self::open`], lalu setiap bagian launcher cukup memanggil [`Self::info`], [`Self::warn`], atau
/// [`Self::error`]. Sebelum dibuka, semua tulisan diabaikan, jadi kode yang menulis log tetap bisa dipakai
/// di test tanpa folder instalasi.
///
/// Begitu ukurannya melewati 1 MB, file dipindah menjadi `launcher.log.1` (menimpa cadangan lama) dan log
/// dimulai dari kosong, sehingga di disk paling banyak ada sekitar 2 MB log.
pub struct LauncherLog;

impl LauncherLog {
   // region: Statics

   /// Mulai menulis log ke `path` (ditambahkan di akhir file). Kegagalan membuka file tidak dianggap fatal:
   /// launcher tetap berjalan, hanya tanpa log.
   pub fn open(path: &Path) {
      if let Some(parent) = path.parent() {
         let _ = fs::create_dir_all(parent);
      }
      let Ok(file) = OpenOptions::new().create(true).append(true).open(path) else {
         return;
      };
      if let Ok(mut log) = LOG.lock() {
         *log = Some(LogFile {
            path: path.to_path_buf(),
            file,
         });
      }
   }

   /// Berhenti menulis log dan menutup file-nya, misalnya sebelum folder instalasi dihapus.
   pub fn close() {
      if let Ok(mut log) = LOG.lock() {
         *log = None;
      }
   }

   /// Mencatat kejadian biasa.
   pub fn info(message: impl Display) {
      Self::write("INFO", message);
   }

   /// Mencatat hal yang tidak wajar tapi tidak menghentikan pekerjaan (mis. folder lama belum bisa dihapus).
   pub fn warn(message: impl Display) {
      Self::write("WARN", message);
   }

   /// Mencatat kegagalan.
   pub fn error(message: impl Display) {
      Self::write("ERROR", message);
   }

   /// Mencatat panic, dipanggil dari panic hook sebelum proses berhenti. Berbeda dengan [`Self::error`], log
   /// tidak ditunggu: kalau panic terjadi saat log sedang ditulis (log terkunci), catatannya dilewati daripada
   /// proses macet selamanya.
   pub fn panic(message: impl Display) {
      let guard = match LOG.try_lock() {
         Ok(guard) => guard,
         // A panic while writing poisons the lock; the file itself is still usable.
         Err(TryLockError::Poisoned(poisoned)) => poisoned.into_inner(),
         Err(TryLockError::WouldBlock) => return,
      };
      Self::write_locked(guard, "PANIC", message);
   }

   // endregion
}

impl LauncherLog {
   fn write(level: &str, message: impl Display) {
      let Ok(guard) = LOG.lock() else {
         return;
      };
      Self::write_locked(guard, level, message);
   }

   fn write_locked(mut guard: MutexGuard<'_, Option<LogFile>>, level: &str, message: impl Display) {
      let Some(log) = guard.as_mut() else {
         return;
      };

      if log.file.metadata().is_ok_and(|m| m.len() >= MAX_LOG_SIZE) {
         log.rotate();
      }
      let now = winsafe::GetLocalTime();
      let _ = write!(
         log.file,
         "{:04}-{:02}-{:02} {:02}:{:02}:{:02}.{:03} [{level}] {message}\r\n",
         now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond, now.wMilliseconds
      );
   }
}

impl LogFile {
   fn rotate(&mut self) {
      let mut backup = self.path.clone().into_os_string();
      backup.push(".1");
      // std opens files with FILE_SHARE_DELETE, so the log can be renamed while its handle is still open.
      let _ = fs::rename(&self.path, &backup);
      if let Ok(file) = OpenOptions::new().create(true).append(true).open(&self.path) {
         self.file = file;
      }
   }
}
