use std::error::Error;
use std::fmt::{self, Display, Formatter};
use std::io;

use crate::install::UpdateError;

use super::ExitCode;

/// Kegagalan sebuah perintah launcher: pesan untuk user (bahasa Inggris) beserta exit code-nya. Setiap
/// perintah mengembalikan `Result<(), CommandError>`, lalu `main` yang menampilkan pesannya dan keluar
/// dengan exit code itu.
#[derive(Debug)]
pub struct CommandError {
   /// Exit code proses.
   pub code: ExitCode,

   /// Pesan untuk user.
   pub message: String,
}

impl CommandError {
   // region: Statics

   /// Kegagalan dengan `code` dan `message`.
   pub fn new(code: ExitCode, message: impl Into<String>) -> Self {
      Self {
         code,
         message: message.into(),
      }
   }

   /// Argumen salah atau permintaan tidak bisa dipenuhi ([`ExitCode::InvalidArguments`]).
   pub fn invalid(message: impl Into<String>) -> Self {
      Self::new(ExitCode::InvalidArguments, message)
   }

   /// Dibatalkan user ([`ExitCode::Cancelled`]).
   pub fn cancelled(message: impl Into<String>) -> Self {
      Self::new(ExitCode::Cancelled, message)
   }

   /// Membuat pembungkus kesalahan I/O untuk `map_err` ([`ExitCode::IoFailed`]), dengan keterangan apa yang
   /// sedang dikerjakan, misalnya `.map_err(CommandError::io("cannot read the configuration"))`.
   pub fn io(action: &str) -> impl FnOnce(io::Error) -> CommandError {
      let action = action.to_string();
      move |error| CommandError::new(ExitCode::IoFailed, format!("{action}: {error}"))
   }

   // endregion
}

impl Display for CommandError {
   fn fmt(&self, f: &mut Formatter<'_>) -> fmt::Result {
      f.write_str(&self.message)
   }
}

impl Error for CommandError {}

impl From<UpdateError> for CommandError {
   fn from(error: UpdateError) -> Self {
      Self::new(ExitCode::of_update_error(&error), error.to_string())
   }
}
