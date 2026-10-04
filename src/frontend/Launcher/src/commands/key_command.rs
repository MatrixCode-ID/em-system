use std::fs;
use std::path::Path;

use crate::cli::{CommandError, Console};
use crate::config::{KeyScope, TrustedKeys};
use crate::format::{ReleaseKeyError, ReleasePublicKey};
use crate::ui::Prompts;

use super::CommandContext;

/// Perintah pengelolaan public key tepercaya: `--import`, `--list-keys`, dan `--remove-key`. Ketiganya tidak
/// membutuhkan instalasi, jadi key bisa disiapkan sebelum install.
pub struct KeyCommand;

impl KeyCommand {
   // region: Statics

   /// Membaca public key dari file `.pem`. File berisi private key ditolak dengan peringatan, karena private
   /// key penanda tangan rilis tidak boleh sampai ke mesin client.
   pub fn read_file(file: &Path) -> Result<ReleasePublicKey, CommandError> {
      let text = fs::read_to_string(file)
         .map_err(|error| CommandError::invalid(format!("cannot read {}: {error}", file.display())))?;
      ReleasePublicKey::from_pem(&text).map_err(|error| {
         let warning = match error {
            ReleaseKeyError::PrivateKey => {
               ". A private key must never leave the machine that signs the releases; delete this copy and ask \
                for the public key instead"
            }
            _ => "",
         };
         CommandError::invalid(format!("{}: {error}{warning}", file.display()))
      })
   }

   /// `--import <file>`: mempercayai public key di `file` sebagai key milik user. Tidak senyap: user diminta
   /// mengonfirmasi `keyId`-nya dulu.
   pub fn import(context: &CommandContext, file: &Path) -> Result<(), CommandError> {
      let key = Self::read_file(file)?;
      if !context.quiet && !Prompts::confirm_import(key.key_id()) {
         return Err(CommandError::cancelled(""));
      }
      TrustedKeys::add(&context.location, &key).map_err(CommandError::io("cannot save the key"))?;

      let config = context.load_config()?;
      if !config.allow_user_keys() {
         Console::err("Warning: the IT policy ignores keys imported by the user; this key is not used.");
      }
      context.done(&format!("Trusted key {} imported.", key.key_id()));
      Ok(())
   }

   /// `--list-keys`: menulis daftar key tepercaya beserta asalnya ke console.
   pub fn list(context: &CommandContext) -> Result<(), CommandError> {
      let config = context.load_config()?;
      let keys = TrustedKeys::list(&context.location).map_err(CommandError::io("cannot read the trusted keys"))?;
      if keys.is_empty() {
         Console::out("No trusted keys.");
      }
      for trusted in keys {
         let scope = match trusted.scope {
            KeyScope::Machine => "HKLM (IT policy)",
            KeyScope::User if config.allow_user_keys() => "HKCU (user)",
            KeyScope::User => "HKCU (user, ignored by the IT policy)",
         };
         Console::out(format!("{}  {scope}", trusted.key.key_id()));
      }
      Ok(())
   }

   /// `--remove-key <keyId>`: berhenti mempercayai key milik user. Key dari kebijakan IT tidak bisa dihapus
   /// dari launcher.
   pub fn remove(context: &CommandContext, key_id: &str) -> Result<(), CommandError> {
      let removed =
         TrustedKeys::remove(&context.location, key_id).map_err(CommandError::io("cannot remove the key"))?;
      if removed {
         context.note(&format!("Trusted key {key_id} removed."));
         return Ok(());
      }

      let keys = TrustedKeys::list(&context.location).map_err(CommandError::io("cannot read the trusted keys"))?;
      let is_policy_key = keys
         .iter()
         .any(|trusted| trusted.scope == KeyScope::Machine && trusted.key.key_id().eq_ignore_ascii_case(key_id));
      Err(CommandError::invalid(if is_policy_key {
         format!("key {key_id} is set by the IT policy and cannot be removed here")
      } else {
         format!("there is no trusted key {key_id}")
      }))
   }

   // endregion
}
