use std::ffi::OsString;
use std::path::PathBuf;

use lexopt::{Arg, Parser, ValueExt};

use crate::install::Uninstaller;

use super::CommandError;

/// The `--help` text.
pub const USAGE: &str = "\
Usage: launcher.exe [options] [-- <app arguments>]

  (no command)                 Check for an update, then start the application
  --install [--source S] [--target T] [--import P]... [--no-start-menu] [--no-desktop] [--no-run] [--quiet]
                               Install (with --quiet: without the setup form)
  --update [--quiet]           Check for an update and apply it without asking
  --maintenance                Open the maintenance window
  --repair [--quiet]           Verify every installed file and restore the damaged ones
  --uninstall [--quiet]        Remove the application, its shortcuts and all its user settings
  --import <file.pem> [--quiet]
                               Trust the public key in the file
  --list-keys                  List the trusted public keys
  --remove-key <keyId>         Stop trusting a key imported by the user
  --apply --pid <pid>          Wait for the process to exit, then update and start without asking
  --help                       Show this text

Arguments after -- are passed to the application unchanged.
Exit codes: 0 success, 1 invalid arguments, 2 release source unreadable or release not valid,
3 disk or registry error, 4 cancelled, 5 the application is still running.";

/// The command requested through the arguments, already validated.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Command {
   /// No command: check for an update, then run the app with `app_args`. `from_app` marks a launcher that was
   /// run again by an app opened directly from the install folder (the internal `--from-app` argument).
   Start { app_args: Vec<OsString>, from_app: bool },

   /// `--install`.
   Install(InstallOptions),

   /// `--update`.
   Update,

   /// `--maintenance`.
   Maintenance,

   /// `--repair`.
   Repair,

   /// `--uninstall`.
   Uninstall,

   /// `--import <file>` without `--install`.
   Import { file: PathBuf },

   /// `--list-keys`.
   ListKeys,

   /// `--remove-key <keyId>`.
   RemoveKey { key_id: String },

   /// `--apply --pid <pid>`: wait for process `pid` to finish, then take the normal flow without asking.
   Apply { pid: u32, app_args: Vec<OsString> },

   /// The second stage of uninstall (an internal argument, see [`Uninstaller::FINISH_ARGUMENT`]).
   UninstallFinish { install: PathBuf, parent_pid: u32 },

   /// `--help`.
   Help,
}

/// The `--install` fields. A field that is not given uses the existing configuration or the default.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct InstallOptions {
   /// `--source`: the address of the release folder.
   pub source: Option<String>,

   /// `--target`: the install folder.
   pub target: Option<PathBuf>,

   /// `--import` (may be repeated): a `.pem` file holding a trusted public key.
   pub imports: Vec<PathBuf>,

   /// `--no-start-menu`.
   pub no_start_menu: bool,

   /// `--no-desktop`.
   pub no_desktop: bool,

   /// `--no-run`: do not run the app after install.
   pub no_run: bool,
}

/// The parsed launcher arguments: its command and `--quiet`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CommandLine {
   /// The requested command.
   pub command: Command,

   /// `--quiet`: no GUI and no questions. Messages are still written to the parent console, if there is one.
   pub quiet: bool,
}

// Everything the parser collected, before it is checked against the command.
#[derive(Default)]
struct Collected {
   commands: Vec<&'static str>,
   options: Vec<&'static str>,
   install: InstallOptions,
   key_id: Option<String>,
   pid: Option<u32>,
   parent_pid: Option<u32>,
   finish_install: Option<PathBuf>,
   quiet: bool,
   from_app: bool,
}

impl CommandLine {
   // region: Statics

   /// Parses the process arguments (without the exe name). Arguments after the first `--` are not parsed and
   /// are passed on to the app as they are.
   pub fn parse<I>(args: I) -> Result<Self, CommandError>
   where
      I: IntoIterator<Item = OsString>,
   {
      let mut own = Vec::new();
      let mut app_args: Option<Vec<OsString>> = None;
      for arg in args {
         match &mut app_args {
            Some(rest) => rest.push(arg),
            None if arg == "--" => app_args = Some(Vec::new()),
            None => own.push(arg),
         }
      }
      let collected = collect(own).map_err(|error| CommandError::invalid(format!("{error}\n\n{USAGE}")))?;
      build(collected, app_args).map_err(|message| CommandError::invalid(format!("{message}\n\n{USAGE}")))
   }

