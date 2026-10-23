// The CLI commands end to end, without GUI: --install --quiet, --update, --repair, --apply, the key commands and
// --uninstall. Every test gets its own install folder, release folder, shortcut folders, registry keys
// (HKCU\Software\LauncherTest-<pid>-<n>) and lock name, so the real installation of the machine is never touched.
// The "application" of the test releases is a copy of ping.exe, so starting it really works.

mod common;

use std::fs;
use std::path::{Path, PathBuf};
use std::process::{Child, Command};
use std::sync::atomic::{AtomicUsize, Ordering};

use common::{TestKey, content, publish};
use launcher::cli::{ExitCode, InstallOptions};
use launcher::commands::{CommandContext, InstallCommand, KeyCommand, StartCommand, UninstallCommand, UpdateCommand};
use launcher::config::{ConfigLocation, KeyScope, LauncherConfig, RegistryHive, TrustedKeys};
use launcher::install::InstallLayout;
use launcher::product;
use launcher::shell::{ShellIntegration, Shortcut, UninstallEntry};
use tempfile::TempDir;

static NEXT: AtomicUsize = AtomicUsize::new(0);

const V1_TIME: &str = "2026-09-27T10:15:00Z";
const V2_TIME: &str = "2026-09-28T08:05:00Z";

struct Fixture {
   temp: TempDir,
   registry_root: String,
   context: CommandContext,
   release: PathBuf,
   target: PathBuf,
   key: TestKey,
   pem: PathBuf,
}

impl Fixture {
   fn new() -> Self {
      let temp = tempfile::tempdir().unwrap();
      let n = NEXT.fetch_add(1, Ordering::Relaxed);
      let registry_root = format!(r"Software\LauncherTest-{}-{n}", std::process::id());
      let context = CommandContext {
         location: ConfigLocation {
            user_key: format!(r"{registry_root}\App\Launcher"),
            policy_hive: RegistryHive::CurrentUser,
            policy_key: format!(r"{registry_root}\Policy\Launcher"),
         },
         integration: ShellIntegration {
            start_menu: temp.path().join("start").join("Test App.lnk"),
            desktop: temp.path().join("desktop").join("Test App.lnk"),
            entry: UninstallEntry::new(format!(r"{registry_root}\Uninstall\Test.App")),
         },
         app_key: format!(r"{registry_root}\App"),
         lock_name: format!(r"Local\LauncherCommandTest-{}-{n}", std::process::id()),
         quiet: true,
      };

      let key = TestKey::new(11);
      let pem = temp.path().join("release.pem");
      fs::write(&pem, key.public.to_pem()).unwrap();
      Self {
         release: temp.path().join("wpf-release"),
         target: temp.path().join("install"),
         temp,
         registry_root,
         context,
         key,
         pem,
      }
   }

   fn layout(&self) -> InstallLayout {
      InstallLayout::new(&self.target)
   }

   fn publish(&self, library: Vec<u8>, time: &str) {
      let ping = fs::read(r"C:\Windows\System32\PING.EXE").unwrap();
      publish(
         &self.release,
         &[
            (product::APP_EXE, ping),
            ("lib/library.dll", library),
            ("data.bin", content(3, 40_000)),
         ],
         &self.key,
         time,
      );
   }

   fn options(&self) -> InstallOptions {
      InstallOptions {
         source: Some(self.release.display().to_string()),
         target: Some(self.target.clone()),
         imports: vec![self.pem.clone()],
         no_run: true,
         ..InstallOptions::default()
      }
   }

   fn install(&self) {
      self.publish(content(1, 10_000), V1_TIME);
      InstallCommand::run(&self.context, &self.options()).unwrap();
   }

   fn entry_string(&self, name: &str) -> Option<String> {
      RegistryHive::CurrentUser
         .read_string(self.context.integration.entry.key(), name)
         .unwrap()
   }

   fn installed_file(&self, path: &str) -> PathBuf {
      let layout = self.layout();
      InstallLayout::file_in(&layout.installed_app_folder(), path)
   }
}

impl Drop for Fixture {
   fn drop(&mut self) {
      let _ = RegistryHive::CurrentUser.delete_tree(&self.registry_root);
   }
}

// A process running from the install folder, like an open application.
struct Running(Child);

