// Install, update, resume, repair and clean-up scenarios of the updater, run against signed releases that the
// tests publish into temporary folders.

mod common;

use std::fs::{self, OpenOptions};
use std::os::windows::fs::OpenOptionsExt;
use std::sync::Arc;

use common::{CountingSource, TestKey, content, publish};
use launcher::format::{ReleaseLayout, ReleasePublicKey, SignatureError};
use launcher::install::{InstallLayout, UpdateError, UpdateOutcome, UpdateProgress, Updater};
use launcher::product;
use tempfile::TempDir;

const V1_TIME: &str = "2026-09-27T10:15:00Z";
const V2_TIME: &str = "2026-09-28T08:00:00Z";

struct Fixture {
   _temp: TempDir,
   release: std::path::PathBuf,
   layout: InstallLayout,
   key: TestKey,
}

impl Fixture {
   fn new() -> Self {
      let temp = tempfile::tempdir().unwrap();
      let release = temp.path().join("wpf-release");
      let layout = InstallLayout::new(temp.path().join("install"));
      Self {
         _temp: temp,
         release,
         layout,
         key: TestKey::new(7),
      }
   }

   fn publish(&self, files: &[(&str, Vec<u8>)], time: &str) -> String {
      InstallLayout::release_id(&publish(&self.release, files, &self.key, time))
   }

   // Checks and installs whatever the release folder holds, the way the launcher does.
   fn update(&self, source: &CountingSource) -> Result<UpdateOutcome, UpdateError> {
      let updater = Updater::new(source, &self.layout, Arc::new(UpdateProgress::new()));
      let release = updater.check(self.keys())?;
      updater.install(&release)
   }

   // A one-element slice borrowed from the key, without cloning it.
   fn keys(&self) -> &[ReleasePublicKey] {
      std::slice::from_ref(&self.key.public)
   }

   // The installed copy of `path`; every release lives in the same app folder, so `_id` only documents which
   // release the caller expects there.
   fn installed(&self, _id: &str, path: &str) -> Vec<u8> {
      fs::read(InstallLayout::file_in(&self.layout.app_folder(), path)).unwrap()
   }

   fn active(&self) -> Option<String> {
      self.layout.read_current().unwrap().map(|current| current.version)
   }
}

fn v1_files() -> Vec<(&'static str, Vec<u8>)> {
   vec![
      (product::APP_EXE, content(1, 3000)),
      ("Sample.Core.dll", content(2, 5000)),
      ("runtimes/win-x64/native/sample.dll", content(3, 700)),
      ("empty.txt", Vec::new()),
      ("launcher.exe", content(4, 900)),
   ]
}

fn downloaded(outcome: &UpdateOutcome) -> usize {
   match outcome {
      UpdateOutcome::Installed(summary) | UpdateOutcome::Repaired(summary) => summary.downloaded_files,
      UpdateOutcome::AlreadyActive => 0,
   }
}

#[test]
fn installs_into_an_empty_folder() {
   let fixture = Fixture::new();
   let files = v1_files();
   let id = fixture.publish(&files, V1_TIME);
   let source = CountingSource::new(&fixture.release);

   let outcome = fixture.update(&source).unwrap();
   assert_eq!(downloaded(&outcome), files.len());
   assert_eq!(fixture.active().as_deref(), Some(id.as_str()));
   assert_eq!(
      fixture.layout.read_current().unwrap().unwrap().published_at_utc,
      V1_TIME
   );
   for (path, bytes) in &files {
      assert_eq!(&fixture.installed(&id, path), bytes, "{path}");
   }

   // The manifest and its signature travel with the app, byte for byte.
   let app = fixture.layout.app_folder();
   for name in [ReleaseLayout::MANIFEST_FILE_NAME, ReleaseLayout::SIGNATURE_FILE_NAME] {
      assert_eq!(
         fs::read(app.join(name)).unwrap(),
         fs::read(fixture.release.join(name)).unwrap()
      );
   }

   // The release's launcher becomes the root launcher, and nothing is left staged.
   assert_eq!(fs::read(fixture.layout.launcher_path()).unwrap(), content(4, 900));
   assert!(fixture.layout.version_folders().unwrap().is_empty());

   // Installing the same release again does nothing.
   source.reset();
   assert_eq!(fixture.update(&source).unwrap(), UpdateOutcome::AlreadyActive);
   assert!(source.opened_paths().is_empty());
}