   // endregion
}

fn collect(args: Vec<OsString>) -> Result<Collected, lexopt::Error> {
   let mut parser = Parser::from_args(args);
   let mut c = Collected::default();
   while let Some(arg) = parser.next()? {
      match arg {
         Arg::Long("install") => c.commands.push("--install"),
         Arg::Long("update") => c.commands.push("--update"),
         Arg::Long("maintenance") => c.commands.push("--maintenance"),
         Arg::Long("repair") => c.commands.push("--repair"),
         Arg::Long("uninstall") => c.commands.push("--uninstall"),
         Arg::Long("list-keys") => c.commands.push("--list-keys"),
         Arg::Long("apply") => c.commands.push("--apply"),
         Arg::Long("help") | Arg::Short('h') | Arg::Short('?') => c.commands.push("--help"),
         Arg::Long("remove-key") => {
            c.commands.push("--remove-key");
            c.key_id = Some(parser.value()?.string()?);
         }
         Arg::Long("uninstall-finish") => {
            c.commands.push(Uninstaller::FINISH_ARGUMENT);
            c.finish_install = Some(parser.value()?.into());
         }
         Arg::Long("import") => {
            c.options.push("--import");
            c.install.imports.push(parser.value()?.into());
         }
         Arg::Long("source") => {
            c.options.push("--source");
            c.install.source = Some(parser.value()?.string()?);
         }
         Arg::Long("target") => {
            c.options.push("--target");
            c.install.target = Some(parser.value()?.into());
         }
         Arg::Long("no-start-menu") => {
            c.options.push("--no-start-menu");
            c.install.no_start_menu = true;
         }
         Arg::Long("no-desktop") => {
            c.options.push("--no-desktop");
            c.install.no_desktop = true;
         }
         Arg::Long("no-run") => {
            c.options.push("--no-run");
            c.install.no_run = true;
         }
         Arg::Long("quiet") => {
            c.options.push("--quiet");
            c.quiet = true;
         }
         Arg::Long("pid") => {
            c.options.push("--pid");
            c.pid = Some(parser.value()?.parse()?);
         }
         Arg::Long("parent-pid") => {
            c.options.push(Uninstaller::PARENT_PID_ARGUMENT);
            c.parent_pid = Some(parser.value()?.parse()?);
         }
         Arg::Long("from-app") => {
            c.options.push("--from-app");
            c.from_app = true;
         }
         _ => return Err(arg.unexpected()),
      }
   }
   Ok(c)
}

fn build(mut c: Collected, app_args: Option<Vec<OsString>>) -> Result<CommandLine, String> {
   // --import alone is a command of its own; next to --install it is one of its options.
   if c.commands.is_empty() && c.options.contains(&"--import") {
      c.commands.push("--import");
      c.options.retain(|option| *option != "--import");
   }
   if c.commands.len() > 1 {
      return Err(format!("{} cannot be combined", c.commands.join(" and ")));
   }
   let name = c.commands.first().copied().unwrap_or("");

   let allowed: &[&str] = match name {
      "" => &["--from-app"],
      "--install" => &[
         "--source",
         "--target",
         "--import",
         "--no-start-menu",
         "--no-desktop",
         "--no-run",
         "--quiet",
      ],
      "--update" | "--repair" | "--uninstall" | "--import" => &["--quiet"],
      "--apply" => &["--pid"],
      Uninstaller::FINISH_ARGUMENT => &[Uninstaller::PARENT_PID_ARGUMENT, "--quiet"],
      _ => &[],
   };
   let mut seen = Vec::new();
   for option in &c.options {
      if !allowed.contains(option) {
         return Err(match name {
            "" => format!("{option} needs a command"),
            _ => format!("{option} cannot be used with {name}"),
         });
      }
      if *option != "--import" && seen.contains(option) {
         return Err(format!("{option} is given twice"));
      }
      seen.push(*option);
   }
   if app_args.is_some() && !matches!(name, "" | "--apply") {
      return Err(format!("arguments after -- cannot be used with {name}"));
   }

   let app_args = app_args.unwrap_or_default();
   let command = match name {
      "" => Command::Start {
         app_args,
         from_app: c.from_app,
      },
      "--install" => Command::Install(c.install),
      "--update" => Command::Update,
      "--maintenance" => Command::Maintenance,
      "--repair" => Command::Repair,
      "--uninstall" => Command::Uninstall,
      "--import" => {
         let mut imports = c.install.imports;
         if imports.len() != 1 {
            return Err("--import takes one file; use --install to import several".into());
         }
         Command::Import {
            file: imports.remove(0),
         }
      }
      "--list-keys" => Command::ListKeys,
      "--remove-key" => Command::RemoveKey {
         key_id: c.key_id.unwrap_or_default(),
      },
      "--apply" => Command::Apply {
         pid: c.pid.ok_or("--apply needs --pid")?,
         app_args,
      },
      Uninstaller::FINISH_ARGUMENT => Command::UninstallFinish {
         install: c.finish_install.unwrap_or_default(),
         parent_pid: c.parent_pid.ok_or("--uninstall-finish needs --parent-pid")?,
      },
      _ => Command::Help,
   };
   Ok(CommandLine {
      command,
      quiet: c.quiet,
   })
}

