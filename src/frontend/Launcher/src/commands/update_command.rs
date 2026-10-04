use std::sync::Arc;

use crate::cli::CommandError;
use crate::install::{UpdateError, UpdateOutcome, UpdateProgress, UpdateSummary, Updater};
use crate::launcher_log::LauncherLog;
use crate::product;
use crate::ui::{format_published, format_size};

use super::CommandContext;

/// Perintah `--update` dan `--repair` terhadap instalasi yang sudah ada.
pub struct UpdateCommand;

impl UpdateCommand {
   // region: Statics

   /// `--update`: cek sumber rilis dan pasang rilisnya tanpa bertanya kalau berbeda dari versi aktif. Juga
   /// membersihkan sisa run sebelumnya dan memperbarui entri Apps & Features.
   pub fn update(context: &CommandContext) -> Result<(), CommandError> {
      let config = context.load_config()?;
      let layout = context.installed_layout(&config)?;
      let _lock = context.lock()?;
      let keys = context.trusted_keys(&config)?;
      let source = context.source(&config)?;
      let progress = Arc::new(UpdateProgress::new());
      let updater = Updater::new(source.as_ref(), &layout, progress.clone());
      updater.remove_leftovers();

      let title = format!("Updating {}", product::APP_NAME);
      let result = context.with_progress(&title, &progress, || {
         let release = updater.check(&keys)?;
         if updater.is_active(&release) {
            return Ok((release, None));
         }
         let outcome = updater.install(&release)?;
         Ok::<_, UpdateError>((release, Some(outcome)))
      });
      let (release, outcome) = result.map_err(from_update_error)?;
      let Some(outcome) = outcome else {
         context.done(&format!(
            "{} is up to date (release published {}).",
            product::APP_NAME,
            format_published(release.manifest().published_at_utc())
         ));
         return Ok(());
      };

      context.ensure_root_launcher(&layout)?;
      context
         .integration
         .refresh_entry(&layout)
         .map_err(CommandError::io("cannot update the Apps & Features entry"))?;
      context.done(&format!(
         "{} was updated to the release published {}: {}.",
         product::APP_NAME,
         format_published(release.manifest().published_at_utc()),
         Self::describe(&outcome)
      ));
      Ok(())
   }

   /// `--repair`: hash ulang setiap file versi aktif terhadap rilis di sumber, unduh ulang yang rusak atau
   /// hilang, lalu pasang ulang launcher root, shortcut, dan entri Apps & Features. App harus ditutup dulu,
   /// karena file yang sedang dipakai tidak bisa diganti.
   pub fn repair(context: &CommandContext) -> Result<(), CommandError> {
      let config = context.load_config()?;
      let layout = context.installed_layout(&config)?;
      let _lock = context.lock()?;
      context.wait_until_app_closed(&layout)?;
      let keys = context.trusted_keys(&config)?;
      let source = context.source(&config)?;
      let progress = Arc::new(UpdateProgress::new());
      let updater = Updater::new(source.as_ref(), &layout, progress.clone());

      let title = format!("Repairing {}", product::APP_NAME);
      let outcome = context
         .with_progress(&title, &progress, || updater.repair(&updater.check(&keys)?))
         .map_err(from_update_error)?;
      context.ensure_root_launcher(&layout)?;
      context
         .integration
         .apply(&layout, config.start_menu, config.desktop)
         .map_err(CommandError::io("cannot restore the shortcuts"))?;
      context.done(&format!(
         "{} was repaired: {}.",
         product::APP_NAME,
         Self::describe(&outcome)
      ));
      Ok(())
   }

   /// Ringkasan hasil install, update, atau repair untuk pesan ke user.
   pub fn describe(outcome: &UpdateOutcome) -> String {
      match outcome {
         UpdateOutcome::AlreadyActive => "nothing had to be done".to_string(),
         UpdateOutcome::Installed(summary) => format!("{} reused", counts(summary)),
         UpdateOutcome::Repaired(summary) => format!("{} intact", counts(summary)),
      }
   }

   // endregion
}

// A cancel asked for in the progress window is the user's own choice: exit code 4, and nothing to show.
fn from_update_error(error: UpdateError) -> CommandError {
   if matches!(error, UpdateError::Cancelled) {
      LauncherLog::info("Cancelled by the user");
      CommandError::cancelled("")
   } else {
      error.into()
   }
}

fn counts(summary: &UpdateSummary) -> String {
   format!(
      "{} downloaded ({}), {}",
      files(summary.downloaded_files),
      format_size(summary.downloaded_bytes),
      files(summary.reused_files)
   )
}

fn files(count: usize) -> String {
   if count == 1 {
      "1 file".to_string()
   } else {
      format!("{count} files")
   }
}
