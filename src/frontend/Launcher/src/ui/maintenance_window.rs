use std::cell::{Cell, RefCell};
use std::io;
use std::rc::Rc;
use std::thread::{self, JoinHandle};

use winsafe::{co, gui, prelude::*};

use crate::cli::CommandError;
use crate::commands::{CommandContext, KeyCommand};
use crate::config::{KeyScope, LauncherConfig, TrustedKey, TrustedKeys};
use crate::install::{InstallLayout, VerifiedRelease};
use crate::launcher_log::LauncherLog;
use crate::product;
use crate::source;

use super::{FileDialogs, KeyListView, Prompts, WindowStyle, format_published, sentence};

const TIMER_ID: usize = 1;
const TIMER_MS: u32 = 200;
const WIDTH: i32 = 540;
const HEIGHT: i32 = 352;
const MARGIN: i32 = 16;
const BUTTON_WIDTH: i32 = 88;
const GAP: i32 = 8;

/// Pekerjaan yang dipilih di jendela maintenance, dijalankan setelah jendelanya tertutup.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MaintenanceAction {
   /// Tutup tanpa pekerjaan lanjutan (perubahan sumber dan key sudah tersimpan di jendela itu sendiri).
   Close,

   /// Jalankan Repair.
   Repair,

   /// Jalankan Uninstall (dengan konfirmasinya sendiri).
   Uninstall,
}

/// Jendela maintenance, target **Modify** di Apps & Features: versi terpasang, folder instalasi, sumber rilis,
/// dan key tepercaya, dengan tombol **Repair**, **Uninstall**, dan **Close**.
///
/// Mengganti sumber (**Save**) memeriksa rilis di sumber baru lebih dulu di thread pekerja; kalau tidak lolos,
/// user ditanya apakah tetap mau menyimpannya. Import dan Remove key langsung tersimpan. Repair dan Uninstall
/// menutup jendela ini dan dikerjakan pemanggil.
#[derive(Clone)]
pub struct MaintenanceWindow {
   wnd: gui::WindowMain,
   heading: gui::Label,
   source: gui::Edit,
   source_browse: gui::Button,
   save: gui::Button,
   keys: KeyListView,
   import: gui::Button,
   remove: gui::Button,
   status: gui::Label,
   repair: gui::Button,
   uninstall: gui::Button,
   state: Rc<MaintenanceState>,
}

// The source check running in the background: the address being checked, and the published time of its release
// or the reason it cannot be used.
type SourceCheck = (String, JoinHandle<Result<String, String>>);

struct MaintenanceState {
   context: CommandContext,
   config: RefCell<LauncherConfig>,
   keys: RefCell<Vec<TrustedKey>>,
   check: RefCell<Option<SourceCheck>>,
   action: Cell<MaintenanceAction>,
}

impl MaintenanceWindow {
   // region: Statics

   /// Membuka jendela maintenance untuk instalasi di `layout` dan memblokir sampai jendelanya ditutup.
   pub fn run(context: &CommandContext, layout: &InstallLayout) -> Result<MaintenanceAction, CommandError> {
      let state = Rc::new(MaintenanceState {
         context: context.clone(),
         config: RefCell::new(context.load_config()?),
         keys: RefCell::new(Vec::new()),
         check: RefCell::new(None),
         action: Cell::new(MaintenanceAction::Close),
      });
      let window = Self::new(state, layout);
      window.events();
      window
         .wnd
         .run_main(None)
         .map_err(|error| CommandError::io("the maintenance window failed")(io::Error::other(error.to_string())))?;
      Ok(window.state.action.get())
   }

   // endregion
}

