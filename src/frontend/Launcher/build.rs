// Turns the product identity (product.toml, or the file named by LAUNCHER_PRODUCT) into Rust constants and a
// resource script with the icon, the application manifest and the version info, so the code itself never names
// a product.

use std::env;
use std::fmt::Write as _;
use std::fs;
use std::path::{Path, PathBuf};

use serde::Deserialize;

const PRODUCT_ENV: &str = "LAUNCHER_PRODUCT";
const DEFAULT_PRODUCT_FILE: &str = "product.toml";
const MANIFEST_FILE: &str = "launcher.manifest";

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct Product {
   app_name: String,
   publisher: String,
   app_exe: String,
   app_id: String,
   icon: String,
}

fn main() {
   let project_dir = PathBuf::from(env::var("CARGO_MANIFEST_DIR").expect("CARGO_MANIFEST_DIR is not set"));
   let out_dir = PathBuf::from(env::var("OUT_DIR").expect("OUT_DIR is not set"));

   let product_file = match env::var(PRODUCT_ENV) {
      Ok(path) if !path.trim().is_empty() => project_dir.join(path.trim()),
      _ => project_dir.join(DEFAULT_PRODUCT_FILE),
   };
   println!("cargo:rerun-if-env-changed={PRODUCT_ENV}");
   println!("cargo:rerun-if-changed={}", product_file.display());
   println!("cargo:rerun-if-changed={MANIFEST_FILE}");

   let product = read_product(&product_file);
   let icon = product_file.parent().unwrap_or(Path::new(".")).join(&product.icon);
   let icon =
      fs::canonicalize(&icon).unwrap_or_else(|_| fail(&format!("the icon '{}' does not exist", icon.display())));
   println!("cargo:rerun-if-changed={}", icon.display());

   fs::write(out_dir.join("product.rs"), product_constants(&product))
      .unwrap_or_else(|x| fail(&format!("cannot write product.rs: {x}")));

   let rc_file = out_dir.join("launcher.rc");
   fs::write(
      &rc_file,
      resource_script(&product, &icon, &project_dir.join(MANIFEST_FILE)),
   )
   .unwrap_or_else(|x| fail(&format!("cannot write launcher.rc: {x}")));
   embed_resource::compile(&rc_file, embed_resource::NONE)
      .manifest_required()
      .unwrap_or_else(|x| fail(&format!("cannot compile launcher.rc: {x}")));

   // Test executables link the same GUI code but get none of the resources above. Without common controls 6 in
   // their manifest Windows refuses to load them, since TaskDialogIndirect only exists in that version.
   println!("cargo:rustc-link-arg-tests=/MANIFEST:EMBED");
   println!(
      "cargo:rustc-link-arg-tests=/MANIFESTDEPENDENCY:type='win32' name='Microsoft.Windows.Common-Controls' \
       version='6.0.0.0' processorArchitecture='*' publicKeyToken='6595b64144ccf1df' language='*'"
   );
}

fn read_product(path: &Path) -> Product {
   let text = fs::read_to_string(path).unwrap_or_else(|x| fail(&format!("cannot read {}: {x}", path.display())));
   let product: Product =
      toml::from_str(&text).unwrap_or_else(|x| fail(&format!("{} is not a valid product file: {x}", path.display())));

   for (name, value) in [
      ("app_name", &product.app_name),
      ("publisher", &product.publisher),
      ("app_exe", &product.app_exe),
      ("app_id", &product.app_id),
      ("icon", &product.icon),
   ] {
      if value.trim().is_empty() {
         fail(&format!("'{name}' in {} is empty", path.display()));
      }
   }

   product
}

fn product_constants(product: &Product) -> String {
   // `{:?}` on a str yields a valid Rust string literal, escapes included.
   let mut code = String::new();
   let items = [
      (
         "The application name, the same as the host's `ApplicationName`; also the registry root `HKCU\\<APP_NAME>`.",
         "APP_NAME",
         &product.app_name,
      ),
      (
         "The publisher name, shown in Apps & Features.",
         "PUBLISHER",
         &product.publisher,
      ),
      (
         "The file name of the application exe inside the version folder.",
         "APP_EXE",
         &product.app_exe,
      ),
      (
         "The application's AppUserModelID, which is also the name of the Uninstall entry key.",
         "APP_ID",
         &product.app_id,
      ),
   ];
   for (doc, name, value) in items {
      let _ = writeln!(code, "/// {doc}\npub const {name}: &str = {value:?};");
   }
   code
}

fn resource_script(product: &Product, icon: &Path, manifest: &Path) -> String {
   let version = env::var("CARGO_PKG_VERSION").unwrap_or_default();
   let numeric = version.split(['-', '+']).next().unwrap_or("0.0.0").replace('.', ",");
   format!(
      r#"#pragma code_page(65001)
1 ICON "{icon}"
1 24 "{manifest}"

1 VERSIONINFO
FILEVERSION {numeric},0
PRODUCTVERSION {numeric},0
FILEOS 0x40004
FILETYPE 0x1
BEGIN
  BLOCK "StringFileInfo"
  BEGIN
    BLOCK "040904B0"
    BEGIN
      VALUE "CompanyName", "{publisher}"
      VALUE "FileDescription", "{description}"
      VALUE "FileVersion", "{version}"
      VALUE "InternalName", "launcher"
      VALUE "OriginalFilename", "launcher.exe"
      VALUE "ProductName", "{name}"
      VALUE "ProductVersion", "{version}"
    END
  END
  BLOCK "VarFileInfo"
  BEGIN
    VALUE "Translation", 0x409, 1200
  END
END
"#,
      icon = rc_path(icon),
      manifest = rc_path(manifest),
      publisher = rc_string(&product.publisher),
      description = rc_string(&format!("{} Launcher", product.app_name)),
      name = rc_string(&product.app_name),
   )
}

// rc.exe reads backslashes as escapes inside strings, and canonicalize() adds a \\?\ prefix it cannot open.
fn rc_path(path: &Path) -> String {
   let text = path.display().to_string();
   text.strip_prefix(r"\\?\").unwrap_or(&text).replace('\\', "/")
}

fn rc_string(value: &str) -> String {
   value.replace('"', "\"\"")
}

fn fail(message: &str) -> ! {
   panic!("launcher build: {message}");
}
