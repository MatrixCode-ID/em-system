use std::cell::{Cell, RefCell};
use std::io;
use std::path::{Path, PathBuf};
use std::rc::Rc;
use std::sync::Arc;
use std::thread::{self, JoinHandle};

use winsafe::{co, gui, prelude::*};

use crate::cli::{CommandError, ExitCode, InstallOptions};
use crate::commands::{CommandContext, InstallAction, InstallCommand, InstallRequest, KeyCommand};
use crate::config::{KeyScope, LauncherConfig, TrustedKey, TrustedKeys};
use crate::format::ReleasePublicKey;
use crate::install::{InstallLayout, Uninstaller, UpdatePhase, UpdateProgress};
use crate::launcher_log::LauncherLog;
use crate::product;

use super::{FileDialogs, KeyListView, ProgressPanel, Prompts, WindowStyle, sentence};

const TIMER_ID: usize = 1;
const TIMER_MS: u32 = 200;
const WIDTH: i32 = 560;
const HEIGHT: i32 = 478;
const MARGIN: i32 = 16;
const BUTTON_WIDTH: i32 = 88;
const CAB_BUTTON_WIDTH: i32 = 128;
const GAP: i32 = 8;

/// The result of the setup form.
#[derive(Debug)]
pub enum SetupOutcome {
   /// The user closed the form without installing anything (or the install never finished).
   Cancelled,

   /// The product is installed in `layout`; `run` = the user chose to run the app after install.
   Installed { layout: InstallLayout, run: bool },

   /// The installation was removed; there is no application to start.
   Uninstalled,
}

/// The setup form (decision 7): the release source, the install folder, trusted keys, and three
/// shortcut/run choices, then a progress page and a finish page with a hint to pin to the taskbar, all in
/// one window.
///
/// **Install** checks the fields first ([`InstallCommand::prepare`]), then runs the install on a worker
/// thread. The form stays visible (inactive) while the release in the source is checked; an error in that
/// phase, including an invalid signature, is shown in the form without anything being written. Once the
/// check passes, the window switches to the progress page. An install that is cancelled or fails goes back
/// to the form, and can be repeated.
///
/// Installed products use the same four pages for Update, Repair, Clean install and Uninstall.
/// Every operation that changes the installation runs on the worker thread.
#[derive(Clone)]
pub struct SetupWindow {
   wnd: gui::WindowMain,
   heading: gui::Label,
   intro: gui::Label,
   source_label: gui::Label,
   source: gui::Edit,
   source_browse: gui::Button,
   folder_label: gui::Label,
   folder: gui::Edit,
   folder_browse: gui::Button,
   keys_label: gui::Label,
   keys: KeyListView,
   import: gui::Button,
   remove: gui::Button,
   start_menu: gui::CheckBox,
   desktop: gui::CheckBox,
   run_after: gui::CheckBox,
   action_label: gui::Label,
   actions: gui::RadioGroup,
   message: gui::Label,
   panel: ProgressPanel,
   done_folder: gui::Label,
   done_text: gui::Label,
   install: gui::Button,
   cab: gui::Button,
   run_installed: gui::Button,
   cancel: gui::Button,
   state: Rc<SetupState>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Page {
   Form,
   // The form, disabled, while the worker checks the release at the source.
   Checking,
   Installing,
   Done,
}

// A key the user brought along in this setup: not saved until the installation succeeds.
struct ImportedKey {
   key: ReleasePublicKey,
   file: String,
}

// Everything the event handlers share besides the controls. Rc + Cell/RefCell: the handlers all run on the UI
// thread, one at a time, so shared mutable state needs no locking here.
struct SetupState {
   context: CommandContext,
   config: LauncherConfig,
   saved_keys: RefCell<Vec<TrustedKey>>,
   // The installation this form updates; `None` for a new install, and after a clean installation removed it.
   installed: RefCell<Option<InstallLayout>>,
   // The result message of the last finished install, shown on the finish page of an update.
   summary: RefCell<String>,
   imported: RefCell<Vec<ImportedKey>>,
   first_message: String,
   page: Cell<Page>,
   running_action: Cell<InstallAction>,
   progress: RefCell<Arc<UpdateProgress>>,
   worker: RefCell<Option<JoinHandle<Result<String, CommandError>>>>,
   target: RefCell<PathBuf>,
   outcome: RefCell<Option<SetupOutcome>>,
}

impl SetupWindow {
   // region: Statics

