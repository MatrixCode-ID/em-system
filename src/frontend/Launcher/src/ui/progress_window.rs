use std::cell::RefCell;
use std::ffi::c_void;
use std::fmt::Display;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::thread;

use winsafe::{self as w, co, gui, prelude::*};

use crate::install::UpdateProgress;
use crate::launcher_log::LauncherLog;

use super::{PROGRESS_PANEL_HEIGHT, ProgressPanel, WindowStyle};

const TIMER_ID: usize = 1;
const TIMER_MS: u32 = 200;
const WIDTH: i32 = 460;
const MARGIN: i32 = 16;

type Completion = Arc<Mutex<Option<Result<(), String>>>>;

struct ProgressView {
   progress: Arc<UpdateProgress>,
   finished: Arc<AtomicBool>,
   completion: Completion,
   auto_close: bool,
   owner: *mut c_void,
}

thread_local! {
   // Keep the real parent available to WindowModal::show_modal, including its font lifetime.
   static OWNER: RefCell<Option<gui::WindowMain>> = const { RefCell::new(None) };
}

/// Shows worker progress and retains its finished state until the user chooses Finish.
/// Runner work that immediately starts the real application uses [`Self::run_before_launch`].
pub struct ProgressWindow;

impl ProgressWindow {
   /// Runs a job and keeps its result visible until the user closes the progress window.
   pub fn run<R: Send, E: Display + Send>(
      title: &str,
      progress: &Arc<UpdateProgress>,
      job: impl FnOnce() -> Result<R, E> + Send,
   ) -> Result<R, E> {
      Self::run_mode(title, progress, job, false)
   }

   /// Closes successful progress automatically so the caller can start the real application.
   pub fn run_before_launch<R: Send, E: Display + Send>(
      title: &str,
      progress: &Arc<UpdateProgress>,
      job: impl FnOnce() -> Result<R, E> + Send,
   ) -> Result<R, E> {
      Self::run_mode(title, progress, job, true)
   }

   fn run_mode<R: Send, E: Display + Send>(
      title: &str,
      progress: &Arc<UpdateProgress>,
      job: impl FnOnce() -> Result<R, E> + Send,
      auto_close: bool,
   ) -> Result<R, E> {
      let finished = Arc::new(AtomicBool::new(false));
      let completion: Completion = Arc::new(Mutex::new(None));
      thread::scope(|scope| {
         let worker = {
            let finished = FinishedFlag(finished.clone());
            let completion = completion.clone();
            scope.spawn(move || {
               let _finished = finished;
               let result = job();
               if let Ok(mut completion) = completion.lock() {
                  *completion = Some(result.as_ref().map(|_| ()).map_err(ToString::to_string));
               }
               result
            })
         };
         if let Err(error) = Self::show(title, progress.clone(), finished, completion, auto_close) {
            LauncherLog::error(format!("The progress window failed: {error}"));
         }
         match worker.join() {
            Ok(result) => result,
            Err(panic) => std::panic::resume_unwind(panic),
         }
      })
   }

   /// Opens progress as a true modal dialog of this setup form, retaining the parent's shared UI font.
   pub fn owned_by<R>(owner: &gui::WindowMain, f: impl FnOnce() -> R) -> R {
      let _restore = OwnerScope(OWNER.with(|previous| previous.replace(Some(owner.clone()))));
      f()
   }

   fn show(
      title: &str,
      progress: Arc<UpdateProgress>,
      finished: Arc<AtomicBool>,
      completion: Completion,
      auto_close: bool,
   ) -> w::AnyResult<i32> {
      let height = MARGIN + 24 + PROGRESS_PANEL_HEIGHT + 12 + 26 + MARGIN;
      let caption = format!("{} - {title}", crate::product::version_display());
      let owner = OWNER.with(|owner| owner.borrow().clone());
      let state = ProgressView {
         progress,
         finished,
         completion,
         auto_close,
         owner: owner.as_ref().map_or(std::ptr::null_mut(), |owner| owner.hwnd().ptr()),
      };
      if let Some(owner) = owner {
         // A nested run_main would delete winsafe's global font while setup still uses it.
         let wnd = gui::WindowModal::new(WindowStyle::modal_opts(&caption, (WIDTH, height)));
         Self::configure(&wnd, wnd.on(), title, height, state);
         let result = wnd.show_modal(&owner).map(|()| 0);
         owner.hwnd().EnableWindow(true);
         result
      } else {
         let wnd = gui::WindowMain::new(WindowStyle::main_opts(&caption, (WIDTH, height)));
         Self::configure(&wnd, wnd.on(), title, height, state);
         wnd.run_main(None)
      }
   }

