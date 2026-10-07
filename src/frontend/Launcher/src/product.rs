//! The product identity embedded at build time from `product.toml` (or the file pointed to by the
//! `LAUNCHER_PRODUCT` environment variable). The launcher code only uses the constants here and never
//! names the product directly.

include!(concat!(env!("OUT_DIR"), "/product.rs"));