   /// Opens the setup form filled in from `options` and the existing configuration, then blocks until the
   /// window is closed. `.pem` files in the exe's folder are added to the key list. The caller must hold the
   /// launcher lock.
   pub fn run(context: &CommandContext, options: &InstallOptions) -> Result<SetupOutcome, CommandError> {
      let config = context.load_config()?;
      let saved_keys =
         TrustedKeys::list(&context.location).map_err(CommandError::io("cannot read the trusted keys"))?;

      let mut messages = Vec::new();
      let mut imported: Vec<ImportedKey> = Vec::new();
      for file in key_files_beside_exe().iter().chain(&options.imports) {
         match KeyCommand::read_file(file) {
            Ok(key) => {
               let known = saved_keys.iter().any(|saved| saved.key.key_id() == key.key_id())
                  || imported.iter().any(|other| other.key.key_id() == key.key_id());
               if !known {
                  let file = file
                     .file_name()
                     .unwrap_or(file.as_os_str())
                     .to_string_lossy()
                     .into_owned();
                  imported.push(ImportedKey { key, file });
               }
            }
            Err(error) => messages.push(sentence(&error.message)),
         }
      }

      let source = match &options.source {
         Some(source) if !config.is_source_locked() => source.clone(),
         _ => config.effective_source().to_string(),
      };
      // Update mode only for a copy outside the installation; the root launcher cannot remove its own folder.
      let own = std::env::current_exe().map_err(CommandError::io("cannot locate installer"))?;
      let installed = config.install_layout().filter(|layout| !layout.contains(&own));
      let folder = match (&installed, &options.target) {
         (Some(layout), _) => layout.root().to_path_buf(),
         (None, Some(target)) => InstallRequest::absolute_target(target)?,
         (None, None) if !config.install_folder.trim().is_empty() => PathBuf::from(config.install_folder.trim()),
         (None, None) => CommandContext::default_install_folder()?,
      };
      // An update keeps the shortcut choices of the installation.
      let choices = match &installed {
         Some(_) => (config.start_menu, config.desktop, !options.no_run),
         None => (!options.no_start_menu, !options.no_desktop, !options.no_run),
      };

      let state = Rc::new(SetupState {
         context: context.clone(),
         config,
         saved_keys: RefCell::new(saved_keys),
         installed: RefCell::new(installed),
         summary: RefCell::new(String::new()),
         imported: RefCell::new(imported),
         first_message: messages.join("\n"),
         page: Cell::new(Page::Form),
         running_action: Cell::new(InstallAction::Install),
         progress: RefCell::new(Arc::new(UpdateProgress::new())),
         worker: RefCell::new(None),
         target: RefCell::new(PathBuf::new()),
         outcome: RefCell::new(None),
      });
      let window = Self::new(state, &source, &folder.display().to_string(), choices);
      window.events();
      window
         .wnd
         .run_main(None)
         .map_err(|error| CommandError::io("the setup window failed")(io::Error::other(error.to_string())))?;
      Ok(window.state.outcome.take().unwrap_or(SetupOutcome::Cancelled))
   }