#[cfg(test)]
mod tests {
   use super::*;

   fn parse(args: &[&str]) -> Result<CommandLine, CommandError> {
      CommandLine::parse(args.iter().map(OsString::from))
   }

   fn command(args: &[&str]) -> Command {
      parse(args).unwrap().command
   }

   #[test]
   fn no_arguments_start_the_application() {
      assert_eq!(
         command(&[]),
         Command::Start {
            app_args: vec![],
            from_app: false
         }
      );
      assert_eq!(
         command(&["--from-app", "--", "--open", "--quiet", "a b"]),
         Command::Start {
            app_args: vec!["--open".into(), "--quiet".into(), "a b".into()],
            from_app: true
         }
      );
   }

   #[test]
   fn parses_the_install_options() {
      let parsed = parse(&[
         "--install",
         "--source",
         r"\\server\releases",
         "--target=C:\\Apps\\X",
         "--import",
         "a.pem",
         "--import",
         "b.pem",
         "--no-desktop",
         "--no-run",
         "--quiet",
      ])
      .unwrap();
      assert!(parsed.quiet);
      assert_eq!(
         parsed.command,
         Command::Install(InstallOptions {
            source: Some(r"\\server\releases".into()),
            target: Some(r"C:\Apps\X".into()),
            imports: vec!["a.pem".into(), "b.pem".into()],
            no_start_menu: false,
            no_desktop: true,
            no_run: true,
         })
      );
   }

   #[test]
   fn parses_the_other_commands() {
      assert_eq!(command(&["--update", "--quiet"]), Command::Update);
      assert_eq!(command(&["--maintenance"]), Command::Maintenance);
      assert_eq!(command(&["--repair"]), Command::Repair);
      assert_eq!(command(&["--uninstall", "--quiet"]), Command::Uninstall);
      assert_eq!(
         command(&["--import", "k.pem"]),
         Command::Import { file: "k.pem".into() }
      );
      assert_eq!(command(&["--list-keys"]), Command::ListKeys);
      assert_eq!(
         command(&["--remove-key", "ABCD"]),
         Command::RemoveKey { key_id: "ABCD".into() }
      );
      assert_eq!(
         command(&["--apply", "--pid", "42", "--", "x"]),
         Command::Apply {
            pid: 42,
            app_args: vec!["x".into()]
         }
      );
      assert_eq!(
         command(&["--uninstall-finish", r"C:\Apps\X", "--parent-pid", "7", "--quiet"]),
         Command::UninstallFinish {
            install: r"C:\Apps\X".into(),
            parent_pid: 7
         }
      );
      assert_eq!(command(&["--help"]), Command::Help);
      assert_eq!(command(&["-h"]), Command::Help);
   }

   #[test]
   fn rejects_invalid_arguments() {
      for args in [
         &["--install", "--update"][..],
         &["--update", "--source", "x"],
         &["--quiet"],
         &["--list-keys", "--quiet"],
         &["--apply"],
         &["--apply", "--pid", "abc"],
         &["--import", "a.pem", "--import", "b.pem"],
         &["--install", "--quiet", "--quiet"],
         &["--repair", "--", "x"],
         &["--bogus"],
         &["stray"],
         &["--remove-key"],
      ] {
         let error = parse(args).unwrap_err();
         assert_eq!(error.code, super::super::ExitCode::InvalidArguments, "{args:?}");
      }
   }
}