#[test]
fn update_downloads_only_changed_files_and_removes_the_old_version() {
   let fixture = Fixture::new();
   let v1 = fixture.publish(&v1_files(), V1_TIME);
   let source = CountingSource::new(&fixture.release);
   fixture.update(&source).unwrap();

   let mut files = v1_files();
   files[1].1 = content(20, 5100);
   files.push(("Sample.New.dll", content(21, 1234)));
   let v2 = fixture.publish(&files, V2_TIME);
   source.reset();

   let updater = Updater::new(&source, &fixture.layout, Arc::new(UpdateProgress::new()));
   let release = updater.check(fixture.keys()).unwrap();
   assert!(!updater.is_active(&release));
   assert_eq!(updater.download_size(&release), 5100 + 1234);

   let outcome = updater.install(&release).unwrap();
   let UpdateOutcome::Installed(summary) = outcome else {
      panic!("expected an install, got {outcome:?}");
   };
   assert_eq!(source.opened_paths(), ["Sample.Core.dll", "Sample.New.dll"]);
   assert_eq!(summary.downloaded_files, 2);
   assert_eq!(summary.downloaded_bytes, 5100 + 1234);
   assert_eq!(summary.reused_files, files.len() - 2);
   assert_eq!(updater.progress().transferred_bytes(), 5100 + 1234);

   assert_eq!(fixture.active().as_deref(), Some(v2.as_str()));
   for (path, bytes) in &files {
      assert_eq!(&fixture.installed(&v2, path), bytes, "{path}");
   }
   assert!(!fixture.layout.version_folder(&v1).exists());
   assert!(fixture.layout.version_folders().unwrap().is_empty());
}

#[test]
fn update_deletes_files_the_release_no_longer_lists() {
   let fixture = Fixture::new();
   fixture.publish(&v1_files(), V1_TIME);
   let source = CountingSource::new(&fixture.release);
   fixture.update(&source).unwrap();

   let files: Vec<_> = v1_files()
      .into_iter()
      .filter(|(path, _)| !path.starts_with("runtimes/"))
      .collect();
   let v2 = fixture.publish(&files, V2_TIME);
   source.reset();
   fixture.update(&source).unwrap();

   assert!(source.opened_paths().is_empty(), "nothing changed, one file less");
   assert_eq!(fixture.active().as_deref(), Some(v2.as_str()));
   let app = fixture.layout.app_folder();
   assert!(!InstallLayout::file_in(&app, "runtimes/win-x64/native/sample.dll").exists());
   assert!(!app.join("runtimes").exists(), "folders left empty are deleted");
}

#[test]
fn an_update_that_stopped_halfway_is_finished_without_downloading_again() {
   let fixture = Fixture::new();
   fixture.publish(&v1_files(), V1_TIME);
   let source = CountingSource::new(&fixture.release);
   fixture.update(&source).unwrap();

   let mut files = v1_files();
   files[1].1 = content(70, 5000);
   files[2].1 = content(71, 700);
   let v2 = fixture.publish(&files, V2_TIME);
   // The previous run already replaced Sample.Core.dll but stopped before writing release.json.
   fs::write(
      InstallLayout::file_in(&fixture.layout.app_folder(), "Sample.Core.dll"),
      content(70, 5000),
   )
   .unwrap();
   source.reset();

   fixture.update(&source).unwrap();
   assert_eq!(source.opened_paths(), ["runtimes/win-x64/native/sample.dll"]);
   assert_eq!(fixture.active().as_deref(), Some(v2.as_str()));
   for (path, bytes) in &files {
      assert_eq!(&fixture.installed(&v2, path), bytes, "{path}");
   }
}

