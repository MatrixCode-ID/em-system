use std::cell::Cell;
use std::ffi::c_void;
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

thread_local! {
   // The window that owns progress windows opened on this thread; null = none.
   static OWNER: Cell<*mut c_void> = const { Cell::new(std::ptr::null_mut()) };
}

/// The progress window for updater work (update, repair) run without `--quiet`.
///
/// The work runs on a worker thread; the window reads [`UpdateProgress`] every 200 ms and closes itself
/// as soon as the work finishes. **Cancel** (or the window's close button) asks for cancellation through
/// `UpdateProgress::cancel`, and the window stays open until the worker has really stopped.
pub struct ProgressWindow;

impl ProgressWindow {
   // region: Statics

   /// Runs `job` on a worker thread while showing `progress` in a window titled `title`, then returns the
   /// result of `job`. Blocks until `job` finishes.
   ///
   /// `job` may borrow data owned by the caller (for example an `Updater` that borrows the source and the
   /// install folder), because the worker thread is guaranteed to finish before this function returns.
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

   /// Runs `f` with `owner` as the owner of every progress window it opens on this thread: the owner is
   /// disabled while the progress window is open, the progress window starts centered on it, and moving the
   /// progress window moves the owner along.
   pub fn owned_by<R>(owner: &w::HWND, f: impl FnOnce() -> R) -> R {
      let _restore = OwnerScope(OWNER.replace(owner.ptr()));
      f()
   }

   // endregion
}

impl ProgressWindow {
   fn show(title: &str, progress: Arc<UpdateProgress>, finished: Arc<AtomicBool>) -> w::AnyResult<i32> {
      let height = MARGIN + 24 + PROGRESS_PANEL_HEIGHT + 12 + 26 + MARGIN;
      let caption = format!("{} - {title}", crate::product::version_display());
      let wnd = gui::WindowMain::new(WindowStyle::main_opts(&caption, (WIDTH, height)));
      super::Logo::attach(&wnd);
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

      let owner = OWNER.get();
      wnd.on().wm_create({
         let (wnd, heading, panel, progress) = (wnd.clone(), heading.clone(), panel.clone(), progress.clone());
         move |_| {
            super::Logo::created(&wnd);
            WindowStyle::apply_heading(&heading);
            panel.refresh(&progress);
            if let Some(owner) = Owner::of(owner) {
               owner.adopt(wnd.hwnd())?;
            }
            wnd.hwnd().SetTimer(TIMER_ID, TIMER_MS, None)?;
            Ok(0)
         }
      });
      wnd.on().wm_move({
         let wnd = wnd.clone();
         move |_| {
            if let Some(owner) = Owner::of(owner) {
               owner.follow(wnd.hwnd())?;
            }
            Ok(())
         }
      });
      wnd.on().wm_timer(TIMER_ID, {
         let wnd = wnd.clone();
         move || {
            panel.refresh(&progress);
            if finished.load(Ordering::Acquire) {
               wnd.hwnd().KillTimer(TIMER_ID)?;
               // Enabled again before the progress window goes, so the owner gets the activation back.
               if let Some(owner) = Owner::of(owner) {
                  owner.release();
               }
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

      let result = wnd.run_main(None);
      if let Some(owner) = Owner::of(owner) {
         owner.release();
      }
      result
   }
}

// The owner window of a progress window, by its raw handle.
struct Owner(w::HWND);

impl Owner {
   fn of(ptr: *mut c_void) -> Option<Self> {
      (!ptr.is_null()).then(|| Owner(unsafe { w::HWND::from_ptr(ptr) }))
   }

   // Makes `progress` an owned window centered on the owner, and disables the owner like a modal dialog.
   fn adopt(&self, progress: &w::HWND) -> w::AnyResult<()> {
      unsafe { progress.SetWindowLongPtr(co::GWLP::HWNDPARENT, self.0.ptr() as isize) };
      let (owner, own) = (self.0.GetWindowRect()?, progress.GetWindowRect()?);
      let pos = w::POINT::with(
         (owner.left + owner.right - (own.right - own.left)) / 2,
         (owner.top + owner.bottom - (own.bottom - own.top)) / 2,
      );
      Self::place(progress, pos)?;
      self.0.EnableWindow(false);
      Ok(())
   }

   // Keeps the owner centered behind `progress` after the user moved it.
   fn follow(&self, progress: &w::HWND) -> w::AnyResult<()> {
      let (owner, own) = (self.0.GetWindowRect()?, progress.GetWindowRect()?);
      let pos = w::POINT::with(
         (own.left + own.right - (owner.right - owner.left)) / 2,
         (own.top + own.bottom - (owner.bottom - owner.top)) / 2,
      );
      if pos.x != owner.left || pos.y != owner.top {
         Self::place(&self.0, pos)?;
      }
      Ok(())
   }

   fn release(&self) {
      self.0.EnableWindow(true);
   }

   fn place(hwnd: &w::HWND, pos: w::POINT) -> w::AnyResult<()> {
      hwnd.SetWindowPos(
         w::HwndPlace::None,
         pos,
         w::SIZE::default(),
         co::SWP::NOSIZE | co::SWP::NOZORDER | co::SWP::NOACTIVATE,
      )?;
      Ok(())
   }
}

// Puts the previous owner back when `owned_by` ends, also while a panic unwinds out of it.
struct OwnerScope(*mut c_void);

impl Drop for OwnerScope {
   fn drop(&mut self) {
      OWNER.set(self.0);
   }
}

// Sets the flag when dropped: at the end of the job, or while a panic unwinds out of it.
struct FinishedFlag(Arc<AtomicBool>);

impl Drop for FinishedFlag {
   fn drop(&mut self) {
      self.0.store(true, Ordering::Release);
   }
}
