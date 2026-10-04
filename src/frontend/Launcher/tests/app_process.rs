// Starting the active version with the launcher -> app contract, and waiting for another process to exit.

use std::ffi::OsStr;
use std::fs;
use std::process::{Command, Stdio};
use std::time::Duration;

use launcher::install::{AppProcess, CurrentVersion, InstallLayout};
use launcher::product;

fn env<'a>(command: &'a Command, name: &str) -> Option<&'a OsStr> {
   command
      .get_envs()
      .find(|(key, _)| *key == OsStr::new(name))
      .and_then(|(_, value)| value)
}

#[test]
fn builds_the_command_with_the_contract_environment() {
   let temp = tempfile::tempdir().unwrap();
   let layout = InstallLayout::new(temp.path());
   assert!(AppProcess::command(&layout, ["x"]).is_err(), "no active version yet");

   layout
      .write_current(&CurrentVersion {
         version: "0123456789ab".into(),
         published_at_utc: "2026-09-27T10:15:00Z".into(),
      })
      .unwrap();
   let folder = layout.version_folder("0123456789ab");
   fs::create_dir_all(&folder).unwrap();
   assert!(AppProcess::command(&layout, ["x"]).is_err(), "the exe is missing");
   fs::write(folder.join(product::APP_EXE), b"MZ").unwrap();

   let command = AppProcess::command(&layout, ["--open", "a b"]).unwrap();
   assert_eq!(command.get_program(), folder.join(product::APP_EXE).as_os_str());
   assert_eq!(command.get_args().collect::<Vec<_>>(), ["--open", "a b"]);
   assert_eq!(command.get_current_dir(), Some(folder.as_path()));
   assert_eq!(
      env(&command, AppProcess::PATH_VARIABLE),
      Some(layout.launcher_path().as_os_str())
   );
   assert_eq!(
      env(&command, AppProcess::APP_ID_VARIABLE),
      Some(OsStr::new(product::APP_ID))
   );
}

#[test]
fn waits_for_a_process_to_exit() {
   let mut quick = Command::new("cmd").args(["/c", "exit"]).spawn().unwrap();
   assert!(AppProcess::wait_for_exit(quick.id(), Some(Duration::from_secs(10))).unwrap());
   quick.wait().unwrap();

   let mut slow = Command::new("ping")
      .args(["-n", "30", "127.0.0.1"])
      .stdout(Stdio::null())
      .spawn()
      .unwrap();
   assert!(!AppProcess::wait_for_exit(slow.id(), Some(Duration::from_millis(100))).unwrap());
   slow.kill().unwrap();
   slow.wait().unwrap();

   // A pid that belongs to no process counts as already exited.
   assert!(AppProcess::wait_for_exit(u32::MAX - 3, Some(Duration::from_millis(100))).unwrap());
}
