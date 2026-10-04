// Launcher configuration and trusted keys in the registry. Every test works under its own key in
// HKCU\Software (LauncherTest-<pid>-<n>) and removes it afterwards, so the real configuration of the machine is never
// touched. The "policy" key sits in HKCU too, since writing HKLM needs administrator rights.

mod common;

use std::sync::atomic::{AtomicUsize, Ordering};

use common::TestKey;
use launcher::config::{ConfigLocation, KeyScope, LauncherConfig, RegistryHive, TrustedKeys};

static NEXT: AtomicUsize = AtomicUsize::new(0);

struct TestLocation {
   root: String,
   location: ConfigLocation,
}

impl TestLocation {
   fn new() -> Self {
      let root = format!(
         r"Software\LauncherTest-{}-{}",
         std::process::id(),
         NEXT.fetch_add(1, Ordering::Relaxed)
      );
      let location = ConfigLocation {
         user_key: format!(r"{root}\User\Launcher"),
         policy_hive: RegistryHive::CurrentUser,
         policy_key: format!(r"{root}\Policy\Launcher"),
      };
      Self { root, location }
   }
}

impl Drop for TestLocation {
   fn drop(&mut self) {
      let _ = RegistryHive::CurrentUser.delete_tree(&self.root);
   }
}

#[test]
fn product_location_uses_the_application_name() {
   let location = ConfigLocation::for_product();
   assert_eq!(location.user_key, format!(r"{}\Launcher", launcher::product::APP_NAME));
   assert_eq!(location.policy_hive, RegistryHive::LocalMachine);
   assert!(location.policy_key.starts_with(r"Software\Policies\"));
}

#[test]
fn a_missing_configuration_loads_the_defaults() {
   let test = TestLocation::new();
   let config = LauncherConfig::load(&test.location).unwrap();
   assert_eq!(config, LauncherConfig::default());
   assert!(config.start_menu && config.desktop);
   assert!(config.install_layout().is_none());
   assert!(!config.is_source_locked());
   assert!(config.allow_user_keys());
}

#[test]
fn saves_and_loads_the_user_configuration() {
   let test = TestLocation::new();
   // Private fields (the policy part) rule out struct literals outside the crate, so start from default().
   let mut config = LauncherConfig::default();
   config.source = "https://server/cdn/wpf-release".into();
   config.install_folder = r"C:\Apps\Sample".into();
   config.desktop = false;
   config.save(&test.location).unwrap();

   let loaded = LauncherConfig::load(&test.location).unwrap();
   assert_eq!(loaded, config);
   assert_eq!(loaded.effective_source(), "https://server/cdn/wpf-release");
   assert_eq!(
      loaded.install_layout().unwrap().root(),
      std::path::Path::new(r"C:\Apps\Sample")
   );
}

#[test]
fn the_policy_source_overrides_and_locks_the_user_source() {
   let test = TestLocation::new();
   let mut config = LauncherConfig::default();
   config.source = r"\\old\share".into();
   config.save(&test.location).unwrap();
   RegistryHive::CurrentUser
      .write_string(&test.location.policy_key, "Source", "https://it/cdn/wpf-release")
      .unwrap();

   let config = LauncherConfig::load(&test.location).unwrap();
   assert!(config.is_source_locked());
   assert_eq!(config.effective_source(), "https://it/cdn/wpf-release");
   assert_eq!(config.source, r"\\old\share");
}

#[test]
fn adds_lists_and_removes_trusted_keys() {
   let test = TestLocation::new();
   let first = TestKey::new(11).public;
   let second = TestKey::new(12).public;

   TrustedKeys::add(&test.location, &first).unwrap();
   TrustedKeys::add(&test.location, &second).unwrap();
   TrustedKeys::add(&test.location, &first).unwrap();
   let listed = TrustedKeys::list(&test.location).unwrap();
   assert_eq!(listed.len(), 2);
   assert!(listed.iter().all(|trusted| trusted.scope == KeyScope::User));

   // The value is named after the key id and holds the PEM.
   let pem = RegistryHive::CurrentUser
      .read_string(&format!(r"{}\TrustedKeys", test.location.user_key), first.key_id())
      .unwrap();
   assert_eq!(pem, Some(first.to_pem()));

   assert!(TrustedKeys::remove(&test.location, &first.key_id().to_uppercase()).unwrap());
   assert!(!TrustedKeys::remove(&test.location, first.key_id()).unwrap());
   assert_eq!(TrustedKeys::usable(&test.location, true).unwrap(), vec![second]);
}

#[test]
fn key_ids_are_recomputed_and_broken_values_skipped() {
   let test = TestLocation::new();
   let key = TestKey::new(13).public;
   let keys = format!(r"{}\TrustedKeys", test.location.user_key);
   RegistryHive::CurrentUser
      .write_string(&keys, "0000000000000000", &key.to_pem())
      .unwrap();
   RegistryHive::CurrentUser
      .write_string(&keys, "broken", "not a key")
      .unwrap();

   let listed = TrustedKeys::list(&test.location).unwrap();
   assert_eq!(listed.len(), 1);
   assert_eq!(listed[0].key.key_id(), key.key_id());

   // Removing by the real key id finds the value despite its misleading name.
   assert!(TrustedKeys::remove(&test.location, key.key_id()).unwrap());
}

#[test]
fn policy_keys_come_first_and_can_exclude_user_keys() {
   let test = TestLocation::new();
   let machine = TestKey::new(14).public;
   let user = TestKey::new(15).public;
   RegistryHive::CurrentUser
      .write_string(
         &format!(r"{}\TrustedKeys", test.location.policy_key),
         machine.key_id(),
         &machine.to_pem(),
      )
      .unwrap();
   TrustedKeys::add(&test.location, &user).unwrap();
   TrustedKeys::add(&test.location, &machine).unwrap();

   let listed = TrustedKeys::list(&test.location).unwrap();
   assert_eq!(listed[0].scope, KeyScope::Machine);
   assert_eq!(listed.len(), 3);
   assert_eq!(
      TrustedKeys::usable(&test.location, true).unwrap(),
      vec![machine.clone(), user]
   );

   RegistryHive::CurrentUser
      .write_dword(&test.location.policy_key, "AllowUserKeys", 0)
      .unwrap();
   let config = LauncherConfig::load(&test.location).unwrap();
   assert!(!config.allow_user_keys());
   assert_eq!(
      TrustedKeys::usable(&test.location, config.allow_user_keys()).unwrap(),
      vec![machine.clone()]
   );

   // A policy key cannot be removed from the launcher; only the user's copy goes.
   assert!(TrustedKeys::remove(&test.location, machine.key_id()).unwrap());
   assert_eq!(TrustedKeys::list(&test.location).unwrap().len(), 2);
}