impl Running {
   fn start(folder: &Path) -> Self {
      let exe = folder.join("busy.exe");
      fs::copy(r"C:\Windows\System32\PING.EXE", &exe).unwrap();
      Self(Command::new(exe).args(["-n", "60", "127.0.0.1"]).spawn().unwrap())
   }
}

impl Drop for Running {
   fn drop(&mut self) {
      let _ = self.0.kill();
      let _ = self.0.wait();
   }
}

#[test]
fn quiet_install_sets_everything_up() {
   let fixture = Fixture::new();
   fixture.install();
   let layout = fixture.layout();

   let current = layout.read_current().unwrap().unwrap();
   assert_eq!(current.published_at_utc, V1_TIME);
   assert!(fixture.installed_file(product::APP_EXE).is_file());
   // The test release carries no launcher.exe, so the running exe was copied to the root.
   assert!(layout.launcher_path().is_file());
   assert!(layout.log_path().is_file());

   let config = LauncherConfig::load(&fixture.context.location).unwrap();
   assert_eq!(config.source, fixture.release.display().to_string());
   assert!(InstallLayout::same_path(
      Path::new(&config.install_folder),
      &fixture.target
   ));
   assert!(config.start_menu && config.desktop);
   let keys = TrustedKeys::list(&fixture.context.location).unwrap();
   assert_eq!(keys.len(), 1);
   assert_eq!(keys[0].key.key_id(), fixture.key.public.key_id());
   assert_eq!(keys[0].scope, KeyScope::User);

   let integration = &fixture.context.integration;
   // The shell stores the long form of a target, while the temporary folder may come in its 8.3 form.
   let target = Shortcut::target_of(&integration.start_menu).unwrap().unwrap();
   assert!(InstallLayout::same_path(&target, &layout.launcher_path()));
   assert_eq!(
      Shortcut::app_id_of(&integration.start_menu).unwrap().as_deref(),
      Some(product::APP_ID)
   );
   let target = Shortcut::target_of(&integration.desktop).unwrap().unwrap();
   assert!(InstallLayout::same_path(&target, &layout.launcher_path()));
   assert_eq!(Shortcut::app_id_of(&integration.desktop).unwrap(), None);

   assert_eq!(fixture.entry_string("DisplayName").as_deref(), Some(product::APP_NAME));
   assert_eq!(fixture.entry_string("Publisher").as_deref(), Some(product::PUBLISHER));
   assert_eq!(
      fixture.entry_string("DisplayVersion").as_deref(),
      Some("2026.9.27.1015")
   );
   let launcher = layout.launcher_path().display().to_string();
   assert_eq!(fixture.entry_string("DisplayIcon"), Some(format!("{launcher},0")));
   assert_eq!(
      fixture.entry_string("UninstallString"),
      Some(format!("\"{launcher}\" --uninstall"))
   );
   assert_eq!(
      fixture.entry_string("QuietUninstallString"),
      Some(format!("\"{launcher}\" --uninstall --quiet"))
   );
   assert_eq!(
      fixture.entry_string("ModifyPath"),
      Some(format!("\"{launcher}\" --maintenance"))
   );
   assert_eq!(fixture.entry_string("InstallDate").map(|date| date.len()), Some(8));
   let hive = RegistryHive::CurrentUser;
   let key = integration.entry.key();
   assert_eq!(hive.read_dword(key, "NoRepair").unwrap(), Some(1));
   assert!(hive.read_dword(key, "EstimatedSize").unwrap().unwrap() > 0);
}

#[test]
fn install_options_turn_the_shortcuts_off() {
   let fixture = Fixture::new();
   fixture.publish(content(1, 10_000), V1_TIME);
   let options = InstallOptions {
      no_start_menu: true,
      no_desktop: true,
      ..fixture.options()
   };
   InstallCommand::run(&fixture.context, &options).unwrap();

   assert!(!fixture.context.integration.start_menu.exists());
   assert!(!fixture.context.integration.desktop.exists());
   let config = LauncherConfig::load(&fixture.context.location).unwrap();
   assert!(!config.start_menu && !config.desktop);
}

