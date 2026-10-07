use crate::cli::CommandError;
use crate::ui::{MaintenanceAction, MaintenanceWindow};

use super::{CommandContext, UninstallCommand, UpdateCommand};

/// The `--maintenance` command: the Repair / Change source / Uninstall window, the **Modify** target in
/// Apps & Features.
pub struct MaintenanceCommand;

impl MaintenanceCommand {
   // region: Statics

   /// Opens the maintenance window. The release source and the keys are changed in that window itself;
   /// Repair and Uninstall close the window and then run like `--repair` and `--uninstall`.
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
