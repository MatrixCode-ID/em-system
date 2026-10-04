use std::io;

use winsafe::{self as w, RegistryValue, co};

/// Hive registry tempat konfigurasi launcher dibaca atau ditulis, beserta operasi kecil yang dibutuhkan
/// launcher. Semua kesalahan Win32 diterjemahkan ke `io::Error`, dan key atau value yang tidak ada dibaca
/// sebagai `None`, bukan kesalahan.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RegistryHive {
   /// `HKEY_CURRENT_USER`: pengaturan milik user, bisa ditulis tanpa hak admin.
   CurrentUser,

   /// `HKEY_LOCAL_MACHINE`: kebijakan yang ditulis IT. Launcher hanya membacanya.
   LocalMachine,
}

impl RegistryHive {
   // region: Methods

   /// Membaca value `REG_SZ` bernama `name` di `key`. `None` kalau key atau value-nya tidak ada.
   pub fn read_string(self, key: &str, name: &str) -> io::Result<Option<String>> {
      match self.hkey().RegGetValue(Some(key), Some(name), co::RRF::RT_REG_SZ) {
         Ok(RegistryValue::Sz(value)) => Ok(Some(value)),
         Ok(_) => Ok(None),
         Err(co::ERROR::FILE_NOT_FOUND) => Ok(None),
         Err(error) => Err(to_io(error)),
      }
   }

   /// Membaca value `REG_DWORD` bernama `name` di `key`. `None` kalau key atau value-nya tidak ada.
   pub fn read_dword(self, key: &str, name: &str) -> io::Result<Option<u32>> {
      match self.hkey().RegGetValue(Some(key), Some(name), co::RRF::RT_REG_DWORD) {
         Ok(RegistryValue::Dword(value)) => Ok(Some(value)),
         Ok(_) => Ok(None),
         Err(co::ERROR::FILE_NOT_FOUND) => Ok(None),
         Err(error) => Err(to_io(error)),
      }
   }

   /// Menulis value `REG_SZ`; key-nya dibuat kalau belum ada. String kosong menghapus value-nya, sehingga
   /// [`Self::read_string`] mengembalikan `None`.
   pub fn write_string(self, key: &str, name: &str, value: &str) -> io::Result<()> {
      if value.is_empty() {
         // winsafe passes a null buffer for "", which RegSetValueExW rejects with ERROR_NOACCESS.
         return self.delete_value(key, name).map(|_| ());
      }
      self.write(key, name, RegistryValue::Sz(value.to_string()))
   }

   /// Menulis value `REG_DWORD`; key-nya dibuat kalau belum ada.
   pub fn write_dword(self, key: &str, name: &str, value: u32) -> io::Result<()> {
      self.write(key, name, RegistryValue::Dword(value))
   }

   /// Menghapus value `name` di `key`. `Ok(false)` kalau value-nya memang tidak ada.
   pub fn delete_value(self, key: &str, name: &str) -> io::Result<bool> {
      let opened = match self
         .hkey()
         .RegOpenKeyEx(Some(key), co::REG_OPTION::NoValue, co::KEY::SET_VALUE)
      {
         Ok(opened) => opened,
         Err(co::ERROR::FILE_NOT_FOUND) => return Ok(false),
         Err(error) => return Err(to_io(error)),
      };
      match opened.RegDeleteValue(Some(name)) {
         Ok(()) => Ok(true),
         Err(co::ERROR::FILE_NOT_FOUND) => Ok(false),
         Err(error) => Err(to_io(error)),
      }
   }

   /// Semua value `REG_SZ` di `key` sebagai pasangan `(nama, isi)`. Value jenis lain dilewati; key yang tidak
   /// ada menghasilkan daftar kosong.
   pub fn string_values(self, key: &str) -> io::Result<Vec<(String, String)>> {
      let opened = match self
         .hkey()
         .RegOpenKeyEx(Some(key), co::REG_OPTION::NoValue, co::KEY::READ)
      {
         Ok(opened) => opened,
         Err(co::ERROR::FILE_NOT_FOUND) => return Ok(Vec::new()),
         Err(error) => return Err(to_io(error)),
      };

      let mut values = Vec::new();
      for entry in opened.RegEnumValue().map_err(to_io)? {
         let (name, kind) = entry.map_err(to_io)?;
         if kind != co::REG::SZ {
            continue;
         }
         if let RegistryValue::Sz(data) = opened.RegQueryValueEx(Some(&name)).map_err(to_io)? {
            values.push((name, data));
         }
      }
      Ok(values)
   }

   /// Menghapus `key` beserta semua isinya. Key yang tidak ada tidak dianggap kesalahan.
   pub fn delete_tree(self, key: &str) -> io::Result<()> {
      match self.hkey().RegDeleteTree(Some(key)) {
         Ok(()) | Err(co::ERROR::FILE_NOT_FOUND) => Ok(()),
         Err(error) => Err(to_io(error)),
      }
   }

   // endregion
}

impl RegistryHive {
   fn hkey(self) -> w::HKEY {
      match self {
         Self::CurrentUser => w::HKEY::CURRENT_USER,
         Self::LocalMachine => w::HKEY::LOCAL_MACHINE,
      }
   }

   fn write(self, key: &str, name: &str, value: RegistryValue) -> io::Result<()> {
      let (created, _) = self
         .hkey()
         .RegCreateKeyEx(key, None, co::REG_OPTION::NON_VOLATILE, co::KEY::WRITE, None)
         .map_err(to_io)?;
      created.RegSetValueEx(Some(name), value).map_err(to_io)
   }
}

fn to_io(error: co::ERROR) -> io::Error {
   io::Error::from_raw_os_error(error.raw() as i32)
}