#[test]
fn install_refuses_before_writing_anything() {
   let fixture = Fixture::new();
   fixture.publish(content(1, 10_000), V1_TIME);
   let nothing_written = |fixture: &Fixture| {
      assert!(!fixture.target.exists());
      assert_eq!(
         LauncherConfig::load(&fixture.context.location).unwrap(),
         LauncherConfig::default()
      );
      assert!(!fixture.context.integration.entry.exists().unwrap());
   };

   let no_key = InstallOptions {
      imports: vec![],
      ..fixture.options()
   };
   let error = InstallCommand::run(&fixture.context, &no_key).unwrap_err();
   assert_eq!(error.code, ExitCode::InvalidArguments);
   nothing_written(&fixture);

   let private = fixture.temp.path().join("private.pem");
   fs::write(
      &private,
      "-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----\n",
   )
   .unwrap();
   let with_private = InstallOptions {
      imports: vec![private],
      ..fixture.options()
   };
   let error = InstallCommand::run(&fixture.context, &with_private).unwrap_err();
   assert_eq!(error.code, ExitCode::InvalidArguments);
   assert!(error.message.contains("private key"), "{}", error.message);
   nothing_written(&fixture);

   let other_key = fixture.temp.path().join("other.pem");
   fs::write(&other_key, TestKey::new(12).public.to_pem()).unwrap();
   let wrong_key = InstallOptions {
      imports: vec![other_key],
      ..fixture.options()
   };
   let error = InstallCommand::run(&fixture.context, &wrong_key).unwrap_err();
   assert_eq!(error.code, ExitCode::SourceFailed);
   nothing_written(&fixture);

   let missing_source = InstallOptions {
      source: Some(fixture.temp.path().join("nowhere").display().to_string()),
      ..fixture.options()
   };
   let error = InstallCommand::run(&fixture.context, &missing_source).unwrap_err();
   assert_eq!(error.code, ExitCode::SourceFailed);
   nothing_written(&fixture);

   fs::create_dir_all(&fixture.target).unwrap();
   fs::write(fixture.target.join("notes.txt"), b"someone else's file").unwrap();
   let error = InstallCommand::run(&fixture.context, &fixture.options()).unwrap_err();
   assert_eq!(error.code, ExitCode::InvalidArguments);
   assert!(error.message.contains("not empty"), "{}", error.message);
}

#[test]
fn install_refuses_a_second_folder() {
   let fixture = Fixture::new();
   fixture.install();
   let elsewhere = InstallOptions {
      target: Some(fixture.temp.path().join("second")),
      ..fixture.options()
   };
   let error = InstallCommand::run(&fixture.context, &elsewhere).unwrap_err();
   assert_eq!(error.code, ExitCode::InvalidArguments);
   assert!(error.message.contains("already installed"), "{}", error.message);

   // Installing again into the same folder is allowed and changes nothing.
   InstallCommand::run(&fixture.context, &fixture.options()).unwrap();
}

#[test]
fn update_installs_a_new_release_and_refreshes_the_entry() {
   let fixture = Fixture::new();
   fixture.install();
   let before = fixture.layout().read_current().unwrap().unwrap();

   UpdateCommand::update(&fixture.context).unwrap();
   assert_eq!(
      fixture.layout().read_current().unwrap().unwrap(),
      before,
      "nothing new yet"
   );

   fixture.publish(content(2, 12_000), V2_TIME);
   UpdateCommand::update(&fixture.context).unwrap();
   let after = fixture.layout().read_current().unwrap().unwrap();
   assert_ne!(after.version, before.version);
   assert_eq!(
      fs::read(fixture.installed_file("lib/library.dll")).unwrap(),
      content(2, 12_000)
   );
   assert!(!fixture.layout().version_folder(&before.version).exists());
   assert_eq!(
      fixture.entry_string("DisplayVersion").as_deref(),
      Some("2026.9.28.0805")
   );
}

#[test]
fn repair_restores_files_and_shortcuts() {
   let fixture = Fixture::new();
   fixture.install();
   fs::write(fixture.installed_file("lib/library.dll"), b"damaged").unwrap();
   fs::remove_file(fixture.installed_file("data.bin")).unwrap();
   fs::remove_file(&fixture.context.integration.desktop).unwrap();
   fixture.context.integration.entry.remove().unwrap();

   UpdateCommand::repair(&fixture.context).unwrap();
   assert_eq!(
      fs::read(fixture.installed_file("lib/library.dll")).unwrap(),
      content(1, 10_000)
   );
   assert_eq!(
      fs::read(fixture.installed_file("data.bin")).unwrap(),
      content(3, 40_000)
   );
   assert!(fixture.context.integration.desktop.is_file());
   assert!(fixture.context.integration.entry.exists().unwrap());
}

