use std::sync::Arc;

use crate::cli::CommandError;
use crate::install::{UpdateProgress, Updater};

use super::CommandContext;

/// Menyiapkan update dengan policy dan verifikasi launcher yang sama, tanpa GUI atau aktivasi.
pub struct PrepareCommand;

impl PrepareCommand {
   /// Mengembalikan JSON satu baris, tanpa mengubah current.json atau launcher root.
   pub fn run(context: &CommandContext) -> Result<serde_json::Value, CommandError> {
      let config = context.load_config()?;
      let layout = context.installed_layout(&config)?;
      let _lock = context.lock()?;
      let keys = context.trusted_keys(&config)?;
      let source = context.source(&config)?;
      let updater = Updater::new(source.as_ref(), &layout, Arc::new(UpdateProgress::new()));
      let release = updater.check(&keys)?;
      if updater.is_active(&release) {
         return Ok(serde_json::json!({"state":"none"}));
      }
      updater.prepare(&release)?;
      Ok(
         serde_json::json!({"state":"prepared", "release":release.id(), "publishedAt":release.manifest().published_at_utc()}),
      )
   }
}
