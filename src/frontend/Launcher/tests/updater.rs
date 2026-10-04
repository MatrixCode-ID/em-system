// Install, update, resume, repair and clean-up scenarios of the updater, run against signed releases that the
// tests publish into temporary folders.

mod common;

use std::fs::{self, OpenOptions};
use std::os::windows::fs::OpenOptionsExt;
use std::path::Path;
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

   fn installed(&self, id: &str, path: &str) -> Vec<u8> {
      fs::read(InstallLayout::file_in(&self.layout.version_folder(id), path)).unwrap()
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

   // The manifest and its signature travel with the version, byte for byte.
   let version = fixture.layout.version_folder(&id);
   for name in [ReleaseLayout::MANIFEST_FILE_NAME, ReleaseLayout::SIGNATURE_FILE_NAME] {
      assert_eq!(
         fs::read(version.join(name)).unwrap(),
         fs::read(fixture.release.join(name)).unwrap()
      );
   }

   // The release's launcher becomes the root launcher, and nothing is left staged.
   assert_eq!(fs::read(fixture.layout.launcher_path()).unwrap(), content(4, 900));
   let folders = fixture.layout.version_folders().unwrap();
   assert_eq!(folders.len(), 1);
   assert!(!folders[0].is_staging);

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
   assert!(!fixture.layout.version_folder(&v2).exists());
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

   let version = fixture.layout.version_folder(&id);
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
fn a_version_in_use_is_removed_on_a_later_run() {
   let fixture = Fixture::new();
   let v1 = fixture.publish(&v1_files(), V1_TIME);
   let source = CountingSource::new(&fixture.release);
   fixture.update(&source).unwrap();

   // A stale staging folder of a release that never finished.
   let stale = fixture.layout.staging_folder("000000000000");
   fs::create_dir_all(&stale).unwrap();

   let mut files = v1_files();
   files[0].1 = content(40, 3000);
   files[4].1 = content(41, 950);
   let v2 = fixture.publish(&files, V2_TIME);

   {
      // The old app is still running: its exe is open without sharing, so its folder cannot go yet.
      let _running = OpenOptions::new()
         .read(true)
         .share_mode(0)
         .open(InstallLayout::file_in(
            &fixture.layout.version_folder(&v1),
            product::APP_EXE,
         ))
         .unwrap();
      fixture.update(&source).unwrap();
      assert_eq!(fixture.active().as_deref(), Some(v2.as_str()));
      assert!(fixture.layout.version_folder(&v1).exists());
      assert!(!stale.exists());
   }

   // The root launcher was replaced, and the previous one parked as launcher.old.exe.
   assert_eq!(fs::read(fixture.layout.launcher_path()).unwrap(), content(41, 950));
   assert_eq!(fs::read(fixture.layout.old_launcher_path()).unwrap(), content(4, 900));

   let updater = Updater::new(&source, &fixture.layout, Arc::new(UpdateProgress::new()));
   updater.remove_leftovers();
   assert!(!fixture.layout.version_folder(&v1).exists());
   assert!(!fixture.layout.old_launcher_path().exists());
   assert!(fixture.layout.version_folder(&v2).exists());
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
fn a_damaged_active_copy_is_downloaded_instead_of_copied() {
   let fixture = Fixture::new();
   let v1 = fixture.publish(&v1_files(), V1_TIME);
   let source = CountingSource::new(&fixture.release);
   fixture.update(&source).unwrap();

   let damaged = InstallLayout::file_in(&fixture.layout.version_folder(&v1), "Sample.Core.dll");
   let mut bytes = fs::read(&damaged).unwrap();
   bytes[0] ^= 1;
   fs::write(&damaged, bytes).unwrap();

   let mut files = v1_files();
   files[2].1 = content(60, 700);
   let v2 = fixture.publish(&files, V2_TIME);
   source.reset();

   fixture.update(&source).unwrap();
   assert_eq!(
      source.opened_paths(),
      ["Sample.Core.dll", "runtimes/win-x64/native/sample.dll"]
   );
   assert_eq!(fixture.installed(&v2, "Sample.Core.dll"), content(2, 5000));
   assert!(Path::new(&fixture.layout.version_folder(&v2)).exists());
}