#[test]
fn repair_waits_for_the_application_to_close() {
   let fixture = Fixture::new();
   fixture.install();
   let running = Running::start(&fixture.target);

   let error = UpdateCommand::repair(&fixture.context).unwrap_err();
   assert_eq!(error.code, ExitCode::AppRunning);
   drop(running);
   UpdateCommand::repair(&fixture.context).unwrap();
}

#[test]
fn a_busy_lock_stops_the_commands() {
   let fixture = Fixture::new();
   fixture.install();
   let held = std::thread::scope(|scope| {
      let (taken, release) = (std::sync::mpsc::channel(), std::sync::mpsc::channel::<()>());
      let name = fixture.context.lock_name.clone();
      scope.spawn(move || {
         let _lock = launcher::launcher_lock::LauncherLock::acquire(&name, std::time::Duration::ZERO).unwrap();
         taken.0.send(()).unwrap();
         release.1.recv().unwrap();
      });
      taken.1.recv().unwrap();
      let error = UpdateCommand::update(&fixture.context).unwrap_err();
      release.0.send(()).unwrap();
      error
   });
   assert_eq!(held.code, ExitCode::IoFailed);
   assert!(held.message.contains("busy"), "{}", held.message);
}

#[test]
fn apply_updates_without_asking_and_repairs_a_damaged_installation() {
   let fixture = Fixture::new();
   fixture.install();
   let args = ["-n".into(), "1".into(), "127.0.0.1".into()];

   fixture.publish(content(2, 12_000), V2_TIME);
   // A pid that does not exist counts as already exited.
   StartCommand::apply(&fixture.context, u32::MAX - 3, &args).unwrap();
   assert_eq!(
      fixture.layout().read_current().unwrap().unwrap().published_at_utc,
      V2_TIME
   );

   fs::remove_file(fixture.installed_file("data.bin")).unwrap();
   fs::remove_file(fixture.layout().launcher_path()).unwrap();
   // Give the ping started above a moment to exit, so repair does not wait for it.
   std::thread::sleep(std::time::Duration::from_secs(2));
   StartCommand::apply(&fixture.context, u32::MAX - 3, &args).unwrap();
   assert!(fixture.installed_file("data.bin").is_file());
   assert!(fixture.layout().launcher_path().is_file());
}

#[test]
fn key_commands_manage_the_user_keys() {
   let fixture = Fixture::new();
   let context = &fixture.context;
   KeyCommand::import(context, &fixture.pem).unwrap();
   let key_id = fixture.key.public.key_id().to_string();
   let keys = TrustedKeys::list(&context.location).unwrap();
   assert_eq!(keys.len(), 1);
   assert_eq!(keys[0].key.key_id(), key_id);
   KeyCommand::list(context).unwrap();

   let private = fixture.temp.path().join("private.pem");
   fs::write(
      &private,
      "-----BEGIN EC PRIVATE KEY-----\nAAAA\n-----END EC PRIVATE KEY-----\n",
   )
   .unwrap();
   assert_eq!(
      KeyCommand::import(context, &private).unwrap_err().code,
      ExitCode::InvalidArguments
   );

   KeyCommand::remove(context, &key_id.to_lowercase()).unwrap();
   assert!(TrustedKeys::list(&context.location).unwrap().is_empty());
   assert_eq!(
      KeyCommand::remove(context, &key_id).unwrap_err().code,
      ExitCode::InvalidArguments
   );

   // A key from the IT policy stays.
   RegistryHive::CurrentUser
      .write_string(
         &format!(r"{}\TrustedKeys", context.location.policy_key),
         &key_id,
         &fixture.key.public.to_pem(),
      )
      .unwrap();
   let error = KeyCommand::remove(context, &key_id).unwrap_err();
   assert!(error.message.contains("IT policy"), "{}", error.message);
   assert_eq!(TrustedKeys::list(&context.location).unwrap().len(), 1);
}

