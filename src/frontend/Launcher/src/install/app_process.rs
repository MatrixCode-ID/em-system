use std::ffi::OsStr;
use std::io;
use std::process::Command;
use std::time::Duration;

use winsafe::{self as w, co};

use crate::launcher_log::LauncherLog;
use crate::product;

use super::InstallLayout;

/// Menjalankan app versi aktif sesuai kontrak launcher → app, dan menunggu proses lain selesai.
///
/// Kontraknya: app dijalankan dari `app-<id>\` sebagai working directory, dengan environment variable
/// [`Self::PATH_VARIABLE`] (path launcher root) dan [`Self::APP_ID_VARIABLE`] (AppUserModelID produk).
/// Dari keduanya app tahu bahwa ia dijalankan launcher, jadi ia tidak mengalihkan dirinya lagi ke launcher,
/// dan jendelanya bisa di-pin ke taskbar atas nama launcher.
pub struct AppProcess;

impl AppProcess {
   // region: Statics

   /// Nama environment variable berisi path lengkap `<install>\launcher.exe`.
   pub const PATH_VARIABLE: &str = "LAUNCHER_PATH";

   /// Nama environment variable berisi AppUserModelID produk.
   pub const APP_ID_VARIABLE: &str = "LAUNCHER_APP_ID";

   /// Menyiapkan perintah untuk menjalankan app versi aktif dengan argumen `args` (diteruskan apa adanya),
   /// tanpa menjalankannya. Gagal kalau belum ada versi aktif atau exe app-nya tidak ada.
   pub fn command<I, S>(layout: &InstallLayout, args: I) -> io::Result<Command>
   where
      I: IntoIterator<Item = S>,
      S: AsRef<OsStr>,
   {
      let current = layout
         .read_current()?
         .ok_or_else(|| io::Error::new(io::ErrorKind::NotFound, "there is no active version"))?;
      let folder = layout.version_folder(&current.version);
      let exe = folder.join(product::APP_EXE);
      if !exe.is_file() {
         return Err(io::Error::new(
            io::ErrorKind::NotFound,
            format!("{} does not exist", exe.display()),
         ));
      }

      let mut command = Command::new(&exe);
      command
         .args(args)
         .current_dir(&folder)
         .env(Self::PATH_VARIABLE, layout.launcher_path())
         .env(Self::APP_ID_VARIABLE, product::APP_ID);
      Ok(command)
   }

   /// Menjalankan app versi aktif (lihat [`Self::command`]) tanpa menunggunya, lalu mengembalikan PID-nya.
   /// Launcher boleh langsung keluar setelah ini.
   pub fn start<I, S>(layout: &InstallLayout, args: I) -> io::Result<u32>
   where
      I: IntoIterator<Item = S>,
      S: AsRef<OsStr>,
   {
      let mut command = Self::command(layout, args)?;
      let child = command.spawn()?;
      LauncherLog::info(format!(
         "Started {} (pid {})",
         command.get_program().to_string_lossy(),
         child.id()
      ));
      Ok(child.id())
   }

   /// Menunggu proses `pid` selesai, paling lama `timeout` (`None` = tanpa batas). `Ok(true)` kalau prosesnya
   /// sudah selesai atau memang tidak ada, `Ok(false)` kalau waktunya habis. Dipakai `--apply --pid`, yang
   /// menunggu app menutup diri sebelum memasang update.
   pub fn wait_for_exit(pid: u32, timeout: Option<Duration>) -> io::Result<bool> {
      let process = match w::HPROCESS::OpenProcess(co::PROCESS::SYNCHRONIZE, false, pid) {
         Ok(process) => process,
         // Windows answers an unknown (or already reaped) pid with ERROR_INVALID_PARAMETER.
         Err(co::ERROR::INVALID_PARAMETER) => return Ok(true),
         Err(error) => return Err(io::Error::from_raw_os_error(error.raw() as i32)),
      };
      let milliseconds = timeout.map(|timeout| u32::try_from(timeout.as_millis()).unwrap_or(u32::MAX - 1));
      match process.WaitForSingleObject(milliseconds) {
         Ok(co::WAIT::TIMEOUT) => Ok(false),
         Ok(_) => Ok(true),
         Err(error) => Err(io::Error::from_raw_os_error(error.raw() as i32)),
      }
   }

   // endregion
}
