// Helpers shared by the integration tests: a throwaway signing key, a publisher that writes signed release
// folders, a source that counts what it serves, and a tiny HTTP server with Range support.

// Every test file compiles its own copy of this module and uses only part of it.
#![allow(dead_code)]

use std::fs;
use std::io::{BufRead, BufReader, Read, Write};
use std::net::{TcpListener, TcpStream};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::thread;

use base64::Engine;
use base64::engine::general_purpose::STANDARD;
use launcher::format::{ReleaseHash, ReleaseLayout, ReleasePublicKey};
use launcher::source::{FolderSource, ReleaseSource, SourceError, SourceStream};
use p256::ecdsa::signature::Signer;
use p256::ecdsa::{Signature, SigningKey};
use p256::pkcs8::EncodePublicKey;

// region: Keys

/// A P-256 key made from a fixed scalar: good enough for tests, and needs no random number generator.
pub struct TestKey {
   signing: SigningKey,
   pub public: ReleasePublicKey,
}

impl TestKey {
   pub fn new(seed: u8) -> Self {
      let signing = SigningKey::from_slice(&[seed; 32]).unwrap();
      let der = signing.verifying_key().to_public_key_der().unwrap();
      Self {
         signing,
         public: ReleasePublicKey::from_spki_der(der.as_bytes()).unwrap(),
      }
   }

   pub fn sign(&self, data: &[u8]) -> String {
      let signature: Signature = self.signing.sign(data);
      STANDARD.encode(signature.to_bytes())
   }
}

// endregion

// region: Publishing

/// Replaces the release folder `root` with `files`, signed by `key`, the way the Release Manager lays it out.
/// Returns the bytes of release.json.
pub fn publish(root: &Path, files: &[(&str, Vec<u8>)], key: &TestKey, published_at_utc: &str) -> Vec<u8> {
   let binaries = root.join(ReleaseLayout::BINARIES_FOLDER);
   let _ = fs::remove_dir_all(&binaries);
   fs::create_dir_all(&binaries).unwrap();

   let mut sorted: Vec<&(&str, Vec<u8>)> = files.iter().collect();
   sorted.sort_by(|a, b| a.0.cmp(b.0));

   let mut entries = Vec::new();
   for (path, content) in sorted {
      let target = binaries.join(path.replace('/', "\\"));
      fs::create_dir_all(target.parent().unwrap()).unwrap();
      fs::write(&target, content).unwrap();
      entries.push(format!(
         r#"    {{ "path": {}, "size": {}, "sha256": "{}" }}"#,
         serde_json::to_string(path).unwrap(),
         content.len(),
         ReleaseHash::sha256_hex(content)
      ));
   }
   let manifest = format!(
      "{{\n  \"publishedAtUtc\": \"{published_at_utc}\",\n  \"files\": [\n{}\n  ]\n}}\n",
      entries.join(",\n")
   );
   let signature = format!(
      "{{\n  \"keyId\": \"{}\",\n  \"signature\": \"{}\"\n}}\n",
      key.public.key_id(),
      key.sign(manifest.as_bytes())
   );

   fs::write(root.join(ReleaseLayout::MANIFEST_FILE_NAME), &manifest).unwrap();
   fs::write(root.join(ReleaseLayout::SIGNATURE_FILE_NAME), signature).unwrap();
   manifest.into_bytes()
}

/// Deterministic content of `size` bytes, different for every `seed`.
pub fn content(seed: u8, size: usize) -> Vec<u8> {
   (0..size)
      .map(|i| (i as u8).wrapping_mul(31).wrapping_add(seed))
      .collect()
}

// endregion

// region: Counting source

/// A folder source that records every binary it is asked for, as (path, offset).
pub struct CountingSource {
   inner: FolderSource,
   pub opened: Mutex<Vec<(String, u64)>>,
}

impl CountingSource {
   pub fn new(root: &Path) -> Self {
      Self {
         inner: FolderSource::new(root.to_str().unwrap()),
         opened: Mutex::new(Vec::new()),
      }
   }

   pub fn opened_paths(&self) -> Vec<String> {
      let mut paths: Vec<String> = self
         .opened
         .lock()
         .unwrap()
         .iter()
         .map(|(path, _)| path.clone())
         .collect();
      paths.sort();
      paths
   }

   pub fn reset(&self) {
      self.opened.lock().unwrap().clear();
   }
}

impl ReleaseSource for CountingSource {
   fn address(&self) -> &str {
      self.inner.address()
   }

