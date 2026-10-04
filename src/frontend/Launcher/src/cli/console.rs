use std::fmt::Display;
use std::io::Write;

use std::fs::OpenOptions;
use std::os::windows::io::IntoRawHandle;

use windows::Win32::Foundation::HANDLE;
use windows::Win32::System::Console::{
   ATTACH_PARENT_PROCESS, AttachConsole, GetStdHandle, STD_ERROR_HANDLE, STD_HANDLE, STD_OUTPUT_HANDLE, SetStdHandle,
};

/// Keluaran teks launcher ke console tempat ia dijalankan.
///
/// Build release bersubsistem `windows` (tanpa jendela console yang berkedip saat dibuka dari shortcut), jadi
/// tidak punya console sendiri. [`Self::attach`] menumpang ke console proses induk (cmd, PowerShell), kalau
/// ada, supaya perintah CLI tetap bisa menulis hasilnya. Tanpa console induk, tulisan diabaikan.
///
/// Karena exe-nya bukan aplikasi console, cmd dan PowerShell tidak menunggunya selesai. Untuk membaca exit
/// code, jalankan dengan `start /wait launcher.exe ...` (cmd) atau `Start-Process -Wait -PassThru` (PowerShell).
pub struct Console;

impl Console {
   // region: Statics

   /// Menumpang ke console proses induk. `true` kalau berhasil, `false` kalau tidak ada console induk (dibuka
   /// dari shortcut atau Explorer) atau proses ini sudah punya console sendiri (build debug).
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

   /// Menulis satu baris ke stdout.
   pub fn out(message: impl Display) {
      let _ = writeln!(std::io::stdout(), "{message}");
   }

   /// Menulis satu baris ke stderr.
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