#[test]
fn an_older_installation_moves_its_version_folder_to_app() {
   let fixture = Fixture::new();
   let v1 = fixture.publish(&v1_files(), V1_TIME);
   let source = CountingSource::new(&fixture.release);
   fixture.update(&source).unwrap();
   // The layout before the in-place update: the app lives in app-<id>.
   fs::rename(fixture.layout.app_folder(), fixture.layout.version_folder(&v1)).unwrap();
   assert_eq!(
      fixture.layout.installed_app_folder(),
      fixture.layout.version_folder(&v1)
   );

   let mut files = v1_files();
   files[1].1 = content(80, 5000);
   let v2 = fixture.publish(&files, V2_TIME);
   source.reset();
   fixture.update(&source).unwrap();

   assert_eq!(source.opened_paths(), ["Sample.Core.dll"]);
   assert_eq!(fixture.active().as_deref(), Some(v2.as_str()));
   assert!(!fixture.layout.version_folder(&v1).exists());
   assert_eq!(fixture.layout.installed_app_folder(), fixture.layout.app_folder());
   for (path, bytes) in &files {
      assert_eq!(&fixture.installed(&v2, path), bytes, "{path}");
   }
}

#[test]
fn resumes_a_partial_download() {
   let fixture = Fixture::new();
   let big = content(9, 200_000);
   let files = vec![("big.bin", big.clone()), ("small.bin", content(8, 10))];
   let id = fixture.publish(&files, V1_TIME);

   // An earlier run stopped halfway through big.bin.
   let staging = fixture.layout.staging_folder(&id);
   fs::create_dir_all(&staging).unwrap();
   fs::write(staging.join("big.bin.part"), &big[..120_000]).unwrap();

   let source = CountingSource::new(&fixture.release);
   let outcome = fixture.update(&source).unwrap();
   let UpdateOutcome::Installed(summary) = outcome else {
      panic!("expected an install, got {outcome:?}");
   };
   assert!(
      source
         .opened
         .lock()
         .unwrap()
         .contains(&("big.bin".to_string(), 120_000))
   );
   assert_eq!(summary.downloaded_bytes, 80_000 + 10);
   assert_eq!(fixture.installed(&id, "big.bin"), big);
}

#[test]
fn a_file_changed_after_the_manifest_cancels_the_update() {
   let fixture = Fixture::new();
   let v1 = fixture.publish(&v1_files(), V1_TIME);
   let source = CountingSource::new(&fixture.release);
   fixture.update(&source).unwrap();

   let mut files = v1_files();
   files[1].1 = content(30, 5000);
   let v2 = fixture.publish(&files, V2_TIME);
   // A later Sync replaced the file while release.json still describes the previous content.
   fs::write(
      fixture
         .release
         .join(ReleaseLayout::BINARIES_FOLDER)
         .join("Sample.Core.dll"),
      content(31, 5000),
   )
   .unwrap();

   let error = fixture.update(&source).unwrap_err();
   assert!(
      matches!(&error, UpdateError::ServerChanged { path } if path == "Sample.Core.dll"),
      "{error}"
   );
   assert!(error.to_string().contains("try again later"));
   assert_eq!(fixture.active().as_deref(), Some(v1.as_str()));
   assert_ne!(v1, v2);
   assert_eq!(fixture.installed(&v1, "Sample.Core.dll"), content(2, 5000));
}

#[test]
fn a_bad_signature_writes_nothing() {
   let fixture = Fixture::new();
   fixture.publish(&v1_files(), V1_TIME);
   let source = CountingSource::new(&fixture.release);

   let updater = Updater::new(&source, &fixture.layout, Arc::new(UpdateProgress::new()));
   let other = TestKey::new(8);
   assert!(matches!(
      updater.check(std::slice::from_ref(&other.public)),
      Err(UpdateError::Signature(SignatureError::UnknownKey))
   ));

   let manifest = fixture.release.join(ReleaseLayout::MANIFEST_FILE_NAME);
   let mut bytes = fs::read(&manifest).unwrap();
   bytes[5] ^= 1;
   fs::write(&manifest, bytes).unwrap();
   assert!(matches!(
      updater.check(fixture.keys()),
      Err(UpdateError::Signature(SignatureError::Invalid))
   ));

   assert!(!fixture.layout.root().exists());
   assert!(source.opened_paths().is_empty());
}

