use std::fs;
use std::io;
use std::os::windows::process::CommandExt;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::thread;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use winsafe::{self as w, co};

use crate::config::RegistryHive;
use crate::launcher_log::LauncherLog;
use crate::shell::ShellIntegration;

use super::InstallLayout;

// CREATE_NO_WINDOW: the helper processes must not flash a console window.
const CREATE_NO_WINDOW: u32 = 0x0800_0000;
const REMOVE_ATTEMPTS: u32 = 10;
const REMOVE_RETRY_DELAY: Duration = Duration::from_millis(500);

/// Mencabut instalasi: folder instalasi, shortcut, entri Apps & Features, dan seluruh key registry app
/// (`HKCU\<ApplicationName>`, termasuk pengaturan app dan konfigurasi launcher).
///
/// Uninstall biasanya dijalankan oleh launcher root di dalam folder instalasi itu sendiri, padahal exe yang
/// sedang berjalan tidak bisa dihapus. Karena itu pekerjaannya dibagi dua proses:
///
/// 1. launcher yang diminta uninstall menyalin dirinya ke `%TEMP%\launcher-uninstall-<acak>\launcher.exe`,
///    menjalankan salinan itu dengan [`Self::FINISH_ARGUMENT`] ([`Self::start_finisher`]), lalu keluar;
/// 2. salinan itu menunggu proses pertama selesai, menghapus semuanya ([`Self::remove`]), lalu menjadwalkan
///    penghapusan foldernya sendiri di `%TEMP%` ([`Self::schedule_removal_of`]).
pub struct Uninstaller<'a> {
   layout: &'a InstallLayout,
}

/// Proses yang exe-nya ada di dalam folder instalasi, misalnya app yang masih terbuka.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RunningProcess {
   /// ID prosesnya.
   pub pid: u32,

   /// Path lengkap exe-nya.
   pub path: PathBuf,
}

impl<'a> Uninstaller<'a> {
   // region: Statics

   /// Argumen internal yang menjalankan tahap kedua uninstall: `--uninstall-finish <install> --parent-pid <pid>`.
   /// Hanya dipakai launcher sendiri, tidak untuk user.
   pub const FINISH_ARGUMENT: &'static str = "--uninstall-finish";

   /// Argumen internal berisi PID launcher yang harus ditunggu selesai oleh tahap kedua.
   pub const PARENT_PID_ARGUMENT: &'static str = "--parent-pid";

   /// Membuat uninstaller untuk instalasi `layout`. Belum ada yang disentuh.
   pub fn new(layout: &'a InstallLayout) -> Self {
      Self { layout }
   }

