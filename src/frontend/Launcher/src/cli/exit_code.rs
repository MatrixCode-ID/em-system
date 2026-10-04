use crate::install::UpdateError;

/// Exit code launcher, supaya script IT (instalasi senyap, deployment) bisa membedakan penyebab kegagalan.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum ExitCode {
   /// Berhasil.
   Success = 0,

   /// Argumen salah, atau permintaan tidak bisa dipenuhi dengan keadaan sekarang (mis. belum terpasang).
   InvalidArguments = 1,

   /// Sumber rilis tidak bisa dibaca, atau rilisnya tidak sah (tanda tangan, format, file tidak cocok).
   SourceFailed = 2,

   /// Gagal membaca atau menulis di disk atau registry.
   IoFailed = 3,

   /// Dibatalkan user.
   Cancelled = 4,

   /// App masih berjalan dari folder instalasi, sehingga pekerjaan tidak bisa dilakukan.
   AppRunning = 5,
}

impl ExitCode {
   // region: Statics

   /// Exit code untuk kegagalan install, update, atau repair.
   pub fn of_update_error(error: &UpdateError) -> Self {
      match error {
         UpdateError::Source(_)
         | UpdateError::Signature(_)
         | UpdateError::Format(_)
         | UpdateError::ServerChanged { .. } => Self::SourceFailed,
         UpdateError::Io { .. } => Self::IoFailed,
         UpdateError::Cancelled => Self::Cancelled,
      }
   }

   // endregion
}

impl From<ExitCode> for std::process::ExitCode {
   fn from(code: ExitCode) -> Self {
      Self::from(code as u8)
   }
}
