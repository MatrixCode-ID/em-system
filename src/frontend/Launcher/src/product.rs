//! The product identity embedded at build time from `product.toml` (or the file pointed to by the
//! `LAUNCHER_PRODUCT` environment variable). The launcher code only uses the constants here and never
//! names the product directly.

include!(concat!(env!("OUT_DIR"), "/product.rs"));

/// Teks identitas versi untuk UI, log dan CLI.
pub fn version_display() -> String {
   format!("{} Launcher {} (engine {})", APP_NAME, LAUNCHER_VERSION, ENGINE_VERSION)
}
