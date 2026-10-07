use std::cell::Cell;
use std::rc::Rc;

use winsafe::{self as w, co, gui, prelude::*};

use crate::install::{UpdatePhase, UpdateProgress};

use super::format_size;

// Resolution of the progress bar; byte counts do not fit its 32-bit range.
const BAR_STEPS: u32 = 1000;

/// The height of the [`ProgressPanel`] in 96 DPI pixels.
pub const PROGRESS_PANEL_HEIGHT: i32 = 92;

/// The control that shows [`UpdateProgress`]: the status text, a bar based on bytes, the name of the file
/// being worked on, then the byte count, the speed, and the estimated time left. A phase whose size is not
/// known yet (checking the release, finishing) uses a marquee bar.
///
/// It is created in the parent window before that window is created, then updated with [`Self::refresh`]
/// from the window's timer, about every 200 ms. Its clones point to the same controls.
#[derive(Clone)]
pub struct ProgressPanel {
   status: gui::Label,
   bar: gui::ProgressBar,
   file: gui::Label,
   detail: gui::Label,
   marquee: Rc<Cell<bool>>,
}

impl ProgressPanel {
   // region: Statics

   /// Creates its controls in `parent`, starting at `position` with width `width` (96 DPI pixels).
   pub fn new(parent: &(impl GuiParent + 'static), position: (i32, i32), width: i32) -> Self {
      let (x, y) = position;
      let label = |top: i32, style: co::SS| {
         gui::Label::new(
            parent,
            gui::LabelOpts {
               text: "",
               position: gui::dpi(x, top),
               size: gui::dpi(width, 18),
               control_style: co::SS::LEFT | co::SS::NOPREFIX | style,
               ..Default::default()
            },
         )
      };
      let status = label(y, co::SS::ENDELLIPSIS);
      let bar = gui::ProgressBar::new(
         parent,
         gui::ProgressBarOpts {
            position: gui::dpi(x, y + 22),
            size: gui::dpi(width, 18),
            range: (0, BAR_STEPS),
            ..Default::default()
         },
      );
      let file = label(y + 48, co::SS::PATHELLIPSIS);
      let detail = label(y + 70, co::SS::ENDELLIPSIS);
      Self {
         status,
         bar,
         file,
         detail,
         marquee: Rc::new(Cell::new(false)),
      }
   }

   // endregion

   // region: Methods

   /// Shows or hides all its controls.
   pub fn set_visible(&self, visible: bool) {
      let show = if visible { co::SW::SHOW } else { co::SW::HIDE };
      for hwnd in [
         self.status.hwnd(),
         self.bar.hwnd(),
         self.file.hwnd(),
         self.detail.hwnd(),
      ] {
         hwnd.ShowWindow(show);
      }
   }

   /// Shows the current state of `progress`.
   pub fn refresh(&self, progress: &UpdateProgress) {
      let phase = progress.phase();
      let total = progress.total_bytes();
      let done = progress.done_bytes().min(total);
      let status = match phase {
         _ if progress.is_cancelled() => "Cancelling...",
         UpdatePhase::Idle | UpdatePhase::Checking => "Checking the release...",
         UpdatePhase::Verifying => "Checking the installed files...",
         UpdatePhase::Preparing => "Downloading the new version...",
         UpdatePhase::Finishing => "Finishing...",
      };
      set_text(self.status.hwnd(), status);

      let sized = matches!(phase, UpdatePhase::Verifying | UpdatePhase::Preparing) && total > 0;
      if sized {
         if self.marquee.replace(false) {
            self.bar.set_marquee(false);
         }
         self
            .bar
            .set_position((done as f64 / total as f64 * BAR_STEPS as f64) as u32);
      } else if !self.marquee.replace(true) {
         self.bar.set_marquee(true);
      }

      set_text(self.file.hwnd(), &progress.current_file());
      let detail = match phase {
         UpdatePhase::Verifying if sized => format!("{} of {} checked", format_size(done), format_size(total)),
         UpdatePhase::Preparing if sized => {
            let mut detail = format!("{} of {}", format_size(done), format_size(total));
            let speed = progress.bytes_per_second();
            if speed > 0 {
               detail.push_str(&format!(
                  ", {}/s, {} left",
                  format_size(speed),
                  format_duration((total - done) / speed)
               ));
            }
            detail
         }
         _ => String::new(),
      };
      set_text(self.detail.hwnd(), &detail);
   }

   // endregion
}

// Sets a text only when it changed, so the labels do not flicker on every timer tick.
fn set_text(hwnd: &w::HWND, text: &str) {
   if hwnd.GetWindowText().is_ok_and(|current| current != text) {
      let _ = hwnd.SetWindowText(text);
   }
}

fn format_duration(seconds: u64) -> String {
   match seconds {
      0..60 => format!("about {} s", seconds.max(1)),
      60..3600 => format!("about {} min", seconds.div_ceil(60)),
      _ => format!("about {} h {} min", seconds / 3600, seconds % 3600 / 60),
   }
}