#[test]
fn repair_restores_a_damaged_and_a_missing_file() {
   let fixture = Fixture::new();
   let id = fixture.publish(&v1_files(), V1_TIME);
   let source = CountingSource::new(&fixture.release);
   fixture.update(&source).unwrap();

   let version = fixture.layout.app_folder();
   let damaged = InstallLayout::file_in(&version, "Sample.Core.dll");
   let mut bytes = fs::read(&damaged).unwrap();
   bytes[100] ^= 0xff;
   fs::write(&damaged, bytes).unwrap();
   fs::remove_file(InstallLayout::file_in(&version, "runtimes/win-x64/native/sample.dll")).unwrap();
   source.reset();

   let updater = Updater::new(&source, &fixture.layout, Arc::new(UpdateProgress::new()));
   let release = updater.check(fixture.keys()).unwrap();
   let outcome = updater.repair(&release).unwrap();
   let UpdateOutcome::Repaired(summary) = outcome else {
      panic!("expected a repair, got {outcome:?}");
   };
   assert_eq!(summary.downloaded_files, 2);
   assert_eq!(summary.reused_files, v1_files().len() - 2);
   assert_eq!(
      source.opened_paths(),
      ["Sample.Core.dll", "runtimes/win-x64/native/sample.dll"]
   );
   for (path, bytes) in &v1_files() {
      assert_eq!(&fixture.installed(&id, path), bytes, "{path}");
   }
}

#[test]
fn repair_without_current_json_reuses_the_version_folder() {
   let fixture = Fixture::new();
   let id = fixture.publish(&v1_files(), V1_TIME);
   let source = CountingSource::new(&fixture.release);
   fixture.update(&source).unwrap();
   fs::remove_file(fixture.layout.current_path()).unwrap();
   source.reset();

   let updater = Updater::new(&source, &fixture.layout, Arc::new(UpdateProgress::new()));
   let release = updater.check(fixture.keys()).unwrap();
   assert_eq!(downloaded(&updater.repair(&release).unwrap()), 0);
   assert_eq!(fixture.active().as_deref(), Some(id.as_str()));
   assert!(source.opened_paths().is_empty());
}

#[test]
fn a_file_in_use_stops_the_update_before_anything_changes() {
   let fixture = Fixture::new();
   let v1 = fixture.publish(&v1_files(), V1_TIME);
   let source = CountingSource::new(&fixture.release);
   fixture.update(&source).unwrap();

   // A stale staging folder of a release that never finished.
   let stale = fixture.layout.staging_folder("000000000000");
   fs::create_dir_all(&stale).unwrap();

   let mut files = v1_files();
   files[0].1 = content(40, 3000);
   files[1].1 = content(42, 5000);
   files[4].1 = content(41, 950);
   let v2 = fixture.publish(&files, V2_TIME);
   let manifest = fixture.layout.app_folder().join(ReleaseLayout::MANIFEST_FILE_NAME);
   let manifest_before = fs::read(&manifest).unwrap();

   {
      // The app exe is open without sharing, as by a running app, so it cannot be replaced.
      let _running = OpenOptions::new()
         .read(true)
         .share_mode(0)
         .open(InstallLayout::file_in(&fixture.layout.app_folder(), product::APP_EXE))
         .unwrap();
      let error = fixture.update(&source).unwrap_err();
      assert!(
         matches!(&error, UpdateError::Locked { path } if path.ends_with(product::APP_EXE)),
         "{error}"
      );
      assert_eq!(fixture.active().as_deref(), Some(v1.as_str()));
      assert_eq!(
         fixture.installed(&v1, "Sample.Core.dll"),
         content(2, 5000),
         "nothing replaced"
      );
      assert_eq!(fs::read(&manifest).unwrap(), manifest_before);
   }

   // Once the file is free, the next run uses what was downloaded already.
   source.reset();
   fixture.update(&source).unwrap();
   assert!(
      source.opened_paths().is_empty(),
      "the changed files were staged by the failed run"
   );
   assert_eq!(fixture.active().as_deref(), Some(v2.as_str()));
   for (path, bytes) in &files {
      assert_eq!(&fixture.installed(&v2, path), bytes, "{path}");
   }
   assert!(!stale.exists());

   // The root launcher was replaced, and the previous one parked as launcher.old.exe.
   assert_eq!(fs::read(fixture.layout.launcher_path()).unwrap(), content(41, 950));
   assert_eq!(fs::read(fixture.layout.old_launcher_path()).unwrap(), content(4, 900));

   let updater = Updater::new(&source, &fixture.layout, Arc::new(UpdateProgress::new()));
   updater.remove_leftovers();
   assert!(!fixture.layout.old_launcher_path().exists());
   assert!(fixture.layout.app_folder().exists());
}

