use std::io;

use crate::format::ReleasePublicKey;
use crate::launcher_log::LauncherLog;

use super::{ConfigLocation, RegistryHive};

const TRUSTED_KEYS_KEY: &str = "TrustedKeys";

/// Public key tepercaya untuk memverifikasi rilis, yang disimpan di registry: milik kebijakan IT (HKLM) dan
/// milik user (HKCU). Launcher tidak membawa key bawaan; semua key masuk lewat file `.pem` (form setup,
/// `--import`, atau IT).
///
/// Setiap key disimpan sebagai value `REG_SZ` berisi PEM, dengan nama value `keyId` yang dihitung ulang dari
/// isi key. Saat dibaca, `keyId` juga selalu dihitung ulang, jadi nama value tidak pernah dipercaya.
pub struct TrustedKeys;

/// Satu key tepercaya beserta asalnya.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TrustedKey {
   /// Public key-nya.
   pub key: ReleasePublicKey,

   /// Asal key ini.
   pub scope: KeyScope,
}

/// Asal key tepercaya.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum KeyScope {
   /// Kebijakan IT di HKLM. Tidak bisa dihapus dari launcher.
   Machine,

   /// Milik user di HKCU. Diabaikan kalau kebijakan IT berisi `AllowUserKeys = 0`.
   User,
}

impl TrustedKeys {
   // region: Statics

   /// Semua key yang tersimpan, key kebijakan IT lebih dulu. Value yang isinya bukan public key P-256 yang
   /// sah dilewati dan dicatat di log.
   pub fn list(location: &ConfigLocation) -> io::Result<Vec<TrustedKey>> {
      let mut keys = read(location.policy_hive, &location.policy_key, KeyScope::Machine)?;
      keys.extend(read(RegistryHive::CurrentUser, &location.user_key, KeyScope::User)?);
      Ok(keys)
   }

   /// Key yang boleh dipakai memverifikasi rilis: key kebijakan IT, ditambah key milik user kalau
   /// `allow_user_keys`. Key yang sama tidak muncul dua kali.
   pub fn usable(location: &ConfigLocation, allow_user_keys: bool) -> io::Result<Vec<ReleasePublicKey>> {
      let mut usable: Vec<ReleasePublicKey> = Vec::new();
      for trusted in Self::list(location)? {
         if (trusted.scope == KeyScope::User && !allow_user_keys)
            || usable.iter().any(|key| key.key_id() == trusted.key.key_id())
         {
            continue;
         }
         usable.push(trusted.key);
      }
      Ok(usable)
   }

   /// Menyimpan `key` sebagai key milik user. Menyimpan key yang sudah ada hanya menimpanya.
   pub fn add(location: &ConfigLocation, key: &ReleasePublicKey) -> io::Result<()> {
      RegistryHive::CurrentUser.write_string(&subkey(&location.user_key), key.key_id(), &key.to_pem())
   }

   /// Menghapus key milik user dengan `key_id` (tanpa memandang huruf besar/kecil). `Ok(false)` kalau tidak
   /// ada key milik user dengan `keyId` itu; key kebijakan IT tidak bisa dihapus dari sini.
   pub fn remove(location: &ConfigLocation, key_id: &str) -> io::Result<bool> {
      let hive = RegistryHive::CurrentUser;
      let key = subkey(&location.user_key);
      let mut removed = false;
      for (name, pem) in hive.string_values(&key)? {
         let matches = ReleasePublicKey::from_pem(&pem).map_or(name.eq_ignore_ascii_case(key_id), |parsed| {
            parsed.key_id().eq_ignore_ascii_case(key_id)
         });
         if matches {
            removed |= hive.delete_value(&key, &name)?;
         }
      }
      Ok(removed)
   }

   // endregion
}

fn subkey(key: &str) -> String {
   format!(r"{key}\{TRUSTED_KEYS_KEY}")
}

fn read(hive: RegistryHive, key: &str, scope: KeyScope) -> io::Result<Vec<TrustedKey>> {
   let mut keys = Vec::new();
   for (name, pem) in hive.string_values(&subkey(key))? {
      match ReleasePublicKey::from_pem(&pem) {
         Ok(key) => keys.push(TrustedKey { key, scope }),
         Err(error) => LauncherLog::warn(format!("Trusted key '{name}' ({scope:?}) is ignored: {error}")),
      }
   }
   Ok(keys)
}
