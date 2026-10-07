use std::io::{self, Read};
use std::time::Duration;

use ureq::http::Response;
use ureq::tls::{RootCerts, TlsConfig, TlsProvider};
use ureq::{Agent, Body, BodyReader};

use crate::format::ReleaseLayout;

use super::{ReleaseSource, SourceError, SourceStream};

// release.json and its signature are small; the budget is what a startup check can afford to wait.
const CHECK_CONNECT_TIMEOUT: Duration = Duration::from_secs(3);
const CHECK_TIMEOUT: Duration = Duration::from_secs(5);
const MAX_SMALL_FILE_SIZE: u64 = 64 * 1024 * 1024;

// ureq only offers a timeout for a whole response body, not for a single read. Downloading in chunks of
// CHUNK_SIZE bytes, each its own request with its own time budget, turns that into a stall detector: a chunk
// that does not arrive within CHUNK_TIMEOUT (about 17 KB/s) fails the download instead of hanging forever.
const DOWNLOAD_CONNECT_TIMEOUT: Duration = Duration::from_secs(10);
const CHUNK_TIMEOUT: Duration = Duration::from_secs(60);
const CHUNK_SIZE: u64 = 1024 * 1024;
const RESPONSE_TIMEOUT: Duration = Duration::from_secs(30);

/// A release folder on a CDN, read over HTTP(S) from an address like `https://server/cdn/wpf-release`.
///
/// Release files are downloaded in chunks with the `Range` header, so an interrupted download can be
/// resumed and a stalled connection is noticed. When the server does not support `Range` (it answers 200,
/// not 206), the file is downloaded whole from the start. HTTPS uses the Windows certificate store, so the
/// company's internal CAs are trusted as well. The Windows system proxy is not used.
pub struct HttpSource {
   address: String,
   base: String,
   check_agent: Agent,
   chunk_agent: Agent,
   stream_agent: Agent,
}

// What one ranged request answered.
enum RangeAnswer {
   // 206: the body holds the requested range of a file of `total` bytes.
   Partial { total: u64 },
   // 416: the offset is at or past the end of the file.
   PastEnd,
   // 200: the server ignored the Range header.
   Whole,
}

// Reads a file chunk by chunk; the body of the current chunk is kept until it runs dry.
struct ChunkReader {
   agent: Agent,
   url: String,
   next: u64,
   total: u64,
   body: Option<BodyReader<'static>>,
}

impl HttpSource {
   /// Creates a source for the release folder address `address` (`http://` or `https://`). No connection is
   /// opened here yet.
   pub fn new(address: &str) -> Self {
      Self {
         address: address.to_string(),
         base: address.trim_end_matches('/').to_string(),
         check_agent: agent(CHECK_CONNECT_TIMEOUT, |c| c.timeout_global(Some(CHECK_TIMEOUT))),
         chunk_agent: agent(DOWNLOAD_CONNECT_TIMEOUT, |c| c.timeout_global(Some(CHUNK_TIMEOUT))),
         stream_agent: agent(DOWNLOAD_CONNECT_TIMEOUT, |c| {
            c.timeout_recv_response(Some(RESPONSE_TIMEOUT))
         }),
      }
   }
}

impl ReleaseSource for HttpSource {
   fn address(&self) -> &str {
      &self.address
   }

   fn read_file(&self, name: &str) -> Result<Vec<u8>, SourceError> {
      let url = format!("{}/{}", self.base, encode_path(name));
      let mut response = self.check_agent.get(&url).call().map_err(|x| failed(&url, x))?;
      match response.status().as_u16() {
         200 => response
            .body_mut()
            .with_config()
            .limit(MAX_SMALL_FILE_SIZE)
            .read_to_vec()
            .map_err(|x| failed(&url, x)),
         404 => Err(SourceError::not_found(&url)),
         status => Err(SourceError::unavailable(format!("{url} answered HTTP {status}"))),
      }
   }

   fn open_binary(&self, path: &str, offset: u64) -> Result<SourceStream, SourceError> {
      let url = format!("{}/{}/{}", self.base, ReleaseLayout::BINARIES_FOLDER, encode_path(path));
      let mut reader = ChunkReader {
         agent: self.chunk_agent.clone(),
         url,
         next: offset,
         total: 0,
         body: None,
      };

      match reader.request()? {
         RangeAnswer::Partial { total } => {
            reader.total = total;
            Ok(SourceStream {
               offset,
               reader: Box::new(reader),
            })
         }
         // Nothing left to send; the caller compares the size and decides what that means.
         RangeAnswer::PastEnd => Ok(SourceStream {
            offset,
            reader: Box::new(io::empty()),
         }),
         RangeAnswer::Whole => {
            let response = self
               .stream_agent
               .get(&reader.url)
               .call()
               .map_err(|x| failed(&reader.url, x))?;
            match response.status().as_u16() {
               200 => Ok(SourceStream {
                  offset: 0,
                  reader: Box::new(response.into_body().into_reader()),
               }),
               404 => Err(SourceError::not_found(&reader.url)),
               status => Err(SourceError::unavailable(format!(
                  "{} answered HTTP {status}",
                  reader.url
               ))),
            }
         }
      }
   }
}

