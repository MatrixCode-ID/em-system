use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::thread;

use winsafe::{self as w, co, gui, prelude::*};

use crate::install::UpdateProgress;
use crate::launcher_log::LauncherLog;

use super::{PROGRESS_PANEL_HEIGHT, ProgressPanel, WindowStyle};

const TIMER_ID: usize = 1;
const TIMER_MS: u32 = 200;
const WIDTH: i32 = 460;
const MARGIN: i32 = 16;

/// Jendela progres untuk pekerjaan updater (update, repair) yang dijalankan tanpa `--quiet`.
///
/// Pekerjaannya berjalan di thread pekerja; jendelanya membaca [`UpdateProgress`] setiap 200 ms dan menutup
/// diri begitu pekerjaan selesai. **Cancel** (atau tombol tutup jendela) meminta pembatalan lewat
/// `UpdateProgress::cancel`, lalu jendelanya tetap terbuka sampai pekerja benar-benar berhenti.
pub struct ProgressWindow;

impl ProgressWindow {
   // region: Statics

   /// Menjalankan `job` di thread pekerja sambil menampilkan `progress` di jendela berjudul `title`, lalu
   /// mengembalikan hasil `job`. Memblokir sampai `job` selesai.
   ///
   /// `job` boleh meminjam data milik pemanggil (misalnya `Updater` yang meminjam sumber dan folder instalasi),
   /// karena thread pekerjanya dijamin selesai sebelum fungsi ini kembali.
   pub fn run<R: Send>(title: &str, progress: &Arc<UpdateProgress>, job: impl FnOnce() -> R + Send) -> R {
      let finished = Arc::new(AtomicBool::new(false));
      thread::scope(|scope| {
         let worker = {
            let finished = FinishedFlag(finished.clone());
            scope.spawn(move || {
               // Moved into the thread, so it is dropped (and the flag set) even when the job panics.
               let _finished = finished;
               job()
            })
         };
         if let Err(error) = Self::show(title, progress.clone(), finished) {
            LauncherLog::error(format!("The progress window failed: {error}"));
         }
         match worker.join() {
            Ok(result) => result,
            Err(panic) => std::panic::resume_unwind(panic),
         }
      })
   }

   // endregion
}

impl ProgressWindow {
   fn show(title: &str, progress: Arc<UpdateProgress>, finished: Arc<AtomicBool>) -> w::AnyResult<i32> {
      let height = MARGIN + 24 + PROGRESS_PANEL_HEIGHT + 12 + 26 + MARGIN;
      let wnd = gui::WindowMain::new(WindowStyle::main_opts(title, (WIDTH, height)));
      let heading = gui::Label::new(
         &wnd,
         gui::LabelOpts {
            text: title,
            position: gui::dpi(MARGIN, MARGIN),
            size: gui::dpi(WIDTH - 2 * MARGIN, 20),
            control_style: co::SS::LEFT | co::SS::NOPREFIX,
            ..Default::default()
         },
      );
      let panel = ProgressPanel::new(&wnd, (MARGIN, MARGIN + 28), WIDTH - 2 * MARGIN);
      let cancel = gui::Button::new(
         &wnd,
         gui::ButtonOpts {
            text: "Cancel",
            position: gui::dpi(WIDTH - MARGIN - 88, height - MARGIN - 26),
            ctrl_id: co::DLGID::CANCEL.raw(),
            ..Default::default()
         },
      );

      let request_cancel = {
         let (progress, cancel) = (progress.clone(), cancel.clone());
         move || {
            progress.cancel();
            cancel.hwnd().EnableWindow(false);
         }
      };

      wnd.on().wm_create({
         let (wnd, heading, panel, progress) = (wnd.clone(), heading.clone(), panel.clone(), progress.clone());
         move |_| {
            WindowStyle::apply_heading(&heading);
            panel.refresh(&progress);
            wnd.hwnd().SetTimer(TIMER_ID, TIMER_MS, None)?;
            Ok(0)
         }
      });
      wnd.on().wm_timer(TIMER_ID, {
         let wnd = wnd.clone();
         move || {
            panel.refresh(&progress);
            if finished.load(Ordering::Acquire) {
               wnd.hwnd().KillTimer(TIMER_ID)?;
               wnd.hwnd().DestroyWindow()?;
            }
            Ok(())
         }
      });
      // The window only closes itself, when the work is done; closing it earlier means cancel.
      wnd.on().wm_close({
         let request_cancel = request_cancel.clone();
         move || {
            request_cancel();
            Ok(())
         }
      });
      cancel.on().bn_clicked(move || {
         request_cancel();
         Ok(())
      });

      wnd.run_main(None)
   }
}

// Sets the flag when dropped: at the end of the job, or while a panic unwinds out of it.
struct FinishedFlag(Arc<AtomicBool>);

impl Drop for FinishedFlag {
   fn drop(&mut self) {
      self.0.store(true, Ordering::Release);
   }
}
