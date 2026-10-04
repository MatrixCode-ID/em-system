use crate::cli::CommandError;
use crate::ui::{MaintenanceAction, MaintenanceWindow};

use super::{CommandContext, UninstallCommand, UpdateCommand};

/// Perintah `--maintenance`: jendela Repair / Change source / Uninstall, target **Modify** di Apps & Features.
pub struct MaintenanceCommand;

impl MaintenanceCommand {
   // region: Statics

   /// Membuka jendela maintenance. Sumber rilis dan key diubah di jendela itu sendiri; Repair dan Uninstall
   /// menutup jendelanya lalu dijalankan seperti `--repair` dan `--uninstall`.
   pub fn run(context: &CommandContext) -> Result<(), CommandError> {
      if context.quiet {
         return Err(CommandError::invalid(
            "the maintenance window cannot be used with --quiet",
         ));
      }
      let config = context.load_config()?;
      let layout = context.installed_layout(&config)?;
      match MaintenanceWindow::run(context, &layout)? {
         MaintenanceAction::Close => Ok(()),
         MaintenanceAction::Repair => UpdateCommand::repair(context),
         MaintenanceAction::Uninstall => UninstallCommand::run(context),
      }
   }

   // endregion
}
