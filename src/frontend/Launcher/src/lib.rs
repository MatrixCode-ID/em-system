//! Launcher, installer, dan updater generik untuk client desktop yang diterbitkan dalam format rilis
//! bertanda tangan (`doc/release-format.md`). Semua yang spesifik produk ada di modul [`product`].

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
