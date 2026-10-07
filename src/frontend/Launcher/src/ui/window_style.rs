use std::cell::OnceCell;

use winsafe::{self as w, co, guard::DeleteObjectGuard, gui, msg, prelude::*};

thread_local! {
   // Created once per thread on first use, and kept until the thread ends: every heading shares it.
   static HEADING_FONT: OnceCell<Option<DeleteObjectGuard<w::HFONT>>> = const { OnceCell::new() };
}

/// The look shared by all launcher windows: the product icon, sizes in 96 DPI pixels adjusted to the
/// screen DPI, the system font (Segoe UI), and a larger page title.
pub struct WindowStyle;

impl WindowStyle {
   /// Id resource ikon produk di exe (`build.rs`).
   pub const ICON_ID: u16 = 1;

   // region: Statics

   /// The options of a main window titled `title` with a content area of `size` (96 DPI pixels): the product
   /// icon, can be minimized, cannot be resized, centered on the screen.
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

   /// A text label in `parent` at `position` with size `size` (96 DPI pixels). Text that is too long is
   /// wrapped to the next line, and `&` is displayed as it is.
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

   /// A single-line label for a path: the middle part of a path that does not fit is replaced with `...`.
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

   /// A button 88 pixels wide. `ctrl_id` 0 = an automatic id; `IDOK` makes it the Enter button, `IDCANCEL`
   /// the Esc button.
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

   /// A single-line text field of width `width`.
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

   /// Gives `label` the page title font. Called after its parent window has been created (in `wm_create`).
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

   /// Shows or hides `window`.
   pub fn set_visible(window: &impl GuiWindow, visible: bool) {
      window
         .hwnd()
         .ShowWindow(if visible { co::SW::SHOW } else { co::SW::HIDE });
   }

   /// Enables or disables `window`.
   pub fn set_enabled(window: &impl GuiWindow, enabled: bool) {
      window.hwnd().EnableWindow(enabled);
   }

   /// Makes the field `edit` read-only (it can still be selected and copied).
   pub fn set_read_only(edit: &gui::Edit, read_only: bool) {
      unsafe {
         let _ = edit.hwnd().SendMessage(msg::EmSetReadOnly { read_only });
      }
   }

   /// Replaces the text of `window` (a label, button, or field).
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