#[test]
fn uninstall_removes_everything_once_the_application_is_closed() {
   let fixture = Fixture::new();
   fixture.install();
   let running = Running::start(&fixture.target);
   let error = UninstallCommand::run(&fixture.context).unwrap_err();
   assert_eq!(error.code, ExitCode::AppRunning);
   assert!(fixture.layout().current_path().is_file());
   drop(running);

   // A shortcut with the same name that belongs to something else is left alone.
   let integration = &fixture.context.integration;
   let other = fixture.temp.path().join("other.exe");
   fs::write(&other, b"MZ").unwrap();
   Shortcut::create(&integration.desktop, &other, None).unwrap();

   UninstallCommand::run(&fixture.context).unwrap();
   assert!(!fixture.target.exists());
   assert!(!integration.start_menu.exists());
   assert!(integration.desktop.exists());
   assert!(!integration.entry.exists().unwrap());
   assert_eq!(
      RegistryHive::CurrentUser
         .string_values(&format!(r"{}\TrustedKeys", fixture.context.location.user_key))
         .unwrap(),
      vec![]
   );
   assert_eq!(
      LauncherConfig::load(&fixture.context.location).unwrap(),
      LauncherConfig::default()
   );
}

#[test]
fn uninstall_finish_only_removes_the_registered_folder() {
   let fixture = Fixture::new();
   fixture.install();
   let other = fixture.temp.path().join("wpf-release");
   let error = UninstallCommand::finish(&fixture.context, &other, u32::MAX - 3).unwrap_err();
   assert_eq!(error.code, ExitCode::InvalidArguments);
   assert!(other.exists());

   UninstallCommand::finish(&fixture.context, &fixture.target, u32::MAX - 3).unwrap();
   assert!(!fixture.target.exists());
   assert!(!fixture.context.integration.entry.exists().unwrap());
}

#[test]
fn prepare_verifies_staging_without_activating_and_resumes() {
   let fixture = Fixture::new();
   fixture.install();
   let before = fs::read(fixture.layout().current_path()).unwrap();
   fixture.publish(content(4, 20_000), V2_TIME);
   let prepared = launcher::commands::PrepareCommand::run(&fixture.context).unwrap();
   assert_eq!(prepared["state"], "prepared");
   assert_eq!(fs::read(fixture.layout().current_path()).unwrap(), before);
   let id = prepared["release"].as_str().unwrap();
   assert!(fixture.layout().staging_folder(id).join("release.json").is_file());
   assert_eq!(
      launcher::commands::PrepareCommand::run(&fixture.context).unwrap(),
      prepared
   );
   UpdateCommand::update(&fixture.context).unwrap();
   assert_eq!(
      launcher::commands::PrepareCommand::run(&fixture.context).unwrap()["state"],
      "none"
   );
}

#[test]
fn installer_ini_uses_source_only_and_can_be_reset() {
   let temp = tempfile::tempdir().unwrap();
   let source =
      launcher::config::InstallerSettings::source(temp.path(), "https://example.test/cdn/inventory/").unwrap();
   assert_eq!(source, "https://example.test/cdn/inventory/");
   fs::write(
      temp.path().join("settings.ini"),
      "; IT override\n[Launcher]\nSource=\\\\fileserver\\release\n",
   )
   .unwrap();
   assert_eq!(
      launcher::config::InstallerSettings::source(temp.path(), "unused").unwrap(),
      r"\\fileserver\release"
   );
}

#[test]
fn cab_roundtrip_reuses_old_files_and_stages_offline_without_activation() {
   let fixture = Fixture::new();
   fixture.publish(content(1, 10_000), V1_TIME);
   let cab = fixture.temp.path().join("Test.App.cab");
   let keys = [fixture.key.public.clone()];
   let progress = std::sync::Arc::new(launcher::install::UpdateProgress::new());
   let message = launcher::commands::CabCommand::download_new(
      &cab,
      &fixture.release.display().to_string(),
      &keys,
      progress.clone(),
   )
   .unwrap();
   assert!(message.starts_with("Cab updated to release"));
   assert_eq!(&fs::read(&cab).unwrap()[..4], b"MSCF");
   assert!(
      launcher::commands::CabCommand::update_existing(
         &cab,
         &fixture.release.display().to_string(),
         &keys,
         progress.clone()
      )
      .unwrap()
      .starts_with("Cab is up to date")
   );
   fixture.publish(content(2, 20_000), V2_TIME);
   launcher::commands::CabCommand::update_existing(
      &cab,
      &fixture.release.display().to_string(),
      &keys,
      progress.clone(),
   )
   .unwrap();
   let before = fs::read(&cab).unwrap();
   assert!(
      launcher::commands::CabCommand::update_existing(&cab, "nonexistent-release-source", &keys, progress.clone())
         .is_err()
   );
   assert_eq!(fs::read(&cab).unwrap(), before);
   let release = launcher::commands::CabCommand::stage(&cab, &fixture.layout(), &keys, progress.clone()).unwrap();
   assert!(!fixture.layout().current_path().exists());
   let offline = launcher::source::FolderSource::new("unreachable-source");
   launcher::install::Updater::new(&offline, &fixture.layout(), progress)
      .install(&release)
      .unwrap();
   assert!(fixture.layout().current_path().is_file());
}

