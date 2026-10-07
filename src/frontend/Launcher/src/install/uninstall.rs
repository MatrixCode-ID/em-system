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

/// Removes the installation: the install folder, the shortcuts, the Apps & Features entry, and the app's
/// whole registry key (`HKCU\<ApplicationName>`, including the app's settings and the launcher
/// configuration).
///
/// Uninstall is usually run by the root launcher inside the install folder itself, yet a running exe
/// cannot be deleted. So the work is split across two processes:
///
/// 1. the launcher asked to uninstall copies itself to `%TEMP%\launcher-uninstall-<random>\launcher.exe`,
///    runs that copy with [`Self::FINISH_ARGUMENT`] ([`Self::start_finisher`]), then exits;
/// 2. that copy waits for the first process to finish, deletes everything ([`Self::remove`]), then
///    schedules the deletion of its own folder in `%TEMP%` ([`Self::schedule_removal_of`]).
pub struct Uninstaller<'a> {
   layout: &'a InstallLayout,
}

/// A process whose exe is inside the install folder, for example an app that is still open.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RunningProcess {
   /// ID prosesnya.
   pub pid: u32,

   /// Path lengkap exe-nya.
   pub path: PathBuf,
}

impl<'a> Uninstaller<'a> {
   // region: Statics

   /// The internal argument that runs the second stage of uninstall:
   /// `--uninstall-finish <install> --parent-pid <pid>`. Only used by the launcher itself, not by users.
   pub const FINISH_ARGUMENT: &'static str = "--uninstall-finish";

   /// The internal argument holding the PID of the launcher that the second stage must wait to finish.
   pub const PARENT_PID_ARGUMENT: &'static str = "--parent-pid";

   /// Creates an uninstaller for installation `layout`. Nothing is touched yet.
   pub fn new(layout: &'a InstallLayout) -> Self {
      Self { layout }
   }

   /// Schedules the deletion of `folder` after this process exits, through a separate `cmd` process that
   /// tries to delete it about every second, for about 10 minutes at most. Used by the launcher copy in
   /// `%TEMP%` to remove itself; that copy may still be waiting for the user to close its last message.
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

   /// Other processes (not this one) whose exe is inside the install folder. While this list is not empty,
   /// the install folder cannot be deleted.
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

   /// `true` when this process's exe is inside the install folder, so uninstall has to go through a copy in
   /// `%TEMP%` ([`Self::start_finisher`]) and cannot be done directly.
   pub fn runs_from_install_folder(&self) -> bool {
      std::env::current_exe().is_ok_and(|exe| self.layout.contains(&exe))
   }

   /// Copies this process's exe to a new folder in `%TEMP%` and runs it as the second stage of uninstall (see
   /// [`Self::FINISH_ARGUMENT`]). After this the calling process must exit right away, because the second
   /// stage waits for it. `quiet` is passed on, so the second stage shows nothing.
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

   /// Deletes the installation: the install folder first, then its traces in Windows (`integration`), then
   /// the app's registry key `app_key` (a path under `HKEY_CURRENT_USER`, `<ApplicationName>` for the
   /// product).
   ///
   /// When the install folder cannot be deleted (a file is in use), the work stops there and the shortcuts,
   /// the Apps & Features entry, and the registry are left alone, so uninstall can be repeated. The folder is
   /// tried several times first, because antivirus or an indexer often holds a file for a moment.
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
