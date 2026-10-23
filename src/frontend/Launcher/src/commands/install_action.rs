/// The operation selected in the shared installer form.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum InstallAction {
   /// First installation.
   Install,
   /// Install a newer release using the active version as donor.
   Update,
   /// Verify and restore the release in place.
   Repair,
   /// Remove the installation and settings before installing a verified release.
   CleanInstall,
   /// Remove the installation and settings without saving a source.
   Uninstall,
}

impl InstallAction {
   /// The caption of the primary action button.
   pub fn caption(self) -> &'static str {
      match self {
         Self::Install => "Install",
         Self::Update => "Update",
         Self::Repair => "Repair",
         Self::CleanInstall => "Clean install",
         Self::Uninstall => "Uninstall",
      }
   }

   /// The action in progress, used in the installer heading.
   pub fn running(self) -> &'static str {
      match self {
         Self::Install | Self::CleanInstall => "Installing",
         Self::Update => "Updating",
         Self::Repair => "Repairing",
         Self::Uninstall => "Removing",
      }
   }
}