   fn configure<W: GuiParent + 'static>(
      wnd: &W,
      events: &impl GuiEventsParent,
      title: &str,
      height: i32,
      state: ProgressView,
   ) {
      let ProgressView {
         progress,
         finished,
         completion,
         auto_close,
         owner,
      } = state;
      super::Logo::attach_to(wnd, events);
      let heading = WindowStyle::label(wnd, title, (MARGIN, MARGIN), (WIDTH - 2 * MARGIN, 20));
      let panel = ProgressPanel::new(wnd, (MARGIN, MARGIN + 28), WIDTH - 2 * MARGIN);
      let cancel = WindowStyle::button(
         wnd,
         "Cancel",
         (WIDTH - MARGIN - 88, height - MARGIN - 26),
         co::DLGID::CANCEL.raw(),
      );

      let request_close = {
         let (wnd, progress, cancel, finished) = (wnd.clone(), progress.clone(), cancel.clone(), finished.clone());
         move || -> w::AnyResult<()> {
            if finished.load(Ordering::Acquire) {
               if let Some(owner) = Owner::of(owner) {
                  owner.release();
               }
               wnd.hwnd().DestroyWindow()?;
            } else if progress.can_cancel() {
               progress.cancel();
               cancel.hwnd().EnableWindow(false);
            }
            Ok(())
         }
      };

      events.wm_create({
         let (wnd, heading, panel, progress, cancel) = (
            wnd.clone(),
            heading.clone(),
            panel.clone(),
            progress.clone(),
            cancel.clone(),
         );
         move |_| {
            super::Logo::created(&wnd);
            WindowStyle::apply_heading(&heading);
            panel.refresh(&progress);
            cancel
               .hwnd()
               .EnableWindow(progress.can_cancel() && !progress.is_cancelled());
            wnd.hwnd().SetTimer(TIMER_ID, TIMER_MS, None)?;
            Ok(0)
         }
      });
      events.wm_move({
         let wnd = wnd.clone();
         move |_| {
            if let Some(owner) = Owner::of(owner) {
               owner.follow(wnd.hwnd())?;
            }
            Ok(())
         }
      });
      events.wm_timer(TIMER_ID, {
         let (wnd, cancel) = (wnd.clone(), cancel.clone());
         move || {
            panel.refresh(&progress);
            cancel
               .hwnd()
               .EnableWindow(progress.can_cancel() && !progress.is_cancelled());
            if finished.load(Ordering::Acquire) {
               wnd.hwnd().KillTimer(TIMER_ID)?;
               let result = completion
                  .lock()
                  .ok()
                  .and_then(|result| result.clone())
                  .unwrap_or_else(|| Err("The operation stopped unexpectedly.".to_string()));
               if auto_close && result.is_ok() {
                  if let Some(owner) = Owner::of(owner) {
                     owner.release();
                  }
                  wnd.hwnd().DestroyWindow()?;
               } else {
                  panel.finish(
                     result.as_ref().map(|_| ()).map_err(String::as_str),
                     progress.is_cancelled(),
                  );
                  cancel.hwnd().SetWindowText("Finish")?;
                  cancel.hwnd().EnableWindow(true);
                  cancel.hwnd().SetFocus();
               }
            }
            Ok(())
         }
      });
      events.wm_close({
         let request_close = request_close.clone();
         move || request_close()
      });
      cancel.on().bn_clicked(request_close);
   }
}

// The owner window of a progress window, by its raw handle.
struct Owner(w::HWND);

impl Owner {
   fn of(ptr: *mut c_void) -> Option<Self> {
      (!ptr.is_null()).then(|| Owner(unsafe { w::HWND::from_ptr(ptr) }))
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

// Restores the previous owner even if an operation unwinds.
struct OwnerScope(Option<gui::WindowMain>);

impl Drop for OwnerScope {
   fn drop(&mut self) {
      OWNER.with(|owner| {
         owner.replace(self.0.take());
      });
   }
}

// Sets the flag when dropped: at the end of the job, or while a panic unwinds out of it.
struct FinishedFlag(Arc<AtomicBool>);

impl Drop for FinishedFlag {
   fn drop(&mut self) {
      self.0.store(true, Ordering::Release);
   }
}
