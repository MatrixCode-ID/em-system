use std::io;

use crate::format::ReleasePublicKey;
use crate::launcher_log::LauncherLog;

use super::{ConfigLocation, RegistryHive};

const TRUSTED_KEYS_KEY: &str = "TrustedKeys";

/// The trusted public keys for verifying releases, stored in the registry: the IT policy's (HKLM) and the
/// user's (HKCU). The launcher carries no built-in keys; every key comes in through a `.pem` file (the
/// setup form, `--import`, or IT).
///
/// Each key is stored as a `REG_SZ` value holding the PEM, with the value name being the `keyId`
/// recalculated from the key's content. When it is read, the `keyId` is also always recalculated, so the
/// value name is never trusted.
pub struct TrustedKeys;

/// One trusted key together with where it comes from.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TrustedKey {
   /// Public key-nya.
   pub key: ReleasePublicKey,

   /// Where this key comes from.
   pub scope: KeyScope,
}

/// Asal key tepercaya.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum KeyScope {
   /// The IT policy in HKLM. It cannot be removed from the launcher.
   Machine,

   /// The user's in HKCU. Ignored when the IT policy holds `AllowUserKeys = 0`.
   User,
}

impl TrustedKeys {
   // region: Statics

   /// All stored keys, the IT policy keys first. A value whose content is not a valid P-256 public key is
   /// skipped and recorded in the log.
   pub fn list(location: &ConfigLocation) -> io::Result<Vec<TrustedKey>> {
      let mut keys = read(location.policy_hive, &location.policy_key, KeyScope::Machine)?;
      keys.extend(read(RegistryHive::CurrentUser, &location.user_key, KeyScope::User)?);
      Ok(keys)
   }

   /// The keys that may be used to verify a release: the IT policy keys, plus the user-owned keys when
   /// `allow_user_keys`. The same key does not appear twice.
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

   /// Saves `key` as a user-owned key. Saving a key that already exists only overwrites it.
   pub fn add(location: &ConfigLocation, key: &ReleasePublicKey) -> io::Result<()> {
      RegistryHive::CurrentUser.write_string(&subkey(&location.user_key), key.key_id(), &key.to_pem())
   }

   /// Deletes the user-owned key with `key_id` (case-insensitive). `Ok(false)` when there is no user-owned key
   /// with that `keyId`; an IT policy key cannot be removed from here.
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
