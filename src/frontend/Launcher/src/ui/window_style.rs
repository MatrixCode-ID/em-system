use std::cell::OnceCell;

use winsafe::{self as w, co, guard::DeleteObjectGuard, gui, msg, prelude::*};

thread_local! {
   // Created once per thread on first use, and kept until the thread ends: every heading shares it.
   static HEADING_FONT: OnceCell<Option<DeleteObjectGuard<w::HFONT>>> = const { OnceCell::new() };
}

/// Tampilan bersama semua jendela launcher: ikon produk, ukuran dalam piksel 96 DPI yang disesuaikan dengan DPI
/// layar, font sistem (Segoe UI), dan judul halaman yang lebih besar.
pub struct WindowStyle;

impl WindowStyle {
   /// Id resource ikon produk di exe (`build.rs`).
   pub const ICON_ID: u16 = 1;

   // region: Statics

   /// Opsi jendela utama berjudul `title` dengan area isi `size` (piksel 96 DPI): ikon produk, bisa
   /// di-minimize, tidak bisa diubah ukurannya, di tengah layar.
   pub fn main_opts(title: &str, size: (i32, i32)) -> gui::WindowMainOpts<'_> {
      gui::WindowMainOpts {
         title,
         class_icon: gui::Icon::Id(Self::ICON_ID),
         size: gui::dpi(size.0, size.1),
         style: co::WS::CAPTION
            | co::WS::SYSMENU
            | co::WS::MINIMIZEBOX
            | co::WS::CLIPCHILDREN
            | co::WS::BORDER
            | co::WS::VISIBLE,
         ..Default::default()
      }
   }

   /// Label teks di `parent` pada `position` dengan ukuran `size` (piksel 96 DPI). Teks yang terlalu panjang
   /// dibungkus ke baris berikutnya, dan `&` ditampilkan apa adanya.
   pub fn label(parent: &(impl GuiParent + 'static), text: &str, position: (i32, i32), size: (i32, i32)) -> gui::Label {
      gui::Label::new(
         parent,
         gui::LabelOpts {
            text,
            position: gui::dpi(position.0, position.1),
            size: gui::dpi(size.0, size.1),
            control_style: co::SS::LEFT | co::SS::NOPREFIX,
            ..Default::default()
         },
      )
   }

   /// Label satu baris untuk path: bagian tengah path yang tidak muat diganti `...`.
   pub fn path_label(parent: &(impl GuiParent + 'static), text: &str, position: (i32, i32), width: i32) -> gui::Label {
      gui::Label::new(
         parent,
         gui::LabelOpts {
            text,
            position: gui::dpi(position.0, position.1),
            size: gui::dpi(width, 18),
            control_style: co::SS::LEFT | co::SS::NOPREFIX | co::SS::PATHELLIPSIS,
            ..Default::default()
         },
      )
   }

   /// Tombol selebar 88 piksel. `ctrl_id` 0 = id otomatis; `IDOK` menjadikannya tombol Enter, `IDCANCEL`
   /// tombol Esc.
   pub fn button(parent: &(impl GuiParent + 'static), text: &str, position: (i32, i32), ctrl_id: u16) -> gui::Button {
      let default = ctrl_id == co::DLGID::OK.raw();
      gui::Button::new(
         parent,
         gui::ButtonOpts {
            text,
            position: gui::dpi(position.0, position.1),
            ctrl_id,
            control_style: if default {
               co::BS::DEFPUSHBUTTON
            } else {
               co::BS::PUSHBUTTON
            },
            ..Default::default()
         },
      )
   }

   /// Isian teks satu baris selebar `width`.
   pub fn edit(parent: &(impl GuiParent + 'static), text: &str, position: (i32, i32), width: i32) -> gui::Edit {
      gui::Edit::new(
         parent,
         gui::EditOpts {
            text,
            position: gui::dpi(position.0, position.1),
            width: gui::dpi_x(width),
            ..Default::default()
         },
      )
   }

   /// Checkbox selebar `width`.
   pub fn check_box(
      parent: &(impl GuiParent + 'static),
      text: &str,
      position: (i32, i32),
      width: i32,
      checked: bool,
   ) -> gui::CheckBox {
      gui::CheckBox::new(
         parent,
         gui::CheckBoxOpts {
            text,
            position: gui::dpi(position.0, position.1),
            size: gui::dpi(width, 20),
            check_state: if checked { co::BST::CHECKED } else { co::BST::UNCHECKED },
            ..Default::default()
         },
      )
   }

   /// Memberi `label` font judul halaman. Dipanggil setelah jendela induknya dibuat (di `wm_create`).
   pub fn apply_heading(label: &gui::Label) {
      HEADING_FONT.with(|font| {
         if let Some(font) = font.get_or_init(create_heading_font) {
            unsafe {
               label.hwnd().SendMessage(msg::WmSetFont {
                  hfont: font.raw_copy(),
                  redraw: true,
               });
            }
         }
      });
   }

   /// Menampilkan atau menyembunyikan `window`.
   pub fn set_visible(window: &impl GuiWindow, visible: bool) {
      window
         .hwnd()
         .ShowWindow(if visible { co::SW::SHOW } else { co::SW::HIDE });
   }

   /// Mengaktifkan atau menonaktifkan `window`.
   pub fn set_enabled(window: &impl GuiWindow, enabled: bool) {
      window.hwnd().EnableWindow(enabled);
   }

   /// Menjadikan isian `edit` hanya-baca (tetap bisa dipilih dan disalin).
   pub fn set_read_only(edit: &gui::Edit, read_only: bool) {
      unsafe {
         let _ = edit.hwnd().SendMessage(msg::EmSetReadOnly { read_only });
      }
   }

   /// Mengganti teks `window` (label, tombol, isian).
   pub fn set_text(window: &impl GuiWindow, text: &str) {
      let _ = window.hwnd().SetWindowText(text);
   }

   // endregion
}

// The message font of the system (Segoe UI on Windows 10 and 11), a third larger and semibold.
fn create_heading_font() -> Option<DeleteObjectGuard<w::HFONT>> {
   let mut metrics = w::NONCLIENTMETRICS::default();
   unsafe {
      w::SystemParametersInfo(
         co::SPI::GETNONCLIENTMETRICS,
         std::mem::size_of::<w::NONCLIENTMETRICS>() as u32,
         &mut metrics,
         co::SPIF::NoValue,
      )
   }
   .ok()?;
   let mut font = metrics.lfMessageFont;
   font.lfHeight = font.lfHeight * 4 / 3;
   font.lfWeight = co::FW::SEMIBOLD;
   w::HFONT::CreateFontIndirect(&font).ok()
}
