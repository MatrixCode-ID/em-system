use std::io;

use winsafe::{self as w, RegistryValue, co};

/// The registry hive where the launcher configuration is read or written, together with the small
/// operations the launcher needs. All Win32 errors are translated to `io::Error`, and a key or value that
/// does not exist is read as `None`, not as an error.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RegistryHive {
   /// `HKEY_CURRENT_USER`: the user's settings, which can be written without admin rights.
   CurrentUser,

   /// `HKEY_LOCAL_MACHINE`: the policy written by IT. The launcher only reads it.
   LocalMachine,
}

impl RegistryHive {
   // region: Methods

   /// Reads the `REG_SZ` value named `name` in `key`. `None` when the key or the value does not exist.
   pub fn read_string(self, key: &str, name: &str) -> io::Result<Option<String>> {
      match self.hkey().RegGetValue(Some(key), Some(name), co::RRF::RT_REG_SZ) {
         Ok(RegistryValue::Sz(value)) => Ok(Some(value)),
         Ok(_) => Ok(None),
         Err(co::ERROR::FILE_NOT_FOUND) => Ok(None),
         Err(error) => Err(to_io(error)),
      }
   }

   /// Reads the `REG_DWORD` value named `name` in `key`. `None` when the key or the value does not exist.
   pub fn read_dword(self, key: &str, name: &str) -> io::Result<Option<u32>> {
      match self.hkey().RegGetValue(Some(key), Some(name), co::RRF::RT_REG_DWORD) {
         Ok(RegistryValue::Dword(value)) => Ok(Some(value)),
         Ok(_) => Ok(None),
         Err(co::ERROR::FILE_NOT_FOUND) => Ok(None),
         Err(error) => Err(to_io(error)),
      }
   }

   /// Writes a `REG_SZ` value; its key is created when it does not exist. An empty string deletes the value,
   /// so [`Self::read_string`] returns `None`.
   pub fn write_string(self, key: &str, name: &str, value: &str) -> io::Result<()> {
      if value.is_empty() {
         // winsafe passes a null buffer for "", which RegSetValueExW rejects with ERROR_NOACCESS.
         return self.delete_value(key, name).map(|_| ());
      }
      self.write(key, name, RegistryValue::Sz(value.to_string()))
   }

   /// Writes a `REG_DWORD` value; its key is created when it does not exist.
   pub fn write_dword(self, key: &str, name: &str, value: u32) -> io::Result<()> {
      self.write(key, name, RegistryValue::Dword(value))
   }

   /// Deletes the value `name` in `key`. `Ok(false)` when the value did not exist in the first place.
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

   /// All `REG_SZ` values in `key` as `(name, content)` pairs. Values of other kinds are skipped; a key that
   /// does not exist yields an empty list.
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

   /// Deletes `key` with everything in it. A key that does not exist is not an error.
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
