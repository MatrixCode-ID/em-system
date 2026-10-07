use std::io;
use std::time::Duration;

use windows::Win32::Foundation::{CloseHandle, HANDLE, WAIT_ABANDONED, WAIT_OBJECT_0, WAIT_TIMEOUT};
use windows::Win32::System::Threading::{CreateMutexW, ReleaseMutex, WaitForSingleObject};
use windows::core::HSTRING;

use crate::product;

/// The lock "one launcher changing the installation per product per user", through a named Windows
/// mutex.
///
/// Install, update, Repair, and uninstall hold this lock while they work, so two launchers (for example a
/// shortcut clicked twice) do not fill the same install folder at once. The lock is released when this
/// value is dropped, or automatically by Windows when the process stops.
#[derive(Debug)]
pub struct LauncherLock {
   handle: HANDLE,
}

impl LauncherLock {
   // region: Statics

   /// The name of this product's mutex, in the login session namespace (`Local\`).
   pub fn product_name() -> String {
      format!(r"Local\{}.Launcher", product::APP_ID)
   }

   /// Takes the lock named `name`, waiting at most `timeout` when another launcher holds it. `Ok(None)` when
   /// the time ran out.
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
