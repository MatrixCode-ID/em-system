use std::sync::Mutex;
use std::sync::atomic::{AtomicBool, AtomicU8, AtomicU64, Ordering};
use std::time::Instant;

/// The progress of the updater's work that other threads (the progress window, the CLI) read while it is
/// running, and also the channel to cancel it.
///
/// It is shared through `Arc<UpdateProgress>`: the worker thread writes, the UI thread reads about every
/// 200 ms. The numbers are atomic, so reading them never waits for the worker; only the file name uses a
/// `Mutex`, because a `String` cannot be replaced atomically.
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

/// The phase of the updater's work. [`UpdateProgress::total_bytes`] and [`UpdateProgress::done_bytes`]
/// apply to the phase that is running: bytes for Verifying/Preparing, units for Finishing/Removing.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum UpdatePhase {
   /// Not started yet.
   Idle = 0,
   /// Fetching and verifying `release.json`. The size is not known yet (show a marquee).
   Checking,
   /// Checking the size and hash of the files that are already installed (Repair).
   Verifying,
   /// Preparing the files of the new version: copying the identical ones from the active version, downloading
   /// the rest.
   Preparing,
   /// Moving the new version into place and cleaning up old leftovers, counted in steps and files.
   Finishing,
   /// Removing the installation, counted in steps and files. Cancellation is disabled.
   Removing,
}

impl UpdateProgress {
   // region: Statics

   /// New progress in phase [`UpdatePhase::Idle`].
   pub fn new() -> Self {
      Self::default()
   }

   // endregion

   // region: Properties

   /// The phase that is running.
   pub fn phase(&self) -> UpdatePhase {
      match self.phase.load(Ordering::Relaxed) {
         1 => UpdatePhase::Checking,
         2 => UpdatePhase::Verifying,
         3 => UpdatePhase::Preparing,
         4 => UpdatePhase::Finishing,
         5 => UpdatePhase::Removing,
         _ => UpdatePhase::Idle,
      }
   }

   /// Bytes to process, or steps/files in Finishing/Removing; `0` means unknown.
   pub fn total_bytes(&self) -> u64 {
      self.total_bytes.load(Ordering::Relaxed)
   }

   /// Bytes processed, or steps/files completed in Finishing/Removing.
   pub fn done_bytes(&self) -> u64 {
      self.done_bytes.load(Ordering::Relaxed)
   }

   /// The number of bytes actually received from the source since the work started, without local copies.
   pub fn transferred_bytes(&self) -> u64 {
      self.transferred_bytes.load(Ordering::Relaxed)
   }

   /// The path of the release file being worked on, or empty.
   pub fn current_file(&self) -> String {
      self.current_file.lock().map(|file| file.clone()).unwrap_or_default()
   }

   /// The average download speed since the work started, in bytes per second.
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

   /// `true` when cancellation has been requested.
   pub fn is_cancelled(&self) -> bool {
      self.cancelled.load(Ordering::Relaxed)
   }

   // endregion

   // region: Methods

   /// Asks the work to stop as soon as possible. The updater checks it between chunks of data, then stops
   /// with a "cancelled" error; files that were already downloaded are kept to be resumed later.
   pub fn cancel(&self) {
      if !self.can_cancel() {
         return;
      }
      self.cancelled.store(true, Ordering::Relaxed);
   }

   /// Whether cancellation is allowed in the current phase.
   pub fn can_cancel(&self) -> bool {
      !matches!(self.phase(), UpdatePhase::Finishing | UpdatePhase::Removing)
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

#[cfg(test)]
mod tests {
   use super::*;

   #[test]
   fn commit_and_removal_cannot_be_cancelled() {
      let progress = UpdateProgress::new();
      for phase in [UpdatePhase::Finishing, UpdatePhase::Removing] {
         progress.begin(phase, 3);
         assert!(!progress.can_cancel());
         progress.cancel();
         assert!(!progress.is_cancelled());
      }
      progress.begin(UpdatePhase::Preparing, 100);
      progress.cancel();
      assert!(progress.is_cancelled());
   }
}
