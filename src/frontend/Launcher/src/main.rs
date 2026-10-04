// Release builds run without a console window; debug builds keep one so their output stays visible.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::ffi::OsString;

use launcher::cli::{Command, CommandError, CommandLine, Console, ExitCode, USAGE};
use launcher::commands::{
   CommandContext, InstallCommand, KeyCommand, MaintenanceCommand, StartCommand, UninstallCommand, UpdateCommand,
};
use launcher::launcher_log::LauncherLog;
use launcher::ui::{Prompts, sentence};

fn main() -> std::process::ExitCode {
   let has_console = Console::attach();
   std::panic::set_hook(Box::new(|info| {
      LauncherLog::panic(info);
      Console::err(format!("launcher crashed: {info}"));
   }));

   let args: Vec<OsString> = std::env::args_os().skip(1).collect();
   // Needed before parsing succeeds, to know whether a parse error may be shown in a dialog.
   let quiet = args.iter().any(|arg| arg == "--quiet");
   let result = CommandLine::parse(args).and_then(|command_line| {
      let context = CommandContext::for_product(command_line.quiet)?;
      run(&context, command_line.command, has_console)
   });

   match result {
      Ok(()) => ExitCode::Success.into(),
      Err(error) => {
         report(&error, quiet);
         error.code.into()
      }
   }
}

fn run(context: &CommandContext, command: Command, has_console: bool) -> Result<(), CommandError> {
   match command {
      Command::Start { app_args, from_app } => StartCommand::run(context, &app_args, from_app),
      Command::Install(options) => InstallCommand::run(context, &options),
      Command::Update => UpdateCommand::update(context),
      Command::Maintenance => MaintenanceCommand::run(context),
      Command::Repair => UpdateCommand::repair(context),
      Command::Uninstall => UninstallCommand::run(context),
      Command::Import { file } => KeyCommand::import(context, &file),
      Command::ListKeys => KeyCommand::list(context),
      Command::RemoveKey { key_id } => KeyCommand::remove(context, &key_id),
      Command::Apply { pid, app_args } => StartCommand::apply(context, pid, &app_args),
      Command::UninstallFinish { install, parent_pid } => UninstallCommand::finish(context, &install, parent_pid),
      Command::Help => {
         Console::out(USAGE);
         if !has_console {
            Prompts::info(USAGE);
         }
         Ok(())
      }
   }
}

// A failure goes to the log and the console, and to a dialog unless the command is quiet. An empty message
// means the user cancelled on purpose, and there is nothing to tell.
fn report(error: &CommandError, quiet: bool) {
   if error.message.is_empty() {
      return;
   }
   LauncherLog::error(&error.message);
   Console::err(format!("Error: {}", error.message));
   if !quiet {
      Prompts::error(&sentence(&error.message));
   }
}