#[test]
fn cab_commands_enforce_creation_and_update_preconditions() {
   use launcher::commands::CabCommand;
   use launcher::install::UpdateProgress;
   use std::sync::Arc;
   let fixture = Fixture::new();
   let cab = fixture.temp.path().join("test.cab");
   let error = CabCommand::update_existing(&cab, "unused", &[], Arc::new(UpdateProgress::new())).unwrap_err();
   assert!(error.message.contains("--download-cab"));
   assert!(!cab.exists());
   fs::write(&cab, b"original").unwrap();
   let error = CabCommand::download_new(&cab, "unused", &[], Arc::new(UpdateProgress::new())).unwrap_err();
   assert!(error.message.contains("--update-cab"));
   assert_eq!(fs::read(&cab).unwrap(), b"original");
   fixture.publish(content(1, 10_000), V1_TIME);
   let keys = [fixture.key.public.clone()];
   CabCommand::update_existing(
      &cab,
      &fixture.release.display().to_string(),
      &keys,
      Arc::new(UpdateProgress::new()),
   )
   .unwrap();
   assert_eq!(&fs::read(&cab).unwrap()[..4], b"MSCF");
}

#[test]
fn form_repair_saves_checked_source_and_clean_install_removes_settings() {
   use launcher::commands::{InstallAction, InstallRequest};
   use launcher::install::UpdateProgress;
   use std::sync::Arc;
   let fixture = Fixture::new();
   fixture.install();
   let alternate = fixture.temp.path().join("alternate");
   publish(
      &alternate,
      &[
         (product::APP_EXE, fs::read(r"C:\Windows\System32\PING.EXE").unwrap()),
         ("new.dll", content(9, 200)),
      ],
      &fixture.key,
      V2_TIME,
   );
   let mut request = InstallRequest::from_options(&fixture.options()).unwrap();
   request.source = Some(alternate.display().to_string());
   request.imported.clear();
   let job = InstallCommand::prepare(&fixture.context, &request).unwrap();
   let progress = Arc::new(UpdateProgress::new());
   job.execute_action(&fixture.context, progress.clone(), InstallAction::Repair)
      .unwrap();
   assert_eq!(
      fixture.context.load_config().unwrap().source,
      alternate.display().to_string()
   );
   assert_eq!(progress.done_bytes(), progress.total_bytes());
   RegistryHive::CurrentUser
      .write_string(&fixture.context.app_key, "CustomSetting", "old")
      .unwrap();
   fs::write(fixture.target.join("unlisted"), b"remove me").unwrap();
   job.execute_action(
      &fixture.context,
      Arc::new(UpdateProgress::new()),
      InstallAction::CleanInstall,
   )
   .unwrap();
   assert!(!fixture.target.join("unlisted").exists());
   assert_eq!(
      RegistryHive::CurrentUser
         .read_string(&fixture.context.app_key, "CustomSetting")
         .unwrap(),
      None
   );
   assert!(fixture.installed_file("new.dll").is_file());
   assert!(TrustedKeys::list(&fixture.context.location).unwrap().is_empty());
}

#[test]
fn clean_install_checks_signature_before_removing_existing_installation() {
   use launcher::commands::{InstallAction, InstallRequest};
   use launcher::install::UpdateProgress;
   use std::sync::Arc;
   let fixture = Fixture::new();
   fixture.install();
   let original = fs::read(fixture.layout().current_path()).unwrap();
   fs::write(fixture.release.join("release.json"), b"invalid").unwrap();
   let request = InstallRequest::from_options(&fixture.options()).unwrap();
   let job = InstallCommand::prepare(&fixture.context, &request).unwrap();
   assert!(
      job.execute_action(
         &fixture.context,
         Arc::new(UpdateProgress::new()),
         InstallAction::CleanInstall
      )
      .is_err()
   );
   assert_eq!(fs::read(fixture.layout().current_path()).unwrap(), original);
   assert!(fixture.context.integration.entry.exists().unwrap());
}