   // endregion
}

impl SetupWindow {
   fn new(state: Rc<SetupState>, source: &str, folder: &str, choices: (bool, bool, bool)) -> Self {
      let title = format!("{} - Setup", product::version_display());
      let wnd = gui::WindowMain::new(WindowStyle::main_opts(&title, (WIDTH, HEIGHT)));
      super::Logo::attach(&wnd);
      let inner = WIDTH - 2 * MARGIN;
      let field = inner - BUTTON_WIDTH - GAP;
      let side = WIDTH - MARGIN - BUTTON_WIDTH;
      let bottom = HEIGHT - MARGIN - 26;
      let source_caption = if state.config.is_source_locked() {
         "Release source (set by your IT policy):"
      } else {
         "Release source, a web address (https://...) or a folder:"
      };

      Self {
         heading: WindowStyle::label(&wnd, "", (MARGIN, 14), (inner, 26)),
         intro: WindowStyle::label(&wnd, "", (MARGIN, 44), (inner, 34)),
         source_label: WindowStyle::label(&wnd, source_caption, (MARGIN, 86), (inner, 18)),
         source: WindowStyle::edit(&wnd, source, (MARGIN, 105), field),
         source_browse: WindowStyle::button(&wnd, "Browse...", (side, 103), 0),
         folder_label: WindowStyle::label(&wnd, "Install folder:", (MARGIN, 138), (inner, 18)),
         folder: WindowStyle::edit(&wnd, folder, (MARGIN, 157), field),
         folder_browse: WindowStyle::button(&wnd, "Browse...", (side, 155), 0),
         keys_label: WindowStyle::label(&wnd, "Trusted public keys:", (MARGIN, 190), (inner, 18)),
         keys: KeyListView::new(&wnd, (MARGIN, 209), (field, 84)),
         import: WindowStyle::button(&wnd, "Import...", (side, 209), 0),
         remove: WindowStyle::button(&wnd, "Remove", (side, 241), 0),
         start_menu: WindowStyle::check_box(&wnd, "Create a Start menu item", (MARGIN, 304), inner, choices.0),
         desktop: WindowStyle::check_box(&wnd, "Create a desktop shortcut", (MARGIN, 326), inner, choices.1),
         run_after: WindowStyle::check_box(
            &wnd,
            &format!("Start {} after installing", product::APP_NAME),
            (MARGIN, 348),
            inner,
            choices.2,
         ),
         action_label: WindowStyle::label(&wnd, "Action:", (MARGIN, 371), (50, 18)),
         actions: WindowStyle::actions(&wnd),
         message: WindowStyle::label(&wnd, "", (MARGIN, 398), (inner, 36)),
         panel: ProgressPanel::new(&wnd, (MARGIN, 96), inner),
         done_folder: WindowStyle::path_label(&wnd, "", (MARGIN, 52), inner),
         done_text: WindowStyle::label(&wnd, "", (MARGIN, 84), (inner, 280)),
         install: WindowStyle::button(
            &wnd,
            "Install",
            (side - BUTTON_WIDTH - GAP, bottom),
            co::DLGID::OK.raw(),
         ),
         cab: WindowStyle::wide_button(&wnd, "Download to cab", (MARGIN, bottom), CAB_BUTTON_WIDTH, 0),
         run_installed: WindowStyle::button(&wnd, "Run installed", (MARGIN + CAB_BUTTON_WIDTH + GAP, bottom), 0),
         cancel: WindowStyle::button(&wnd, "Cancel", (side, bottom), co::DLGID::CANCEL.raw()),
         wnd,
         state,
      }
   }

