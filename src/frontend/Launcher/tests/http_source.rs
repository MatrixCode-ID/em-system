// HttpSource against a local HTTP server: small files, ranged chunks, resuming, servers without Range
// support, and a whole install over HTTP.

mod common;

use std::fs;
use std::io::Read;
use std::sync::Arc;
use std::sync::atomic::Ordering;

use common::{TestKey, TestServer, content, publish};
use launcher::format::ReleaseLayout;
use launcher::install::{InstallLayout, UpdateOutcome, UpdateProgress, Updater};
use launcher::source::{self, HttpSource, ReleaseSource, SourceErrorKind};

const TIME: &str = "2026-09-27T10:15:00Z";

// Larger than two download chunks, so reading it takes several ranged requests.
const BIG_SIZE: usize = 2 * 1024 * 1024 + 12_345;

fn read_all(source: &HttpSource, path: &str, offset: u64) -> (u64, Vec<u8>) {
   let mut stream = source.open_binary(path, offset).unwrap();
   let mut bytes = Vec::new();
   stream.reader.read_to_end(&mut bytes).unwrap();
   (stream.offset, bytes)
}

#[test]
fn reads_small_files_and_reports_missing_ones() {
   let temp = tempfile::tempdir().unwrap();
   let key = TestKey::new(3);
   let manifest = publish(temp.path(), &[("a.dll", content(1, 10))], &key, TIME);
   let server = TestServer::start(temp.path(), true);

   // A trailing slash on the address is tolerated.
   let source = HttpSource::new(&format!("{}/", server.url));
   assert_eq!(source.read_file(ReleaseLayout::MANIFEST_FILE_NAME).unwrap(), manifest);
   let missing = source.read_file("nothing.json").unwrap_err();
   assert_eq!(missing.kind(), SourceErrorKind::NotFound);
   assert_eq!(
      source.open_binary("nothing.dll", 0).err().map(|error| error.kind()),
      Some(SourceErrorKind::NotFound)
   );
}

#[test]
fn downloads_in_chunks_and_resumes_from_an_offset() {
   let temp = tempfile::tempdir().unwrap();
   let big = content(5, BIG_SIZE);
   publish(
      temp.path(),
      &[("folder with space/big file.bin", big.clone())],
      &TestKey::new(3),
      TIME,
   );
   let server = TestServer::start(temp.path(), true);
   let source = HttpSource::new(&server.url);

   let (offset, bytes) = read_all(&source, "folder with space/big file.bin", 0);
   assert_eq!(offset, 0);
   assert_eq!(bytes, big);
   assert_eq!(
      server.requests.load(Ordering::Relaxed),
      3,
      "one request per 1 MiB chunk"
   );

   let (offset, bytes) = read_all(&source, "folder with space/big file.bin", 1_500_000);
   assert_eq!(offset, 1_500_000);
   assert_eq!(bytes, &big[1_500_000..]);

   // Resuming a download that already has every byte.
   let (offset, bytes) = read_all(&source, "folder with space/big file.bin", BIG_SIZE as u64);
   assert_eq!(offset, BIG_SIZE as u64);
   assert!(bytes.is_empty());
}

#[test]
fn starts_over_when_the_server_ignores_ranges() {
   let temp = tempfile::tempdir().unwrap();
   let big = content(6, BIG_SIZE);
   publish(temp.path(), &[("big.bin", big.clone())], &TestKey::new(3), TIME);
   let server = TestServer::start(temp.path(), false);
   let source = HttpSource::new(&server.url);

   let (offset, bytes) = read_all(&source, "big.bin", 1000);
   assert_eq!(offset, 0);
   assert_eq!(bytes, big);
}

#[test]
fn installs_over_http() {
   let temp = tempfile::tempdir().unwrap();
   let release = temp.path().join("cdn").join("wpf-release");
   let key = TestKey::new(4);
   let files = vec![
      ("a.dll", content(1, 5000)),
      ("sub/b.dll", content(2, BIG_SIZE)),
      ("empty.txt", Vec::new()),
   ];
   let id = InstallLayout::release_id(&publish(&release, &files, &key, TIME));
   let server = TestServer::start(&temp.path().join("cdn"), true);

   let address = format!("{}/wpf-release", server.url);
   let source = source::from_address(&address).unwrap();
   assert_eq!(source.address(), address);
   let layout = InstallLayout::new(temp.path().join("install"));
   let updater = Updater::new(source.as_ref(), &layout, Arc::new(UpdateProgress::new()));
   let checked = updater.check(std::slice::from_ref(&key.public)).unwrap();
   assert!(matches!(
      updater.install(&checked).unwrap(),
      UpdateOutcome::Installed(_)
   ));

   for (path, bytes) in &files {
      assert_eq!(
         &fs::read(InstallLayout::file_in(&layout.version_folder(&id), path)).unwrap(),
         bytes
      );
   }
}

#[test]
fn an_unreachable_server_is_unavailable() {
   // Nothing listens on port 9 of the loopback address, so the connection is refused at once.
   let source = HttpSource::new("http://127.0.0.1:9/wpf-release");
   let error = source.read_file(ReleaseLayout::MANIFEST_FILE_NAME).unwrap_err();
   assert_eq!(error.kind(), SourceErrorKind::Unavailable);
}
