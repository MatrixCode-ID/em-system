use std::io;
use std::time::Duration;

use windows::Win32::Foundation::{CloseHandle, HANDLE, WAIT_ABANDONED, WAIT_OBJECT_0, WAIT_TIMEOUT};
use windows::Win32::System::Threading::{CreateMutexW, ReleaseMutex, WaitForSingleObject};
use windows::core::HSTRING;

use crate::product;

/// Kunci "satu launcher yang mengubah instalasi per produk per user", lewat mutex bernama Windows.
///
/// Install, update, Repair, dan uninstall memegang kunci ini selama bekerja, supaya dua launcher (misalnya
/// shortcut diklik dua kali) tidak mengisi folder instalasi yang sama bersamaan. Kunci dilepas saat nilai ini
/// di-drop, atau otomatis oleh Windows saat prosesnya berhenti.
#[derive(Debug)]
pub struct LauncherLock {
   handle: HANDLE,
}

impl LauncherLock {
   // region: Statics

   /// Nama mutex milik produk ini, di namespace sesi login (`Local\`).
   pub fn product_name() -> String {
      format!(r"Local\{}.Launcher", product::APP_ID)
   }

   /// Mengambil kunci bernama `name`, menunggu paling lama `timeout` kalau sedang dipegang launcher lain.
   /// `Ok(None)` kalau waktunya habis.
   pub fn acquire(name: &str, timeout: Duration) -> io::Result<Option<Self>> {
      let handle = unsafe { CreateMutexW(None, false, &HSTRING::from(name)) }.map_err(io::Error::other)?;
      let milliseconds = u32::try_from(timeout.as_millis()).unwrap_or(u32::MAX - 1);
      let result = unsafe { WaitForSingleObject(handle, milliseconds) };
      // WAIT_ABANDONED: the previous owner died without releasing it; the lock is ours now all the same.
      if result == WAIT_OBJECT_0 || result == WAIT_ABANDONED {
         return Ok(Some(Self { handle }));
      }
      unsafe {
         let _ = CloseHandle(handle);
      }
      if result == WAIT_TIMEOUT {
         Ok(None)
      } else {
         Err(io::Error::last_os_error())
      }
   }

   // endregion
}

impl Drop for LauncherLock {
   fn drop(&mut self) {
      unsafe {
         let _ = ReleaseMutex(self.handle);
         let _ = CloseHandle(self.handle);
      }
   }
}

#[cfg(test)]
mod tests {
   use super::*;

   #[test]
   fn only_one_holder_at_a_time() {
      let name = format!(r"Local\LauncherLockTest-{}", std::process::id());
      let first = LauncherLock::acquire(&name, Duration::ZERO).unwrap();
      assert!(first.is_some());

      // A mutex is owned by a thread, so the second attempt has to come from another one.
      let busy = std::thread::scope(|scope| {
         scope
            .spawn(|| {
               LauncherLock::acquire(&name, Duration::from_millis(50))
                  .unwrap()
                  .is_none()
            })
            .join()
            .unwrap()
      });
      assert!(busy);

      drop(first);
      let again = std::thread::scope(|scope| {
         scope
            .spawn(|| LauncherLock::acquire(&name, Duration::ZERO).unwrap().is_some())
            .join()
            .unwrap()
      });
      assert!(again);
   }
}
