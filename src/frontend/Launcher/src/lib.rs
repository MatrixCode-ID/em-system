//! A generic launcher, installer, and updater for desktop clients published in the signed release format
//! (`doc/release-format.md`). Everything product-specific is in the [`product`] module.

pub mod cli;
pub mod commands;
pub mod config;
pub mod format;
pub mod install;
pub mod launcher_lock;
pub mod launcher_log;
pub mod product;
pub mod shell;
pub mod source;
pub mod ui;