#[test]
fn uninstall_progress_counts_files_and_windows_integration() {
   use launcher::install::{Uninstaller, UpdatePhase, UpdateProgress};
   let fixture = Fixture::new();
   fixture.install();
   let progress = UpdateProgress::new();
   Uninstaller::new(&fixture.layout())
      .remove(&fixture.context.integration, &fixture.context.app_key, &progress)
      .unwrap();
   assert_eq!(progress.phase(), UpdatePhase::Removing);
   // Three release files, a version sweep, a root sweep, and two Windows integration steps.
   assert_eq!(progress.total_bytes(), 7);
   assert_eq!(progress.done_bytes(), 7);
   assert!(!fixture.target.exists());
}

#[test]
fn a_cancelled_clean_check_keeps_the_installation() {
   use launcher::commands::{InstallAction, InstallRequest};
   use launcher::install::UpdateProgress;
   use std::sync::Arc;
   let fixture = Fixture::new();
   fixture.install();
   let before = fs::read(fixture.layout().current_path()).unwrap();
   let job = InstallCommand::prepare(
      &fixture.context,
      &InstallRequest::from_options(&fixture.options()).unwrap(),
   )
   .unwrap();
   let progress = Arc::new(UpdateProgress::new());
   progress.cancel();
   assert_eq!(
      job.execute_action(&fixture.context, progress, InstallAction::CleanInstall)
         .unwrap_err()
         .code,
      ExitCode::Cancelled
   );
   assert_eq!(fs::read(fixture.layout().current_path()).unwrap(), before);
}

#[test]
fn uninstall_retries_without_counting_completed_files_twice() {
   use launcher::install::{Uninstaller, UpdateProgress};
   use std::os::windows::fs::OpenOptionsExt;
   let fixture = Fixture::new();
   fixture.install();
   let locked = fixture.target.join("unlisted-lock");
   fs::write(&locked, b"locked").unwrap();
   let held = fs::OpenOptions::new().read(true).share_mode(1).open(&locked).unwrap();
   let progress = UpdateProgress::new();
   std::thread::scope(|scope| {
      scope.spawn(move || {
         std::thread::sleep(std::time::Duration::from_millis(650));
         drop(held);
      });
      Uninstaller::new(&fixture.layout())
         .remove(&fixture.context.integration, &fixture.context.app_key, &progress)
         .unwrap();
   });
   assert_eq!(progress.done_bytes(), progress.total_bytes());
   assert!(!fixture.target.exists());
}

#[test]
fn cab_with_a_different_signing_key_is_rebuilt_with_a_full_download() {
   use launcher::commands::CabCommand;
   use launcher::install::UpdateProgress;
   use std::sync::Arc;
   let fixture = Fixture::new();
   let files = [("same.dll", content(3, 1000))];
   publish(&fixture.release, &files, &fixture.key, V1_TIME);
   let cab = fixture.temp.path().join("test.cab");
   CabCommand::download_new(
      &cab,
      &fixture.release.display().to_string(),
      std::slice::from_ref(&fixture.key.public),
      Arc::new(UpdateProgress::new()),
   )
   .unwrap();
   let new_key = TestKey::new(55);
   publish(&fixture.release, &files, &new_key, V2_TIME);
   let progress = Arc::new(UpdateProgress::new());
   CabCommand::update_existing(
      &cab,
      &fixture.release.display().to_string(),
      &[new_key.public],
      progress.clone(),
   )
   .unwrap();
   assert_eq!(progress.transferred_bytes(), 1000);
}

#[test]
fn uninstall_does_not_need_a_source_or_trusted_keys() {
   let fixture = Fixture::new();
   fixture.install();
   let mut config = fixture.context.load_config().unwrap();
   config.source.clear();
   config.save(&fixture.context.location).unwrap();
   RegistryHive::CurrentUser
      .delete_tree(&format!(r"{}\TrustedKeys", fixture.context.location.user_key))
      .unwrap();
   UninstallCommand::run(&fixture.context).unwrap();
   assert!(!fixture.target.exists());
}
