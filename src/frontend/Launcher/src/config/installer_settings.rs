use std::fs::{self, OpenOptions};
use std::io::{self, Write};
use std::path::Path;

/// Konfigurasi Source saja di samping installer; runner tidak membaca berkas ini.
pub struct InstallerSettings;

impl InstallerSettings {
   /// Membaca INI Windows atau membuat default dengan create_new agar tidak menimpa berkas IT.
   pub fn source(folder: &Path, default_source: &str) -> io::Result<String> {
      let path = folder.join("settings.ini");
      if !path.exists() {
         match OpenOptions::new().write(true).create_new(true).open(&path) {
            Ok(mut file) => writeln!(
               file,
               "; Release source of the application. Delete this file to restore the default.\r\n[Launcher]\r\nSource={default_source}\r"
            )?,
            Err(error) if error.kind() == io::ErrorKind::AlreadyExists => {}
            Err(error) => {
               return Err(io::Error::new(
                  error.kind(),
                  format!("cannot create {}: {error}", path.display()),
               ));
            }
         }
      }
      let text = fs::read_to_string(&path)?;
      let mut launcher = false;
      for line in text.trim_start_matches('\u{feff}').lines().map(str::trim) {
         if line.starts_with(';') || line.is_empty() {
            continue;
         }
         if line.starts_with('[') {
            launcher = line.eq_ignore_ascii_case("[Launcher]");
            continue;
         }
         if launcher
            && let Some((key, value)) = line.split_once('=')
            && key.trim().eq_ignore_ascii_case("Source")
         {
            return Ok(value.trim().to_string());
         }
      }
      Err(io::Error::new(
         io::ErrorKind::InvalidData,
         format!("{} must contain [Launcher] Source=...", path.display()),
      ))
   }
}