impl MaintenanceWindow {
   fn new(state: Rc<MaintenanceState>, layout: &InstallLayout) -> Self {
      let title = format!("{} Maintenance", product::APP_NAME);
      let wnd = gui::WindowMain::new(WindowStyle::main_opts(&title, (WIDTH, HEIGHT)));
      let inner = WIDTH - 2 * MARGIN;
      let side = WIDTH - MARGIN - BUTTON_WIDTH;
      let bottom = HEIGHT - MARGIN - 26;
      let version = match layout.active_version() {
         Some(active) => format!(
            "Installed version: published {} (release {})",
            format_published(&active.current.published_at_utc),
            active.current.version
         ),
         None => "Installed version: none is active; Repair restores it".to_string(),
      };
      let config = state.config.borrow().clone();
      let source_caption = if config.is_source_locked() {
         "Release source (set by your IT policy):"
      } else {
         "Release source, a web address (https://...) or a folder:"
      };

      WindowStyle::label(&wnd, &version, (MARGIN, 48), (inner, 18));
      let folder = format!("Installed in {}", layout.root().display());
      WindowStyle::path_label(&wnd, &folder, (MARGIN, 68), inner);

      let this = Self {
         heading: WindowStyle::label(
            &wnd,
            &format!("Maintain {}", product::APP_NAME),
            (MARGIN, 14),
            (inner, 26),
         ),
         source: {
            WindowStyle::label(&wnd, source_caption, (MARGIN, 100), (inner, 18));
            WindowStyle::edit(
               &wnd,
               config.effective_source(),
               (MARGIN, 119),
               inner - 2 * (BUTTON_WIDTH + GAP),
            )
         },
         source_browse: WindowStyle::button(&wnd, "Browse...", (side - BUTTON_WIDTH - GAP, 117), 0),
         save: WindowStyle::button(&wnd, "Save", (side, 117), 0),
         keys: {
            WindowStyle::label(&wnd, "Trusted public keys:", (MARGIN, 152), (inner, 18));
            KeyListView::new(&wnd, (MARGIN, 171), (inner - BUTTON_WIDTH - GAP, 96))
         },
         import: WindowStyle::button(&wnd, "Import...", (side, 171), 0),
         remove: WindowStyle::button(&wnd, "Remove", (side, 203), 0),
         status: WindowStyle::label(&wnd, "", (MARGIN, 276), (inner, 34)),
         repair: WindowStyle::button(&wnd, "Repair", (MARGIN, bottom), 0),
         uninstall: WindowStyle::button(&wnd, "Uninstall", (MARGIN + BUTTON_WIDTH + GAP, bottom), 0),
         wnd,
         state,
      };
      // Not a field: nothing reads it again, and Esc reaches it through its IDCANCEL id.
      let close = WindowStyle::button(&this.wnd, "Close", (side, bottom), co::DLGID::CANCEL.raw());
      let wnd = this.wnd.clone();
      close.on().bn_clicked(move || {
         wnd.close();
         Ok(())
      });
      this
   }

   fn events(&self) {
      let this = self.clone();
      self.wnd.on().wm_create(move |_| {
         WindowStyle::apply_heading(&this.heading);
         if this.state.config.borrow().is_source_locked() {
            WindowStyle::set_read_only(&this.source, true);
         }
         this.reload_keys();
         this.wnd.hwnd().SetTimer(TIMER_ID, TIMER_MS, None)?;
         Ok(0)
      });

      let this = self.clone();
      self.wnd.on().wm_timer(TIMER_ID, move || {
         this.tick();
         Ok(())
      });

      let this = self.clone();
      self.source.on().en_change(move || {
         this.update_buttons();
         Ok(())
      });

      let this = self.clone();
      self.source_browse.on().bn_clicked(move || {
         let current = this.source.text().unwrap_or_default();
         if let Some(folder) = FileDialogs::pick_folder(this.wnd.hwnd(), "Choose the release folder", &current) {
            this.source.set_text(&folder.display().to_string())?;
         }
         Ok(())
      });

      let this = self.clone();
      self.save.on().bn_clicked(move || {
         this.start_source_check();
         Ok(())
      });

      let this = self.clone();
      self.keys.view().on().lvn_item_changed(move |_| {
         this.update_buttons();
         Ok(())
      });

      let this = self.clone();
      self.import.on().bn_clicked(move || {
         this.import_key();
         Ok(())
      });

      let this = self.clone();
      self.remove.on().bn_clicked(move || {
         this.remove_key();
         Ok(())
      });

      for (button, action) in [
         (&self.repair, MaintenanceAction::Repair),
         (&self.uninstall, MaintenanceAction::Uninstall),
      ] {
         let this = self.clone();
         button.on().bn_clicked(move || {
            this.state.action.set(action);
            this.wnd.close();
            Ok(())
         });
      }
   }

   fn set_status(&self, text: &str) {
      WindowStyle::set_text(&self.status, text);
   }

   fn reload_keys(&self) {
      match TrustedKeys::list(&self.state.context.location) {
         Ok(keys) => *self.state.keys.borrow_mut() = keys,
         Err(error) => self.set_status(&format!("Cannot read the trusted keys: {error}")),
      }
      let allow_user_keys = self.state.config.borrow().allow_user_keys();
      let rows: Vec<(String, String)> = self
         .state
         .keys
         .borrow()
         .iter()
         .map(|trusted| {
            let origin = match trusted.scope {
               KeyScope::Machine => "IT policy",
               KeyScope::User if allow_user_keys => "Trusted by this user",
               KeyScope::User => "Trusted by this user, ignored by the IT policy",
            };
            (trusted.key.key_id().to_string(), origin.to_string())
         })
         .collect();
      self.keys.fill(&rows);
      self.update_buttons();
   }

   // The key selected in the list, when it is one the user may remove.
   fn selected_user_key(&self) -> Option<String> {
      let index = self.keys.selected_index()?;
      let keys = self.state.keys.borrow();
      let trusted = keys.get(index)?;
      (trusted.scope == KeyScope::User).then(|| trusted.key.key_id().to_string())
   }

