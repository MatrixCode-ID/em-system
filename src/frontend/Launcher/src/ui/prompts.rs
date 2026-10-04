use winsafe::{self as w, co};

use crate::install::RunningProcess;
use crate::product;

use super::WindowStyle;

// Ids of the custom task dialog buttons. They only have to differ from the common button ids (1-8).
const FIRST_ID: u16 = 100;
const SECOND_ID: u16 = 101;

/// Pilihan user saat launcher dari paket setup dibuka padahal produknya sudah terpasang di folder lain.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AlreadyInstalledChoice {
   /// Jalankan app yang sudah terpasang (lewat launcher instalasi itu).
   Run,

   /// Buka jendela maintenance.
   Maintenance,

   /// Tutup tanpa melakukan apa pun.
   Cancel,
}

/// Pertanyaan dan pesan singkat untuk user (TaskDialog): tawaran update, konfirmasi, dan kesalahan.
///
/// Semua fungsi di sini memblokir sampai user menjawab, dan hanya dipanggil kalau `--quiet` tidak diberikan.
/// Judul setiap dialog adalah nama produk. Kalau jendela launcher sedang terbuka, dialognya menjadi milik
/// jendela itu (modal), jadi jendelanya tidak bisa diklik sampai dialog ditutup.
pub struct Prompts;

impl Prompts {
   // region: Statics

   /// Menampilkan pesan kesalahan.
   pub fn error(message: &str) {
      Dialog::new(Icon::Error, message).show();
   }

   /// Menampilkan pesan informasi.
   pub fn info(message: &str) {
      Dialog::new(Icon::Information, message).show();
   }

   /// Bertanya sebelum tindakan yang berisiko: `instruction` adalah pertanyaannya, `content` penjelasannya, dan
   /// `action` teks tombol tindakannya. `true` kalau user memilih tombol itu; Enter dan Esc memilih Cancel.
   pub fn ask(instruction: &str, content: &str, action: &str) -> bool {
      Dialog::new(Icon::Warning, content)
         .instruction(instruction)
         .buttons(&[(FIRST_ID, action)], co::TDCBF::CANCEL)
         .default_button(co::DLGID::CANCEL.raw())
         .show()
         == FIRST_ID
   }

   /// Menawarkan update: `true` = *Update now*, `false` = *Later* (jalankan versi terpasang).
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

   /// Menawarkan Repair untuk instalasi yang rusak; `reason` menjelaskan apa yang rusak.
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

   /// Meminta konfirmasi sebelum mempercayai public key dengan `key_id`.
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

   /// Meminta konfirmasi uninstall, dengan peringatan bahwa pengaturan user ikut dihapus.
   pub fn confirm_uninstall() -> bool {
      Self::ask(
         &format!("Uninstall {}?", product::APP_NAME),
         "This removes the application, its shortcuts and all its settings for this user, including saved \
          connections.",
         "Uninstall",
      )
   }

   /// Memberi tahu bahwa app masih berjalan: `true` = coba lagi setelah user menutupnya, `false` = batal.
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

   /// Memberi tahu bahwa produk sudah terpasang di `folder` dan menanyakan apa yang ingin dilakukan.
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

/// Ukuran byte dalam satuan yang mudah dibaca (`12.3 MB`).
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

/// Waktu terbit rilis (`publishedAtUtc`, `2026-09-27T10:15:00.123Z`) dalam bentuk yang mudah dibaca
/// (`2026-09-27 10:15 UTC`). Teks yang bentuknya lain dikembalikan apa adanya.
pub fn format_published(published_at_utc: &str) -> String {
   match published_at_utc.split_once('T') {
      Some((date, time)) if time.len() >= 5 => format!("{date} {} UTC", &time[..5]),
      _ => published_at_utc.to_string(),
   }
}

/// `text` sebagai kalimat untuk user: huruf pertama besar dan diakhiri titik. Pesan kesalahan launcher ditulis
/// huruf kecil tanpa titik, supaya bisa disambung di tengah kalimat lain.
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