   /// Menjadwalkan penghapusan `folder` setelah proses ini keluar, lewat proses `cmd` terpisah yang mencoba
   /// menghapusnya kira-kira setiap detik, paling lama sekitar 10 menit. Dipakai salinan launcher di `%TEMP%`
   /// untuk membuang dirinya sendiri; salinan itu masih bisa menunggu user menutup pesan terakhirnya.
   pub fn schedule_removal_of(folder: &Path) -> io::Result<()> {
      let working = folder.parent().unwrap_or(Path::new(r"C:\"));
      let folder = folder.display();
      // Raw argument: cmd /c strips the outer quotes and keeps the quoted paths intact.
      Command::new("cmd.exe")
         .raw_arg(format!(
            "/d /c \"for /l %i in (1,1,600) do @(ping -n 2 127.0.0.1 >nul & rmdir /s /q \"{folder}\" 2>nul & \
             if not exist \"{folder}\" exit)\""
         ))
         .current_dir(working)
         .creation_flags(CREATE_NO_WINDOW)
         .spawn()
         .map(|_| ())
   }

   // endregion

   // region: Methods

   /// Proses lain (selain proses ini) yang exe-nya ada di dalam folder instalasi. Selama daftar ini tidak
   /// kosong, folder instalasi tidak bisa dihapus.
   pub fn running_processes(&self) -> io::Result<Vec<RunningProcess>> {
      let own = w::GetCurrentProcessId();
      let mut snapshot = w::HPROCESSLIST::CreateToolhelp32Snapshot(co::TH32CS::SNAPPROCESS, None).map_err(to_io)?;
      let mut running = Vec::new();
      for entry in snapshot.iter_processes() {
         let pid = entry.map_err(to_io)?.th32ProcessID;
         if pid == own || pid == 0 {
            continue;
         }
         // Processes of other users or protected ones cannot be opened; they cannot run from this folder anyway.
         let Ok(process) = w::HPROCESS::OpenProcess(co::PROCESS::QUERY_LIMITED_INFORMATION, false, pid) else {
            continue;
         };
         let Ok(path) = process.QueryFullProcessImageName(co::PROCESS_NAME::WIN32) else {
            continue;
         };
         let path = PathBuf::from(path);
         if self.layout.contains(&path) {
            running.push(RunningProcess { pid, path });
         }
      }
      Ok(running)
   }

   /// `true` kalau exe proses ini ada di dalam folder instalasi, sehingga uninstall harus lewat salinan di
   /// `%TEMP%` ([`Self::start_finisher`]) dan tidak bisa dikerjakan langsung.
   pub fn runs_from_install_folder(&self) -> bool {
      std::env::current_exe().is_ok_and(|exe| self.layout.contains(&exe))
   }

   /// Menyalin exe proses ini ke folder baru di `%TEMP%` dan menjalankannya sebagai tahap kedua uninstall
   /// (lihat [`Self::FINISH_ARGUMENT`]). Setelah ini proses pemanggil harus segera keluar, karena tahap kedua
   /// menunggunya. `quiet` diteruskan, supaya tahap kedua tidak menampilkan apa pun.
   pub fn start_finisher(&self, quiet: bool) -> io::Result<()> {
      let exe = std::env::current_exe()?;
      let temp = PathBuf::from(w::GetTempPath().map_err(to_io)?);
      let stamp = SystemTime::now()
         .duration_since(UNIX_EPOCH)
         .map_or(0, |elapsed| elapsed.as_nanos());
      let folder = temp.join(format!(
         "launcher-uninstall-{:x}{:x}",
         w::GetCurrentProcessId(),
         stamp & 0xFFFF_FFFF_FFFF
      ));
      fs::create_dir_all(&folder)?;
      let copy = folder.join(InstallLayout::LAUNCHER_FILE_NAME);
      fs::copy(&exe, &copy)?;

      let mut command = Command::new(&copy);
      command
         .arg(Self::FINISH_ARGUMENT)
         .arg(self.layout.root())
         .arg(Self::PARENT_PID_ARGUMENT)
         .arg(w::GetCurrentProcessId().to_string())
         // The finisher must not keep the install folder busy as its working directory.
         .current_dir(&folder)
         .creation_flags(CREATE_NO_WINDOW);
      if quiet {
         command.arg("--quiet");
      }
      let child = command.spawn()?;
      LauncherLog::info(format!(
         "Uninstall continues in {} (pid {})",
         copy.display(),
         child.id()
      ));
      Ok(())
   }

   /// Menghapus instalasi: folder instalasi lebih dulu, lalu jejaknya di Windows (`integration`), lalu key
   /// registry app `app_key` (path di bawah `HKEY_CURRENT_USER`, untuk produk `<ApplicationName>`).
   ///
   /// Kalau folder instalasi tidak bisa dihapus (ada file yang dipakai), pekerjaan berhenti di situ dan
   /// shortcut, entri Apps & Features, serta registry dibiarkan, supaya uninstall bisa diulang. Folder dicoba
   /// beberapa kali dulu, karena antivirus atau indexer sering menahan file sebentar.
   pub fn remove(&self, integration: &ShellIntegration, app_key: &str) -> io::Result<()> {
      LauncherLog::close();
      let root = self.layout.root();
      let mut attempt = 0;
      loop {
         match fs::remove_dir_all(root) {
            Ok(()) => break,
            Err(error) if error.kind() == io::ErrorKind::NotFound => break,
            Err(error) => {
               attempt += 1;
               if attempt >= REMOVE_ATTEMPTS {
                  return Err(io::Error::new(
                     error.kind(),
                     format!("cannot remove {}: {error}", root.display()),
                  ));
               }
               thread::sleep(REMOVE_RETRY_DELAY);
            }
         }
      }

      let integration_result = integration.remove(self.layout);
      RegistryHive::CurrentUser.delete_tree(app_key)?;
      integration_result
   }

   // endregion
}

fn to_io(error: co::ERROR) -> io::Error {
   io::Error::from_raw_os_error(error.raw() as i32)
}