   fn events(&self) {
      let this = self.clone();
      self.wnd.on().wm_create(move |_| {
         super::Logo::created(&this.wnd);
         WindowStyle::apply_heading(&this.heading);
         if this.state.config.is_source_locked() {
            WindowStyle::set_read_only(&this.source, true);
            WindowStyle::set_enabled(&this.source_browse, false);
         }
         this.fill_keys();
         this.show_page(Page::Form);
         this.set_message(&this.state.first_message);
         this.wnd.hwnd().SetTimer(TIMER_ID, TIMER_MS, None)?;
         Ok(0)
      });

      let this = self.clone();
      self.wnd.on().wm_timer(TIMER_ID, move || {
         this.tick();
         Ok(())
      });

      // Closing the window is Cancel: it only closes while nothing is running.
      let this = self.clone();
      self.wnd.on().wm_close(move || {
         this.cancel_or_close();
         Ok(())
      });

      let this = self.clone();
      self.cancel.on().bn_clicked(move || {
         this.cancel_or_close();
         Ok(())
      });

      let this = self.clone();
      self.install.on().bn_clicked(move || {
         this.start_install();
         Ok(())
      });

      let this = self.clone();
      self.cab.on().bn_clicked(move || {
         let context = this.state.context.clone();
         let address = this.source.text()?;
         let result = super::ProgressWindow::owned_by(this.wnd.hwnd(), || {
            crate::commands::CabCommand::run(
               &context,
               Some(address),
               crate::commands::CabCommand::path().is_ok_and(|path| path.exists()),
            )
         });
         if let Err(error) = result {
            Prompts::error(&error.message);
         }
         this.update_cab_button();
         Ok(())
      });

      // The form stays open and keeps the launcher lock, so the app starts without checking for an update.
      let this = self.clone();
      self.run_installed.on().bn_clicked(move || {
         if let Some(layout) = this.state.installed.borrow().as_ref()
            && let Err(error) = this.state.context.start_app(layout, &[])
         {
            Prompts::error(&sentence(&error.message));
         }
         Ok(())
      });

      let this = self.clone();
      self.actions.on().bn_clicked(move || {
         this.update_install_button();
         Ok(())
      });

      let this = self.clone();
      self.keys.view().on().lvn_item_changed(move |_| {
         this.update_remove_button();
         Ok(())
      });
      let this = self.clone();
      self.remove.on().bn_clicked(move || {
         this.remove_key();
         Ok(())
      });

      for edit in [&self.source, &self.folder] {
         let this = self.clone();
         edit.on().en_change(move || {
            this.update_install_button();
            Ok(())
         });
      }

      let this = self.clone();
      self.source_browse.on().bn_clicked(move || {
         let current = this.source.text().unwrap_or_default();
         if let Some(folder) = FileDialogs::pick_folder(this.wnd.hwnd(), "Choose the release folder", &current) {
            this.source.set_text(&folder.display().to_string())?;
         }
         Ok(())
      });

      let this = self.clone();
      self.folder_browse.on().bn_clicked(move || {
         let current = this.folder.text().unwrap_or_default();
         if let Some(folder) = FileDialogs::pick_folder(this.wnd.hwnd(), "Choose the install folder", &current) {
            // A folder with other files in it cannot hold the installation; use a new folder inside it.
            let folder = if CommandContext::is_usable_install_folder(&folder) {
               folder
            } else {
               folder.join(product::APP_NAME)
            };
            this.folder.set_text(&folder.display().to_string())?;
         }
         Ok(())
      });

      let this = self.clone();
      self.import.on().bn_clicked(move || {
         this.import_key();
         Ok(())
      });
   }

