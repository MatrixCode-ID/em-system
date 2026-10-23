use std::ffi::OsStr;
use std::io;
use std::process::Command;
use std::time::Duration;

use winsafe::{self as w, co};

use crate::launcher_log::LauncherLog;
use crate::product;

use super::InstallLayout;

/// Runs the app of the active version according to the launcher → app contract, and waits for another
/// process to finish.
///
/// The contract: the app is run with `app-<id>\` as its working directory, with the environment variables
/// [`Self::PATH_VARIABLE`] (the path of the root launcher) and [`Self::APP_ID_VARIABLE`] (the product's
/// AppUserModelID). From these two the app knows it was run by the launcher, so it does not redirect
/// itself to the launcher again, and its window can be pinned to the taskbar on behalf of the launcher.
pub struct AppProcess;

impl AppProcess {
   // region: Statics

   /// The name of the environment variable holding the full path `<install>\launcher.exe`.
   pub const PATH_VARIABLE: &str = "LAUNCHER_PATH";

   /// The name of the environment variable holding the product's AppUserModelID.
   pub const APP_ID_VARIABLE: &str = "LAUNCHER_APP_ID";

   /// Prepares the command that runs the app of the active version with arguments `args` (passed on as they
   /// are), without running it. Fails when there is no active version yet or the app's exe is missing.
   pub fn command<I, S>(layout: &InstallLayout, args: I) -> io::Result<Command>
   where
      I: IntoIterator<Item = S>,
      S: AsRef<OsStr>,
   {
      layout
         .read_current()?
         .ok_or_else(|| io::Error::new(io::ErrorKind::NotFound, "there is no active version"))?;
      let folder = layout.installed_app_folder();
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

   /// Runs the app of the active version (see [`Self::command`]) without waiting for it, then returns its
   /// PID. The launcher may exit right after this.
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

   /// Waits for process `pid` to finish, at most `timeout` (`None` = no limit). `Ok(true)` when the process
   /// has finished or did not exist, `Ok(false)` when the time ran out. Used by `--apply --pid`, which waits
   /// for the app to close itself before installing an update.
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

   /// The PIDs of the app's exe running from the install folder. The process that started this launcher is
   /// left out: an app opened directly from its folder hands over to the launcher and exits right after.
   pub fn running(layout: &InstallLayout) -> io::Result<Vec<u32>> {
      let own = w::GetCurrentProcessId();
      let mut snapshot = w::HPROCESSLIST::CreateToolhelp32Snapshot(co::TH32CS::SNAPPROCESS, None).map_err(to_io)?;
      let mut processes = Vec::new();
      for entry in snapshot.iter_processes() {
         let entry = entry.map_err(to_io)?;
         processes.push((entry.th32ProcessID, entry.th32ParentProcessID));
      }
      let parent = processes.iter().find(|(pid, _)| *pid == own).map(|(_, parent)| *parent);

      let mut running = Vec::new();
      for (pid, _) in processes {
         if pid == own || pid == 0 || Some(pid) == parent {
            continue;
         }
         // Processes of other users or protected ones cannot be opened; they cannot run from this folder anyway.
         let Ok(process) = w::HPROCESS::OpenProcess(co::PROCESS::QUERY_LIMITED_INFORMATION, false, pid) else {
            continue;
         };
         let Ok(path) = process.QueryFullProcessImageName(co::PROCESS_NAME::WIN32) else {
            continue;
         };
         let path = std::path::PathBuf::from(path);
         if layout.contains(&path)
            && path
               .file_name()
               .is_some_and(|name| name.eq_ignore_ascii_case(product::APP_EXE))
         {
            running.push(pid);
         }
      }
      Ok(running)
   }

   /// Brings the main window of process `pid` to the front, restoring it when it is minimized. `false` when
   /// the process has no visible window with a title.
   pub fn activate(pid: u32) -> bool {
      let mut found = None;
      let _ = w::EnumWindows(|hwnd: w::HWND| {
         if hwnd.GetWindowThreadProcessId().1 == pid
            && hwnd.IsWindowVisible()
            && hwnd.GetWindowText().is_ok_and(|title| !title.is_empty())
         {
            found = Some(hwnd);
            return false;
         }
         true
      });
      let Some(hwnd) = found else {
         return false;
      };
      if hwnd.IsIconic() {
         hwnd.ShowWindow(co::SW::RESTORE);
      }
      hwnd.SetForegroundWindow()
   }

   // endregion
}

fn to_io(error: co::ERROR) -> io::Error {
   io::Error::from_raw_os_error(error.raw() as i32)
}
