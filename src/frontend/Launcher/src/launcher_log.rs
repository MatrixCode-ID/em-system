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

/// The launcher log in `launcher.log` of the install folder. One log for the whole process: opened once
/// through [`Self::open`], after which every part of the launcher only calls [`Self::info`],
/// [`Self::warn`], or [`Self::error`]. Before it is opened, all writes are ignored, so code that writes
/// the log can still be used in tests without an install folder.
///
/// Once its size passes 1 MB, the file is moved to `launcher.log.1` (overwriting the old backup) and the
/// log starts empty, so at most about 2 MB of log is on disk.
pub struct LauncherLog;

impl LauncherLog {
   // region: Statics

   /// Starts writing the log to `path` (appended at the end of the file). A failure to open the file is not
   /// fatal: the launcher keeps running, only without a log.
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

   /// Stops writing the log and closes its file, for example before the install folder is deleted.
   pub fn close() {
      if let Ok(mut log) = LOG.lock() {
         *log = None;
      }
   }

   /// Mencatat kejadian biasa.
   pub fn info(message: impl Display) {
      Self::write("INFO", message);
   }

   /// Records something unusual that does not stop the work (e.g. an old folder could not be deleted yet).
   pub fn warn(message: impl Display) {
      Self::write("WARN", message);
   }

   /// Mencatat kegagalan.
   pub fn error(message: impl Display) {
      Self::write("ERROR", message);
   }

   /// Records a panic, called from the panic hook before the process stops. Unlike [`Self::error`], it does
   /// not wait for the log: when the panic happens while the log is being written (the log is locked), the
   /// record is skipped rather than let the process hang forever.
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
