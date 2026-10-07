use std::fmt::Display;
use std::io::Write;

use std::fs::OpenOptions;
use std::os::windows::io::IntoRawHandle;

use windows::Win32::Foundation::HANDLE;
use windows::Win32::System::Console::{
   ATTACH_PARENT_PROCESS, AttachConsole, GetStdHandle, STD_ERROR_HANDLE, STD_HANDLE, STD_OUTPUT_HANDLE, SetStdHandle,
};

/// The launcher's text output to the console it runs in.
///
/// The release build has the `windows` subsystem (no console window flashing when opened from a shortcut),
/// so it has no console of its own. [`Self::attach`] latches onto the parent process's console (cmd,
/// PowerShell), if there is one, so CLI commands can still write their results. Without a parent console,
/// the text is ignored.
///
/// Because the exe is not a console application, cmd and PowerShell do not wait for it to finish. To read
/// the exit code, run it with `start /wait launcher.exe ...` (cmd) or `Start-Process -Wait -PassThru`
/// (PowerShell).
pub struct Console;

impl Console {
   // region: Statics

   /// Latches onto the parent process's console. `true` on success, `false` when there is no parent console
   /// (opened from a shortcut or Explorer) or this process already has a console of its own (debug build).
   pub fn attach() -> bool {
      let attached = unsafe { AttachConsole(ATTACH_PARENT_PROCESS) }.is_ok();
      if attached {
         connect(STD_OUTPUT_HANDLE);
         connect(STD_ERROR_HANDLE);
         // The shell already printed its prompt; start on a fresh line below it.
         Self::out("");
      }
      attached
   }

   /// Writes one line to stdout.
   pub fn out(message: impl Display) {
      let _ = writeln!(std::io::stdout(), "{message}");
   }

   /// Writes one line to stderr.
   pub fn err(message: impl Display) {
      let _ = writeln!(std::io::stderr(), "{message}");
   }

   // endregion
}

// A GUI process started from a console without redirection may have no standard handles even after attaching;
// std looks the handle up on every write, so pointing it at the console once is enough. Redirected handles
// (launcher.exe ... > file) are kept.
fn connect(which: STD_HANDLE) {
   let current = unsafe { GetStdHandle(which) }.unwrap_or_default();
   if !current.is_invalid() && !current.0.is_null() {
      return;
   }
   if let Ok(console) = OpenOptions::new().read(true).write(true).open("CONOUT$") {
      // The handle stays open for the life of the process, as a standard handle should.
      let _ = unsafe { SetStdHandle(which, HANDLE(console.into_raw_handle())) };
   }
}