   fn update_buttons(&self) {
      let checking = self.state.check.borrow().is_some();
      let config = self.state.config.borrow();
      let text = self.source.text().unwrap_or_default();
      let changed = !text.trim().is_empty() && text.trim() != config.source.trim();
      WindowStyle::set_enabled(&self.save, !checking && !config.is_source_locked() && changed);
      WindowStyle::set_enabled(&self.source_browse, !checking && !config.is_source_locked());
      WindowStyle::set_enabled(&self.source, !checking);
      WindowStyle::set_enabled(&self.remove, self.selected_user_key().is_some());
      WindowStyle::set_enabled(&self.repair, !checking);
      WindowStyle::set_enabled(&self.uninstall, !checking);
   }

   fn start_source_check(&self) {
      let address = self.source.text().unwrap_or_default().trim().to_string();
      let allow_user_keys = self.state.config.borrow().allow_user_keys();
      let keys = TrustedKeys::usable(&self.state.context.location, allow_user_keys).unwrap_or_default();
      let worker = {
         let address = address.clone();
         thread::spawn(move || {
            let source = source::from_address(&address).map_err(|error| error.to_string())?;
            VerifiedRelease::fetch(source.as_ref(), &keys)
               .map(|release| release.manifest().published_at_utc().to_string())
               .map_err(|error| error.to_string())
         })
      };
      *self.state.check.borrow_mut() = Some((address, worker));
      self.set_status("Checking the release at the new source...");
      self.update_buttons();
   }

   fn tick(&self) {
      let finished = self
         .state
         .check
         .borrow()
         .as_ref()
         .is_some_and(|(_, worker)| worker.is_finished());
      if !finished {
         return;
      }
      let Some((address, worker)) = self.state.check.borrow_mut().take() else {
         return;
      };
      let result = worker.join().unwrap_or_else(|panic| std::panic::resume_unwind(panic));
      self.update_buttons();
      match result {
         Ok(published) => {
            if self.save_source(&address) {
               self.set_status(&format!(
                  "Saved. The source has the release published {}.",
                  format_published(&published)
               ));
            }
         }
         Err(error) => {
            let content = format!(
               "{}\n\nSave it anyway? Until the source can be read and its release is signed with a trusted \
                key, {} starts without looking for updates.",
               sentence(&error),
               product::APP_NAME
            );
            if Prompts::ask("The new release source cannot be used", &content, "Save anyway") {
               if self.save_source(&address) {
                  self.set_status("Saved without a successful check.");
               }
            } else {
               self.set_status(&format!("Not saved: {error}"));
            }
         }
      }
   }

   fn save_source(&self, address: &str) -> bool {
      let mut config = self.state.config.borrow_mut();
      config.source = address.to_string();
      if let Err(error) = config.save(&self.state.context.location) {
         drop(config);
         Prompts::error(&format!("Cannot save the release source: {error}"));
         return false;
      }
      LauncherLog::info(format!("The release source was changed to {address}"));
      drop(config);
      self.update_buttons();
      true
   }

   fn import_key(&self) {
      let Some(file) = FileDialogs::pick_key_file(self.wnd.hwnd(), "") else {
         return;
      };
      let key = match KeyCommand::read_file(&file) {
         Ok(key) => key,
         Err(error) => return Prompts::error(&error.message),
      };
      let known = self
         .state
         .keys
         .borrow()
         .iter()
         .any(|trusted| trusted.scope == KeyScope::User && trusted.key.key_id() == key.key_id());
      if known {
         return Prompts::info(&format!("Key {} is already trusted.", key.key_id()));
      }
      if !Prompts::confirm_import(key.key_id()) {
         return;
      }
      match TrustedKeys::add(&self.state.context.location, &key) {
         Ok(()) => {
            LauncherLog::info(format!("Trusted key {} imported", key.key_id()));
            self.reload_keys();
            self.set_status(&format!("Key {} is now trusted.", key.key_id()));
         }
         Err(error) => Prompts::error(&format!("Cannot save the key: {error}")),
      }
   }

   fn remove_key(&self) {
      let Some(key_id) = self.selected_user_key() else {
         return;
      };
      let content =
         format!("Key ID: {key_id}\n\nReleases signed with this key only will no longer be installed or updated.");
      if !Prompts::ask("Stop trusting this key?", &content, "Remove") {
         return;
      }
      match TrustedKeys::remove(&self.state.context.location, &key_id) {
         Ok(_) => {
            LauncherLog::info(format!("Trusted key {key_id} removed"));
            self.reload_keys();
            self.set_status(&format!("Key {key_id} was removed."));
         }
         Err(error) => Prompts::error(&format!("Cannot remove the key: {error}")),
      }
   }
}
