//! Identitas produk yang ditanam saat build dari `product.toml` (atau file yang ditunjuk environment
//! variable `LAUNCHER_PRODUCT`). Kode launcher hanya memakai konstanta di sini, tidak pernah menyebut nama
//! produk secara langsung.

include!(concat!(env!("OUT_DIR"), "/product.rs"));