impl ChunkReader {
   // Asks for the chunk starting at `next`; on 206 its body becomes the current one.
   fn request(&mut self) -> Result<RangeAnswer, SourceError> {
      let last = self.next.saturating_add(CHUNK_SIZE - 1);
      let response = self
         .agent
         .get(&self.url)
         .header("Range", format!("bytes={}-{last}", self.next))
         .call()
         .map_err(|x| failed(&self.url, x))?;

      match response.status().as_u16() {
         206 => {
            let (start, total) = content_range(&response).ok_or_else(|| {
               SourceError::unavailable(format!("{} answered 206 without a valid Content-Range", self.url))
            })?;
            if start != self.next {
               return Err(SourceError::unavailable(format!(
                  "{} answered a range other than the one requested",
                  self.url
               )));
            }
            self.body = Some(response.into_body().into_reader());
            Ok(RangeAnswer::Partial { total })
         }
         416 => Ok(RangeAnswer::PastEnd),
         200 => Ok(RangeAnswer::Whole),
         404 => Err(SourceError::not_found(&self.url)),
         status => Err(SourceError::unavailable(format!("{} answered HTTP {status}", self.url))),
      }
   }
}

impl Read for ChunkReader {
   fn read(&mut self, buffer: &mut [u8]) -> io::Result<usize> {
      loop {
         if let Some(body) = self.body.as_mut() {
            let read = body.read(buffer)?;
            if read > 0 {
               self.next += read as u64;
               return Ok(read);
            }
            self.body = None;
         }
         if self.next >= self.total {
            return Ok(0);
         }

         // The previous chunk is used up (or its connection closed early): continue where it stopped.
         let total = self.total;
         match self.request().map_err(io::Error::other)? {
            RangeAnswer::Partial { total: now } if now == total => {}
            _ => {
               return Err(io::Error::other(format!(
                  "{} changed on the server while it was being downloaded",
                  self.url
               )));
            }
         }
      }
   }
}

type AgentConfig = ureq::config::ConfigBuilder<ureq::typestate::AgentScope>;

fn agent(connect: Duration, timeouts: impl FnOnce(AgentConfig) -> AgentConfig) -> Agent {
   let tls = TlsConfig::builder()
      .provider(TlsProvider::NativeTls)
      .root_certs(RootCerts::PlatformVerifier)
      .build();
   let builder = Agent::config_builder()
      .http_status_as_error(false)
      .timeout_connect(Some(connect))
      .tls_config(tls);
   timeouts(builder).build().into()
}

fn failed(url: &str, error: ureq::Error) -> SourceError {
   SourceError::unavailable(format!("cannot read {url}: {error}"))
}

// "bytes 0-1023/4096" gives (0, 4096).
fn content_range(response: &Response<Body>) -> Option<(u64, u64)> {
   let value = response.headers().get("content-range")?.to_str().ok()?;
   let (range, total) = value.trim().strip_prefix("bytes ")?.split_once('/')?;
   let start = range.split_once('-')?.0.trim().parse().ok()?;
   Some((start, total.trim().parse().ok()?))
}

// Percent-encodes each segment of a release path; '/' stays the separator.
fn encode_path(path: &str) -> String {
   let mut encoded = String::with_capacity(path.len());
   for byte in path.bytes() {
      if byte.is_ascii_alphanumeric() || matches!(byte, b'-' | b'.' | b'_' | b'~' | b'/') {
         encoded.push(byte as char);
      } else {
         encoded.push_str(&format!("%{byte:02X}"));
      }
   }
   encoded
}

#[cfg(test)]
mod tests {
   use super::*;

   #[test]
   fn encodes_each_path_segment() {
      assert_eq!(
         encode_path("runtimes/win-x64/a b+c.dll"),
         "runtimes/win-x64/a%20b%2Bc.dll"
      );
      assert_eq!(encode_path("straße#1.txt"), "stra%C3%9Fe%231.txt");
   }
}