   fn show_page(&self, page: Page) {
      self.state.page.set(page);
      let form = matches!(page, Page::Form | Page::Checking);
      let form_controls = [
         self.source_label.hwnd(),
         self.source.hwnd(),
         self.source_browse.hwnd(),
         self.folder_label.hwnd(),
         self.folder.hwnd(),
         self.folder_browse.hwnd(),
         self.keys_label.hwnd(),
         self.keys.view().hwnd(),
         self.import.hwnd(),
         self.remove.hwnd(),
         self.start_menu.hwnd(),
         self.desktop.hwnd(),
         self.run_after.hwnd(),
         self.action_label.hwnd(),
         self.message.hwnd(),
      ];
      for hwnd in form_controls {
         hwnd.ShowWindow(if form { co::SW::SHOW } else { co::SW::HIDE });
         // The message stays readable while the source is checked.
         if hwnd != self.message.hwnd() {
            hwnd.EnableWindow(page == Page::Form);
         }
      }
      if self.state.config.is_source_locked() {
         WindowStyle::set_enabled(&self.source_browse, false);
      }
      let installed = self.state.installed.borrow().is_some();
      WindowStyle::set_read_only(&self.folder, installed);
      if installed {
         WindowStyle::set_enabled(&self.folder_browse, false);
      }
      WindowStyle::set_visible(&self.action_label, form && installed);
      for radio in self.actions.iter() {
         WindowStyle::set_visible(radio, form && installed);
         WindowStyle::set_enabled(radio, page == Page::Form);
      }
      WindowStyle::set_visible(&self.remove, form && installed);
      self.update_remove_button();
      WindowStyle::set_visible(&self.run_installed, form && installed);
      WindowStyle::set_enabled(&self.run_installed, page == Page::Form);
      if page == Page::Form {
         self.update_cab_button();
      }
      let verb = match self.action() {
         InstallAction::Repair => "repairing",
         InstallAction::Update => "updating",
         InstallAction::Uninstall => "uninstalling",
         _ => "installing",
      };
      WindowStyle::set_text(&self.run_after, &format!("Start {} after {verb}", product::APP_NAME));
      self.panel.set_visible(page == Page::Installing);
      WindowStyle::set_visible(&self.intro, page != Page::Done);
      WindowStyle::set_visible(&self.done_folder, page == Page::Done);
      WindowStyle::set_visible(&self.done_text, page == Page::Done);
      WindowStyle::set_visible(&self.install, form);
      WindowStyle::set_visible(&self.cab, form);
      WindowStyle::set_enabled(&self.cab, page == Page::Form);
      WindowStyle::set_enabled(
         &self.cancel,
         !matches!(page, Page::Checking | Page::Installing) || self.state.progress.borrow().can_cancel(),
      );
      WindowStyle::set_text(&self.cancel, if page == Page::Done { "Finish" } else { "Cancel" });

      let (heading, intro) = match page {
         Page::Form | Page::Checking if installed => (
            format!("Maintain {}", product::APP_NAME),
            self
               .state
               .installed
               .borrow()
               .as_ref()
               .and_then(|layout| layout.read_current().ok().flatten())
               .map(|current| {
                  format!(
                     "Installed: published {} (release {}).",
                     super::format_published(&current.published_at_utc),
                     current.version
                  )
               })
               .unwrap_or_else(|| "No active release. Choose Repair to restore the installation.".to_string()),
         ),
         Page::Form | Page::Checking => (
            format!("Install {}", product::APP_NAME),
            "Choose where the application is downloaded from and where it is installed. Nothing is written until \
             the release at the source has been checked."
               .to_string(),
         ),
         Page::Installing => (
            format!("{} {}", self.state.running_action.get().running(), product::APP_NAME),
            if self.state.running_action.get() == InstallAction::Uninstall {
               "The installation and its settings are being removed. Please wait for completion.".to_string()
            } else {
               "Every file is checked against the signed release. Finishing and removal must complete before setup can close.".to_string()
            },
         ),
         Page::Done => {
            let suffix = match self.state.running_action.get() {
               InstallAction::Uninstall => "was uninstalled",
               InstallAction::Repair => "was repaired",
               InstallAction::Update => "is up to date",
               _ => "is installed",
            };
            (format!("{} {suffix}", product::APP_NAME), String::new())
         }
      };
      WindowStyle::set_text(&self.heading, &heading);
      WindowStyle::set_text(&self.intro, &intro);
      self.update_install_button();
      if page == Page::Done {
         let folder = self.state.target.borrow().display().to_string();
         WindowStyle::set_visible(
            &self.done_folder,
            self.state.running_action.get() != InstallAction::Uninstall,
         );
         WindowStyle::set_text(&self.done_folder, &format!("Installed in {folder}"));
         WindowStyle::set_text(&self.done_text, &self.done_message());
         self.cancel.hwnd().SetFocus();
      }
   }

   fn done_message(&self) -> String {
      let app = product::APP_NAME;
      if self.state.running_action.get() == InstallAction::Uninstall {
         return format!("{}\n\nChoose Finish to close setup.", self.state.summary.borrow());
      }
      if self.state.running_action.get() != InstallAction::Install {
         let finish = if self.run_after.is_checked() {
            format!("Choose Finish to close setup and start {app}.")
         } else {
            "Choose Finish to close setup.".to_string()
         };
         return format!("{}\n\n{finish}", self.state.summary.borrow());
      }
      let pin = if self.start_menu.is_checked() {
         format!(
            "To pin {app} to the taskbar, open Start, find {app}, right-click it and choose Pin to taskbar. You \
             can also right-click its taskbar button while it runs and choose Pin to taskbar."
         )
      } else {
         format!("To pin {app} to the taskbar, right-click its taskbar button while it runs and choose Pin to taskbar.")
      };
      let finish = if self.run_after.is_checked() {
         format!("Choose Finish to close setup and start {app}.")
      } else {
         "Choose Finish to close setup.".to_string()
      };
      format!("{pin} Windows does not let a setup program pin itself.\n\n{finish}")
   }

