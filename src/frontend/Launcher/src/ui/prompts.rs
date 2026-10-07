use winsafe::{self as w, co};

use crate::install::RunningProcess;
use crate::product;

use super::WindowStyle;

// Ids of the custom task dialog buttons. They only have to differ from the common button ids (1-8).
const FIRST_ID: u16 = 100;
const SECOND_ID: u16 = 101;

/// The user's choice when the launcher from the setup package is opened although the product is already
/// installed in another folder.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AlreadyInstalledChoice {
   /// Run the app that is already installed (through that installation's launcher).
   Run,

   /// Open the maintenance window.
   Maintenance,

   /// Close without doing anything.
   Cancel,
}

/// Questions and short messages for the user (TaskDialog): the update offer, confirmations, and errors.
///
/// Every function here blocks until the user answers, and is only called when `--quiet` is not given. The
/// title of every dialog is the product name. When a launcher window is open, the dialog belongs to that
/// window (modal), so the window cannot be clicked until the dialog is closed.
pub struct Prompts;

impl Prompts {
   // region: Statics

   /// Shows an error message.
   pub fn error(message: &str) {
      Dialog::new(Icon::Error, message).show();
   }

   /// Shows an informational message.
   pub fn info(message: &str) {
      Dialog::new(Icon::Information, message).show();
   }

   /// Asks before a risky action: `instruction` is the question, `content` its explanation, and `action` the
   /// text of its action button. `true` when the user chose that button; Enter and Esc choose Cancel.
   pub fn ask(instruction: &str, content: &str, action: &str) -> bool {
      Dialog::new(Icon::Warning, content)
         .instruction(instruction)
         .buttons(&[(FIRST_ID, action)], co::TDCBF::CANCEL)
         .default_button(co::DLGID::CANCEL.raw())
         .show()
         == FIRST_ID
   }

   /// Offers an update: `true` = *Update now*, `false` = *Later* (run the installed version).
   pub fn offer_update(published_at_utc: &str, download_size: u64) -> bool {
      let content = format!(
         "Published: {}\nDownload: {}",
         format_published(published_at_utc),
         format_size(download_size)
      );
      let now = format!("Update now\nDownload the new version, then start {}", product::APP_NAME);
      let later = "Later\nStart the installed version this time";
      Dialog::new(Icon::Product, &content)
         .instruction(&format!("A new version of {} is available", product::APP_NAME))
         .command_links(&[(FIRST_ID, &now), (SECOND_ID, later)], false)
         .show()
         == FIRST_ID
   }

   /// Offers Repair for a broken installation; `reason` explains what is broken.
   pub fn offer_repair(reason: &str) -> bool {
      let content = format!(
         "{}\n\nRepair downloads the missing or damaged files again.",
         sentence(reason)
      );
      Dialog::new(Icon::Warning, &content)
         .instruction(&format!("{} is not installed correctly", product::APP_NAME))
         .buttons(&[(FIRST_ID, "Repair")], co::TDCBF::CANCEL)
         .show()
         == FIRST_ID
   }

   /// Asks for confirmation before trusting the public key with `key_id`.
   pub fn confirm_import(key_id: &str) -> bool {
      let content = format!(
         "Key ID: {key_id}\n\nReleases signed with this key will be installed without further questions. Only \
          trust a key you received from your administrator."
      );
      Dialog::new(Icon::Warning, &content)
         .instruction(&format!("Trust this public key for {}?", product::APP_NAME))
         .buttons(&[(FIRST_ID, "Trust")], co::TDCBF::CANCEL)
         .show()
         == FIRST_ID
   }

   /// Asks for confirmation of uninstall, with a warning that the user's settings are deleted too.
   pub fn confirm_uninstall() -> bool {
      Self::ask(
         &format!("Uninstall {}?", product::APP_NAME),
         "This removes the application, its shortcuts and all its settings for this user, including saved \
          connections.",
         "Uninstall",
      )
   }

   /// Tells the user the app is still running: `true` = try again after the user closes it, `false` = cancel.
   pub fn app_running(processes: &[RunningProcess]) -> bool {
      let names = processes
         .iter()
         .map(|process| format!("{} (pid {})", process.path.display(), process.pid))
         .collect::<Vec<_>>()
         .join("\n");
      Dialog::new(Icon::Warning, &format!("Close it, then choose Retry.\n\n{names}"))
         .instruction(&format!("{} is still running", product::APP_NAME))
         .buttons(&[], co::TDCBF::RETRY | co::TDCBF::CANCEL)
         .show()
         == co::DLGID::RETRY.raw()
   }

   /// Tells the user the product is already installed in `folder` and asks what they want to do.
   pub fn already_installed(folder: &str) -> AlreadyInstalledChoice {
      let run = format!("Start {}", product::APP_NAME);
      let maintenance = "Maintenance\nRepair, change the release source or uninstall";
      let choice = Dialog::new(Icon::Product, &format!("Installed in {folder}"))
         .instruction(&format!("{} is already installed", product::APP_NAME))
         .command_links(&[(FIRST_ID, &run), (SECOND_ID, maintenance)], true)
         .show();
      match choice {
         FIRST_ID => AlreadyInstalledChoice::Run,
         SECOND_ID => AlreadyInstalledChoice::Maintenance,
         _ => AlreadyInstalledChoice::Cancel,
      }
   }

   // endregion
}