#[test]
fn a_cancelled_update_keeps_the_active_version() {
   let fixture = Fixture::new();
   let v1 = fixture.publish(&v1_files(), V1_TIME);
   let source = CountingSource::new(&fixture.release);
   fixture.update(&source).unwrap();

   let mut files = v1_files();
   files[1].1 = content(50, 5000);
   fixture.publish(&files, V2_TIME);

   let progress = Arc::new(UpdateProgress::new());
   let updater = Updater::new(&source, &fixture.layout, progress.clone());
   let release = updater.check(fixture.keys()).unwrap();
   progress.cancel();
   assert!(matches!(updater.install(&release), Err(UpdateError::Cancelled)));
   assert_eq!(fixture.active().as_deref(), Some(v1.as_str()));
}

#[test]
fn unchanged_files_are_trusted_from_the_installed_manifest() {
   let fixture = Fixture::new();
   let v1 = fixture.publish(&v1_files(), V1_TIME);
   let source = CountingSource::new(&fixture.release);
   fixture.update(&source).unwrap();

   let damaged = InstallLayout::file_in(&fixture.layout.app_folder(), "Sample.Core.dll");
   let mut bytes = fs::read(&damaged).unwrap();
   bytes[0] ^= 1;
   fs::write(&damaged, &bytes).unwrap();

   let mut files = v1_files();
   files[2].1 = content(60, 700);
   let v2 = fixture.publish(&files, V2_TIME);
   source.reset();

   // Only the changed file is downloaded; the unchanged one is not read, so its damage stays for Repair.
   fixture.update(&source).unwrap();
   assert_eq!(source.opened_paths(), ["runtimes/win-x64/native/sample.dll"]);
   assert_eq!(fixture.installed(&v2, "Sample.Core.dll"), bytes);

   source.reset();
   let updater = Updater::new(&source, &fixture.layout, Arc::new(UpdateProgress::new()));
   let release = updater.check(fixture.keys()).unwrap();
   updater.repair(&release).unwrap();
   assert_eq!(source.opened_paths(), ["Sample.Core.dll"]);
   assert_eq!(fixture.installed(&v2, "Sample.Core.dll"), content(2, 5000));
   assert_ne!(v1, v2);
}

#[test]
fn finishing_counts_replaced_and_deleted_files_and_repair_cleanup() {
   use launcher::install::UpdatePhase;
   let fixture = Fixture::new();
   let source = CountingSource::new(&fixture.release);
   fixture.publish(&v1_files(), V1_TIME);
   fixture.update(&source).unwrap();
   let id = fixture.publish(&[(product::APP_EXE, content(8, 500))], V2_TIME);
   let stale = fixture.layout.version_folder(&id);
   fs::create_dir(&stale).unwrap();
   fs::write(stale.join("unlisted"), b"stale").unwrap();
   let progress = Arc::new(UpdateProgress::new());
   let updater = Updater::new(&source, &fixture.layout, progress.clone());
   let release = updater.check(fixture.keys()).unwrap();
   updater.install(&release).unwrap();
   assert_eq!(progress.phase(), UpdatePhase::Finishing);
   // 1 replaced + 4 deleted + manifest + current.json + launcher + cleanup step, and the stale folder (1 unit)
   // together with the staging folder of this release (1 unit).
   assert_eq!(progress.total_bytes(), 1 + 4 + 4 + 1 + 1);
   assert_eq!(progress.done_bytes(), progress.total_bytes());
   assert!(!stale.exists());
   let leftover = fixture.layout.version_folder("old");
   fs::create_dir(&leftover).unwrap();
   fs::write(leftover.join("extra"), b"old").unwrap();
   let stale_staging = fixture.layout.staging_folder(&id);
   fs::create_dir(&stale_staging).unwrap();
   fs::write(stale_staging.join("extra"), b"stale").unwrap();
   updater.repair(&release).unwrap();
   assert_eq!(progress.total_bytes(), 6);
   assert!(!stale_staging.exists());
   assert_eq!(progress.done_bytes(), progress.total_bytes());
   assert!(!leftover.exists());
}