   fn set_message(&self, text: &str) {
      WindowStyle::set_text(&self.message, text);
   }

   fn fill_keys(&self) {
      let allow_user_keys = self.state.config.allow_user_keys();
      let mut rows: Vec<(String, String)> = self
         .state
         .saved_keys
         .borrow()
         .iter()
         .map(|saved| {
            let origin = match saved.scope {
               KeyScope::Machine => "IT policy",
               KeyScope::Embedded => "Embedded product key",
               KeyScope::User if allow_user_keys => "Trusted earlier on this computer",
               KeyScope::User => "Trusted earlier, ignored by the IT policy",
            };
            (saved.key.key_id().to_string(), origin.to_string())
         })
         .collect();
      for imported in self.state.imported.borrow().iter() {
         let origin = if allow_user_keys {
            imported.file.clone()
         } else {
            format!("{}, ignored by the IT policy", imported.file)
         };
         rows.push((imported.key.key_id().to_string(), origin));
      }
      self.keys.fill(&rows);
   }

   // Keys that can verify the release, without duplicates.
   fn usable_key_count(&self) -> usize {
      let allow_user_keys = self.state.config.allow_user_keys();
      let mut ids: Vec<String> = self
         .state
         .saved_keys
         .borrow()
         .iter()
         .filter(|saved| saved.scope != KeyScope::User || allow_user_keys)
         .map(|saved| saved.key.key_id().to_string())
         .collect();
      if allow_user_keys {
         ids.extend(
            self
               .state
               .imported
               .borrow()
               .iter()
               .map(|imported| imported.key.key_id().to_string()),
         );
      }
      ids.sort();
      ids.dedup();
      ids.len()
   }

   fn action(&self) -> InstallAction {
      if self.state.installed.borrow().is_none() {
         return InstallAction::Install;
      }
      match self.actions.selected_index().unwrap_or(0) {
         1 => InstallAction::Repair,
         2 => InstallAction::CleanInstall,
         3 => InstallAction::Uninstall,
         _ => InstallAction::Update,
      }
   }

   fn update_install_button(&self) {
      let filled = |edit: &gui::Edit| edit.text().is_ok_and(|text| !text.trim().is_empty());
      let action = self.action();
      let ready = self.state.page.get() == Page::Form
         && (action == InstallAction::Uninstall
            || (filled(&self.source) && filled(&self.folder) && self.usable_key_count() > 0));
      WindowStyle::set_enabled(&self.install, ready);
      WindowStyle::set_text(&self.install, action.caption());
      let verb = match action {
         InstallAction::Update => "updating",
         InstallAction::Repair => "repairing",
         InstallAction::Uninstall => "uninstalling",
         _ => "installing",
      };
      WindowStyle::set_text(&self.run_after, &format!("Start {} after {verb}", product::APP_NAME));
      WindowStyle::set_enabled(
         &self.run_after,
         self.state.page.get() == Page::Form && action != InstallAction::Uninstall,
      );
   }

   fn update_cab_button(&self) {
      let exists = crate::commands::CabCommand::path().is_ok_and(|path| path.exists());
      WindowStyle::set_text(&self.cab, if exists { "Update cab" } else { "Download to cab" });
   }

   fn selected_user_key(&self) -> Option<String> {
      let index = self.keys.selected_index()?;
      let keys = self.state.saved_keys.borrow();
      let trusted = keys.get(index)?;
      (trusted.scope == KeyScope::User).then(|| trusted.key.key_id().to_string())
   }