/// A byte size in an easy-to-read unit (`12.3 MB`).
pub fn format_size(bytes: u64) -> String {
   const UNITS: [&str; 4] = ["bytes", "KB", "MB", "GB"];
   let mut value = bytes as f64;
   let mut unit = 0;
   while value >= 1024.0 && unit < UNITS.len() - 1 {
      value /= 1024.0;
      unit += 1;
   }
   if unit == 0 {
      format!("{bytes} bytes")
   } else {
      format!("{value:.1} {}", UNITS[unit])
   }
}

/// The release publish time (`publishedAtUtc`, `2026-09-27T10:15:00.123Z`) in an easy-to-read form
/// (`2026-09-27 10:15 UTC`). Text of any other form is returned as it is.
pub fn format_published(published_at_utc: &str) -> String {
   match published_at_utc.split_once('T') {
      Some((date, time)) if time.len() >= 5 => format!("{date} {} UTC", &time[..5]),
      _ => published_at_utc.to_string(),
   }
}

/// `text` as a sentence for the user: the first letter capitalized and ending with a period. Launcher
/// error messages are written in lowercase without a period, so they can be joined into the middle of
/// another sentence.
pub fn sentence(text: &str) -> String {
   let mut chars = text.trim().chars();
   let mut sentence: String = chars
      .next()
      .map(|first| first.to_uppercase().chain(chars).collect())
      .unwrap_or_default();
   if !sentence.is_empty() && !sentence.ends_with(['.', '!', '?']) {
      sentence.push('.');
   }
   sentence
}

#[derive(Clone, Copy)]
enum Icon {
   Product,
   Information,
   Warning,
   Error,
}

// One task dialog, filled step by step, then shown with show().
struct Dialog<'a> {
   icon: Icon,
   instruction: Option<&'a str>,
   content: &'a str,
   buttons: &'a [(u16, &'a str)],
   common_buttons: co::TDCBF,
   default_button: Option<u16>,
   command_links: bool,
}

impl<'a> Dialog<'a> {
   fn new(icon: Icon, content: &'a str) -> Self {
      Self {
         icon,
         instruction: None,
         content,
         buttons: &[],
         common_buttons: co::TDCBF::OK,
         default_button: None,
         command_links: false,
      }
   }

   fn instruction(mut self, instruction: &'a str) -> Self {
      self.instruction = Some(instruction);
      self
   }

   fn buttons(mut self, buttons: &'a [(u16, &'a str)], common_buttons: co::TDCBF) -> Self {
      self.buttons = buttons;
      self.common_buttons = common_buttons;
      self
   }

   fn default_button(mut self, id: u16) -> Self {
      self.default_button = Some(id);
      self
   }

   // Big buttons with a second line of explanation. Closing the dialog (Esc, X) answers Cancel, with or without
   // a Cancel button of its own.
   fn command_links(mut self, buttons: &'a [(u16, &'a str)], cancel_button: bool) -> Self {
      self.buttons = buttons;
      self.common_buttons = if cancel_button {
         co::TDCBF::CANCEL
      } else {
         co::TDCBF::default()
      };
      self.command_links = true;
      self
   }

   // The id of the chosen button, or the id of Cancel when the dialog could not be shown.
   fn show(self) -> u16 {
      let hinstance = w::HINSTANCE::GetModuleHandle(None).ok();
      // The active window of this thread is the launcher window, if one is open; owning the dialog makes it modal.
      let owner = w::HWND::GetActiveWindow();
      let mut flags = co::TDF::ALLOW_DIALOG_CANCELLATION | co::TDF::SIZE_TO_CONTENT;
      if owner.is_some() {
         flags |= co::TDF::POSITION_RELATIVE_TO_WINDOW;
      }
      if self.command_links {
         flags |= co::TDF::USE_COMMAND_LINKS;
      }
      let main_icon = match self.icon {
         Icon::Product if hinstance.is_some() => w::IconIdTd::Id(WindowStyle::ICON_ID),
         Icon::Product | Icon::Information => w::IconIdTd::Td(co::TD_ICON::INFORMATION),
         Icon::Warning => w::IconIdTd::Td(co::TD_ICON::WARNING),
         Icon::Error => w::IconIdTd::Td(co::TD_ICON::ERROR),
      };
      let config = w::TASKDIALOGCONFIG {
         hwnd_parent: owner.as_ref(),
         hinstance: hinstance.as_ref(),
         flags,
         common_buttons: self.common_buttons,
         window_title: Some(product::APP_NAME),
         main_icon,
         main_instruction: self.instruction,
         content: Some(self.content),
         buttons: self.buttons,
         default_button_id: self
            .default_button
            .or_else(|| self.buttons.first().map(|(id, _)| *id))
            .unwrap_or(0),
         ..Default::default()
      };
      match w::TaskDialogIndirect(&config) {
         Ok((id, _, _)) => id.raw(),
         Err(_) => co::DLGID::CANCEL.raw(),
      }
   }
}

#[cfg(test)]
mod tests {
   use super::*;

   #[test]
   fn formats_sizes_and_dates() {
      assert_eq!(format_size(512), "512 bytes");
      assert_eq!(format_size(1536), "1.5 KB");
      assert_eq!(format_size(12 * 1024 * 1024), "12.0 MB");
      assert_eq!(format_published("2026-09-27T10:15:00.123Z"), "2026-09-27 10:15 UTC");
      assert_eq!(format_published("bogus"), "bogus");
      assert_eq!(sentence("there is no active version"), "There is no active version.");
      assert_eq!(sentence("Done."), "Done.");
      assert_eq!(sentence(""), "");
   }
}
