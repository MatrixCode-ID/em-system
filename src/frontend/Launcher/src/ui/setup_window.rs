use std::cell::{Cell, RefCell};
use std::io;
use std::path::{Path, PathBuf};
use std::rc::Rc;
use std::sync::Arc;
use std::thread::{self, JoinHandle};

use winsafe::{co, gui, prelude::*};

use crate::cli::{CommandError, ExitCode, InstallOptions};
use crate::commands::{CommandContext, InstallCommand, InstallRequest, KeyCommand};
use crate::config::{KeyScope, LauncherConfig, TrustedKey, TrustedKeys};
use crate::format::ReleasePublicKey;
use crate::install::{InstallLayout, UpdatePhase, UpdateProgress};
use crate::launcher_log::LauncherLog;
use crate::product;

use super::{FileDialogs, KeyListView, ProgressPanel, Prompts, WindowStyle, sentence};

const TIMER_ID: usize = 1;
const TIMER_MS: u32 = 200;
const WIDTH: i32 = 540;
const HEIGHT: i32 = 456;
const MARGIN: i32 = 16;
const BUTTON_WIDTH: i32 = 88;
const GAP: i32 = 8;

/// The result of the setup form.
#[derive(Debug)]
pub enum SetupOutcome {
   /// The user closed the form without installing anything (or the install never finished).
   Cancelled,

   /// The product is installed in `layout`; `run` = the user chose to run the app after install.
   Installed { layout: InstallLayout, run: bool },
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
   start_menu: gui::CheckBox,
   desktop: gui::CheckBox,
   run_after: gui::CheckBox,
   message: gui::Label,
   panel: ProgressPanel,
   done_folder: gui::Label,
   done_text: gui::Label,
   install: gui::Button,
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
   saved_keys: Vec<TrustedKey>,
   imported: RefCell<Vec<ImportedKey>>,
   first_message: String,
   page: Cell<Page>,
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
      let folder = match &options.target {
         Some(target) => InstallRequest::absolute_target(target)?,
         None if !config.install_folder.trim().is_empty() => PathBuf::from(config.install_folder.trim()),
         None => CommandContext::default_install_folder()?,
      };

      let state = Rc::new(SetupState {
         context: context.clone(),
         config,
         saved_keys,
         imported: RefCell::new(imported),
         first_message: messages.join("\n"),
         page: Cell::new(Page::Form),
         progress: RefCell::new(Arc::new(UpdateProgress::new())),
         worker: RefCell::new(None),
         target: RefCell::new(PathBuf::new()),
         outcome: RefCell::new(None),
      });
      let window = Self::new(state, &source, &folder.display().to_string(), options);
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
   fn new(state: Rc<SetupState>, source: &str, folder: &str, options: &InstallOptions) -> Self {
      let title = format!("{} Setup", product::APP_NAME);
      let wnd = gui::WindowMain::new(WindowStyle::main_opts(&title, (WIDTH, HEIGHT)));
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
         start_menu: WindowStyle::check_box(
            &wnd,
            "Create a Start menu item",
            (MARGIN, 304),
            inner,
            !options.no_start_menu,
         ),
         desktop: WindowStyle::check_box(
            &wnd,
            "Create a desktop shortcut",
            (MARGIN, 326),
            inner,
            !options.no_desktop,
         ),
         run_after: WindowStyle::check_box(
            &wnd,
            &format!("Start {} after installing", product::APP_NAME),
            (MARGIN, 348),
            inner,
            !options.no_run,
         ),
         message: WindowStyle::label(&wnd, "", (MARGIN, 376), (inner, 36)),
         panel: ProgressPanel::new(&wnd, (MARGIN, 96), inner),
         done_folder: WindowStyle::path_label(&wnd, "", (MARGIN, 52), inner),
         done_text: WindowStyle::label(&wnd, "", (MARGIN, 84), (inner, 280)),
         install: WindowStyle::button(
            &wnd,
            "Install",
            (side - BUTTON_WIDTH - GAP, bottom),
            co::DLGID::OK.raw(),
         ),
         cancel: WindowStyle::button(&wnd, "Cancel", (side, bottom), co::DLGID::CANCEL.raw()),
         wnd,
         state,
      }
   }

   fn events(&self) {
      let this = self.clone();
      self.wnd.on().wm_create(move |_| {
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
         self.start_menu.hwnd(),
         self.desktop.hwnd(),
         self.run_after.hwnd(),
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
      self.panel.set_visible(page == Page::Installing);
      WindowStyle::set_visible(&self.intro, page != Page::Done);
      WindowStyle::set_visible(&self.done_folder, page == Page::Done);
      WindowStyle::set_visible(&self.done_text, page == Page::Done);
      WindowStyle::set_visible(&self.install, form);
      WindowStyle::set_enabled(&self.cancel, true);
      WindowStyle::set_text(&self.cancel, if page == Page::Done { "Finish" } else { "Cancel" });

      let (heading, intro) = match page {
         Page::Form | Page::Checking => (
            format!("Install {}", product::APP_NAME),
            "Choose where the application is downloaded from and where it is installed. Nothing is written until \
             the release at the source has been checked."
               .to_string(),
         ),
         Page::Installing => (
            format!("Installing {}", product::APP_NAME),
            "Every file is checked against the signed release. If you cancel, the files downloaded so far are \
             kept, and the next try continues from there."
               .to_string(),
         ),
         Page::Done => (format!("{} is installed", product::APP_NAME), String::new()),
      };
      WindowStyle::set_text(&self.heading, &heading);
      WindowStyle::set_text(&self.intro, &intro);
      self.update_install_button();
      if page == Page::Done {
         let folder = self.state.target.borrow().display().to_string();
         WindowStyle::set_text(&self.done_folder, &format!("Installed in {folder}"));
         WindowStyle::set_text(&self.done_text, &self.done_message());
         self.cancel.hwnd().SetFocus();
      }
   }

   fn done_message(&self) -> String {
      let app = product::APP_NAME;
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
         .iter()
         .map(|saved| {
            let origin = match saved.scope {
               KeyScope::Machine => "IT policy",
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
         .iter()
         .filter(|saved| saved.scope == KeyScope::Machine || allow_user_keys)
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

   fn update_install_button(&self) {
      let filled = |edit: &gui::Edit| edit.text().is_ok_and(|text| !text.trim().is_empty());
      let ready = self.state.page.get() == Page::Form
         && filled(&self.source)
         && filled(&self.folder)
         && self.usable_key_count() > 0;
      WindowStyle::set_enabled(&self.install, ready);
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
      *self.state.worker.borrow_mut() = Some(thread::spawn(move || job.execute(&context, progress)));
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
            let layout = InstallLayout::new(self.state.target.borrow().as_path());
            *self.state.outcome.borrow_mut() = Some(SetupOutcome::Installed {
               layout,
               run: self.run_after.is_checked(),
            });
            self.show_page(Page::Done);
         }
         Err(error) if error.code == ExitCode::Cancelled => {
            LauncherLog::info("The user cancelled the installation");
            self.show_page(Page::Form);
            self.set_message("The installation was cancelled. Choose Install to continue where it stopped.");
         }
         Err(error) => {
            LauncherLog::error(format!("The installation failed: {error}"));
            self.show_page(Page::Form);
            self.set_message(&sentence(&error.message));
         }
      }
   }

   fn cancel_or_close(&self) {
      match self.state.page.get() {
         Page::Form | Page::Done => {
            let _ = self.wnd.hwnd().DestroyWindow();
         }
         Page::Checking | Page::Installing => {
            self.state.progress.borrow().cancel();
            WindowStyle::set_enabled(&self.cancel, false);
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