   fn update_remove_button(&self) {
      WindowStyle::set_enabled(
         &self.remove,
         self.state.page.get() == Page::Form && self.selected_user_key().is_some(),
      );
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
            self.reload_saved_keys();
            self.set_message(&format!("Key {key_id} was removed."));
         }
         Err(error) => Prompts::error(&format!("Cannot remove the key: {error}")),
      }
   }

   fn reload_saved_keys(&self) {
      match TrustedKeys::list(&self.state.context.location) {
         Ok(keys) => *self.state.saved_keys.borrow_mut() = keys,
         Err(error) => LauncherLog::warn(format!("Cannot read the trusted keys: {error}")),
      }
      self.fill_keys();
      self.update_remove_button();
      self.update_install_button();
   }

   fn import_key(&self) {
      let start = key_folder()
         .map(|folder| folder.display().to_string())
         .unwrap_or_default();
      let Some(file) = FileDialogs::pick_key_file(self.wnd.hwnd(), &start) else {
         return;
      };
      let key = match KeyCommand::read_file(&file) {
         Ok(key) => key,
         Err(error) => return Prompts::error(&error.message),
      };
      let known = self
         .state
         .saved_keys
         .borrow()
         .iter()
         .any(|saved| saved.key.key_id() == key.key_id())
         || self
            .state
            .imported
            .borrow()
            .iter()
            .any(|other| other.key.key_id() == key.key_id());
      if known {
         return Prompts::info(&format!("Key {} is already in the list.", key.key_id()));
      }
      if !Prompts::confirm_import(key.key_id()) {
         return;
      }
      let file = file
         .file_name()
         .unwrap_or(file.as_os_str())
         .to_string_lossy()
         .into_owned();
      self.state.imported.borrow_mut().push(ImportedKey { key, file });
      self.fill_keys();
      self.update_install_button();
   }

   fn start_install(&self) {
      // Enter reaches this button even when it is hidden.
      if self.state.page.get() != Page::Form || !self.install.hwnd().IsWindowEnabled() {
         return;
      }
      let folder = self.folder.text().unwrap_or_default();
      let target = match InstallRequest::absolute_target(Path::new(folder.trim())) {
         Ok(target) => target,
         Err(error) => return self.set_message(&sentence(&error.message)),
      };
      let action = self.action();
      if let Some(layout) = self.state.installed.borrow().as_ref() {
         let confirmed = match action {
            InstallAction::CleanInstall => Prompts::confirm_clean_install(&layout.root().display().to_string()),
            InstallAction::Uninstall => Prompts::confirm_uninstall(),
            _ => true,
         };
         if !confirmed {
            return;
         }
         if matches!(
            action,
            InstallAction::CleanInstall | InstallAction::Uninstall | InstallAction::Repair
         ) && let Err(error) = self.state.context.wait_until_app_closed(layout)
         {
            self.set_message(&sentence(&error.message));
            return;
         }
      }
      self.state.running_action.set(action);
      if action == InstallAction::Uninstall {
         let context = self.state.context.clone();
         let layout = self.state.installed.borrow().clone().unwrap();
         let progress = Arc::new(UpdateProgress::new());
         progress.begin(UpdatePhase::Removing, 0);
         *self.state.target.borrow_mut() = layout.root().to_path_buf();
         *self.state.progress.borrow_mut() = progress.clone();
         *self.state.worker.borrow_mut() = Some(thread::spawn(move || {
            Uninstaller::new(&layout)
               .remove(&context.integration, &context.app_key, &progress)
               .map_err(CommandError::io("cannot uninstall"))?;
            Ok(format!("{} was uninstalled.", product::APP_NAME))
         }));
         self.show_page(Page::Installing);
         return;
      }
      let request = InstallRequest {
         source: (!self.state.config.is_source_locked()).then(|| self.source.text().unwrap_or_default()),
         target,
         imported: self
            .state
            .imported
            .borrow()
            .iter()
            .map(|imported| imported.key.clone())
            .collect(),
         start_menu: self.start_menu.is_checked(),
         desktop: self.desktop.is_checked(),
         run: self.run_after.is_checked(),
      };
      let job = match InstallCommand::prepare(&self.state.context, &request) {
         Ok(job) => job,
         Err(error) => return self.set_message(&sentence(&error.message)),
      };

      // A fresh progress for every try: a cancelled one stays cancelled.
      let progress = Arc::new(UpdateProgress::new());
      *self.state.progress.borrow_mut() = progress.clone();
      *self.state.target.borrow_mut() = request.target;
      let context = self.state.context.clone();
      *self.state.worker.borrow_mut() = Some(thread::spawn(move || job.execute_action(&context, progress, action)));
      self.set_message("Checking the release at the source...");
      self.show_page(Page::Checking);
   }

   fn tick(&self) {
      let page = self.state.page.get();
      if !matches!(page, Page::Checking | Page::Installing) {
         return;
      }
      let progress = self.state.progress.borrow().clone();
      if page == Page::Checking && !matches!(progress.phase(), UpdatePhase::Idle | UpdatePhase::Checking) {
         self.show_page(Page::Installing);
      }
      if self.state.page.get() == Page::Installing {
         self.panel.refresh(&progress);
      }
      WindowStyle::set_enabled(&self.cancel, progress.can_cancel() && !progress.is_cancelled());

      let finished = self
         .state
         .worker
         .borrow()
         .as_ref()
         .is_some_and(|worker| worker.is_finished());
      if !finished {
         return;
      }
      let Some(worker) = self.state.worker.borrow_mut().take() else {
         return;
      };
      let result = worker.join().unwrap_or_else(|panic| std::panic::resume_unwind(panic));
      match result {
         Ok(summary) => {
            self.state.context.note(&summary);
            *self.state.summary.borrow_mut() = summary;
            *self.state.outcome.borrow_mut() = Some(if self.state.running_action.get() == InstallAction::Uninstall {
               SetupOutcome::Uninstalled
            } else {
               SetupOutcome::Installed {
                  layout: InstallLayout::new(self.state.target.borrow().as_path()),
                  run: self.run_after.is_checked(),
               }
            });
            self.show_page(Page::Done);
         }
         Err(error) if error.code == ExitCode::Cancelled => {
            LauncherLog::info("The user cancelled the installation");
            self.refresh_after_failure();
            self.show_page(Page::Form);
            self.set_message("The operation was cancelled. Choose an action to continue.");
         }
         Err(error) => {
            LauncherLog::error(format!("The installation failed: {error}"));
            self.refresh_after_failure();
            self.show_page(Page::Form);
            self.set_message(&sentence(&error.message));
         }
      }
   }

   fn refresh_after_failure(&self) {
      let layout = InstallLayout::new(self.state.target.borrow().as_path());
      *self.state.installed.borrow_mut() = layout.current_path().is_file().then_some(layout);
      self.reload_saved_keys();
   }

   fn cancel_or_close(&self) {
      match self.state.page.get() {
         Page::Form | Page::Done => {
            let _ = self.wnd.hwnd().DestroyWindow();
         }
         Page::Checking | Page::Installing => {
            let progress = self.state.progress.borrow();
            if progress.can_cancel() {
               progress.cancel();
               WindowStyle::set_enabled(&self.cancel, false);
            }
         }
      }
   }
}

// The folder of the running exe: the setup package, where its public key file usually sits.
fn key_folder() -> Option<PathBuf> {
   std::env::current_exe().ok()?.parent().map(Path::to_path_buf)
}

fn key_files_beside_exe() -> Vec<PathBuf> {
   let Some(entries) = key_folder().and_then(|folder| std::fs::read_dir(folder).ok()) else {
      return Vec::new();
   };
   let mut files: Vec<PathBuf> = entries
      .filter_map(Result::ok)
      .map(|entry| entry.path())
      .filter(|path| {
         path.is_file()
            && path
               .extension()
               .is_some_and(|extension| extension.eq_ignore_ascii_case("pem"))
      })
      .collect();
   files.sort();
   files
}