   fn read_file(&self, name: &str) -> Result<Vec<u8>, SourceError> {
      self.inner.read_file(name)
   }

   fn open_binary(&self, path: &str, offset: u64) -> Result<SourceStream, SourceError> {
      self.opened.lock().unwrap().push((path.to_string(), offset));
      self.inner.open_binary(path, offset)
   }
}

// endregion

// region: HTTP server

/// Serves the files under a folder over plain HTTP on 127.0.0.1, one request per connection. With
/// `honor_range` false it ignores Range headers and always answers 200, like a server without range support.
pub struct TestServer {
   pub url: String,
   pub requests: Arc<AtomicUsize>,
}

impl TestServer {
   pub fn start(root: &Path, honor_range: bool) -> Self {
      let listener = TcpListener::bind("127.0.0.1:0").unwrap();
      let url = format!("http://{}", listener.local_addr().unwrap());
      let requests = Arc::new(AtomicUsize::new(0));
      let root = root.to_path_buf();
      let counter = requests.clone();
      // The thread lives as long as the test process; the listener never closes on its own.
      thread::spawn(move || {
         for stream in listener.incoming().flatten() {
            counter.fetch_add(1, Ordering::Relaxed);
            let _ = serve(stream, &root, honor_range);
         }
      });
      Self { url, requests }
   }
}

fn serve(mut stream: TcpStream, root: &Path, honor_range: bool) -> std::io::Result<()> {
   let mut reader = BufReader::new(stream.try_clone()?);
   let mut request_line = String::new();
   reader.read_line(&mut request_line)?;
   let mut range = None;
   loop {
      let mut line = String::new();
      if reader.read_line(&mut line)? == 0 || line.trim().is_empty() {
         break;
      }
      if let Some((name, value)) = line.split_once(':')
         && name.trim().eq_ignore_ascii_case("range")
      {
         range = Some(value.trim().to_string());
      }
   }

   let target = request_line.split_whitespace().nth(1).unwrap_or("/");
   let path: PathBuf = decode(target.trim_start_matches('/'))
      .split('/')
      .fold(root.to_path_buf(), |path, segment| path.join(segment));
   let Ok(body) = fs::read(&path) else {
      return respond(&mut stream, "404 Not Found", &[], &[]);
   };
   let total = body.len() as u64;

   let requested = range
      .filter(|_| honor_range)
      .and_then(|range| range.strip_prefix("bytes=").map(str::to_string))
      .and_then(|range| {
         let (start, end) = range.split_once('-')?;
         Some((start.parse::<u64>().ok()?, end.parse::<u64>().ok()))
      });
   match requested {
      None => respond(&mut stream, "200 OK", &[], &body),
      Some((start, _)) if start >= total => respond(
         &mut stream,
         "416 Range Not Satisfiable",
         &[format!("Content-Range: bytes */{total}")],
         &[],
      ),
      Some((start, end)) => {
         let end = end.unwrap_or(total - 1).min(total - 1);
         respond(
            &mut stream,
            "206 Partial Content",
            &[format!("Content-Range: bytes {start}-{end}/{total}")],
            &body[start as usize..=end as usize],
         )
      }
   }
}

fn respond(stream: &mut TcpStream, status: &str, headers: &[String], body: &[u8]) -> std::io::Result<()> {
   let mut head = format!(
      "HTTP/1.1 {status}\r\nContent-Length: {}\r\nConnection: close\r\n",
      body.len()
   );
   for header in headers {
      head.push_str(header);
      head.push_str("\r\n");
   }
   head.push_str("\r\n");
   stream.write_all(head.as_bytes())?;
   stream.write_all(body)?;
   stream.flush()?;
   // Drain whatever the client still sends, so closing does not reset the connection under it.
   let _ = stream.shutdown(std::net::Shutdown::Write);
   let _ = stream.read(&mut [0u8; 64]);
   Ok(())
}

fn decode(text: &str) -> String {
   let bytes = text.as_bytes();
   let mut decoded = Vec::with_capacity(bytes.len());
   let mut i = 0;
   while i < bytes.len() {
      if bytes[i] == b'%'
         && let Some(hex) = text.get(i + 1..i + 3)
         && let Ok(byte) = u8::from_str_radix(hex, 16)
      {
         decoded.push(byte);
         i += 3;
      } else {
         decoded.push(bytes[i]);
         i += 1;
      }
   }
   String::from_utf8_lossy(&decoded).into_owned()
}

// endregion
