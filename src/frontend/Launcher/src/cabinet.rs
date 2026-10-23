use std::alloc::{Layout, alloc, dealloc};
use std::cell::RefCell;
use std::collections::{HashMap, HashSet};
use std::ffi::{CStr, CString, c_void};
use std::fs::{self, File, OpenOptions};
use std::io::{self, Read, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};
use std::sync::Arc;

use windows::Win32::Storage::Cabinets::*;
use windows::core::{BOOL, PCSTR, PSTR};

use crate::format::ReleaseFile;
use crate::install::{UpdatePhase, UpdateProgress};

/// Pembaca dan penulis Microsoft Cabinet LZX melalui cabinet.dll, tanpa proses eksternal.
pub struct Cabinet;

/// Batas satu cab; tidak ada spanning atau enkripsi.
pub const MAX_CAB_BYTES: u64 = 2_000_000_000;

struct State {
   root: PathBuf,
   paths: HashMap<String, PathBuf>,
   seen: HashSet<String>,
   temporary: usize,
   bytes: u64,
   progress: Arc<UpdateProgress>,
   error: Option<String>,
   extracting: bool,
   metadata_only: bool,
   // Set by the first extraction pass, which only sums the entry sizes for the progress total.
   counting: Option<u64>,
}

thread_local! { static STATE: RefCell<Option<State>> = const { RefCell::new(None) }; }

struct Scope;
impl Drop for Scope {
   fn drop(&mut self) {
      STATE.with(|s| *s.borrow_mut() = None);
   }
}
struct Fci(*mut c_void);
impl Drop for Fci {
   fn drop(&mut self) {
      unsafe {
         let _ = FCIDestroy(self.0);
      }
   }
}
struct Fdi(*mut c_void);
impl Drop for Fdi {
   fn drop(&mut self) {
      unsafe {
         let _ = FDIDestroy(self.0);
      }
   }
}

impl Cabinet {
   /// Membungkus hanya berkas yang diberikan; kegagalan tidak menimpa cab tujuan lama.
   pub fn create(path: &Path, root: &Path, files: &[String], progress: Arc<UpdateProgress>) -> io::Result<()> {
      let mut paths = HashMap::new();
      paths.insert("output.cab".into(), path.to_path_buf());
      let mut total = 0u64;
      for (index, name) in files.iter().enumerate() {
         valid_path(name)?;
         let source = root.join(name.replace('/', "\\"));
         total = total
            .checked_add(fs::metadata(&source)?.len())
            .ok_or_else(|| io::Error::other("Cab size overflow"))?;
         paths.insert(format!("source-{index}"), source);
      }
      if total > MAX_CAB_BYTES {
         return Err(io::Error::other(
            "Release exceeds the approximately 2 GB single-cab limit; cabinet spanning is not supported.",
         ));
      }
      progress.begin(UpdatePhase::Packing, total);
      let _scope = begin(root, paths, progress.clone(), false)?;
      let erf = ERF::default();
      let mut cab = CCAB {
         cb: MAX_CAB_BYTES as u32,
         cbFolderThresh: MAX_CAB_BYTES as u32,
         setID: 1,
         ..Default::default()
      };
      for (target, byte) in cab.szCab.iter_mut().zip(b"output.cab\0") {
         *target = *byte as i8;
      }
      let handle = Fci(unsafe {
         FCICreate(
            &erf,
            Some(placed),
            Some(allocate),
            Some(release_alloc),
            Some(fci_open),
            Some(fci_read),
            Some(fci_write),
            Some(fci_close),
            Some(fci_seek),
            Some(fci_delete),
            Some(temp_file),
            &cab,
            None,
         )
      });
      if handle.0.is_null() {
         return Err(failed("create cabinet", &erf));
      }
      for (index, name) in files.iter().enumerate() {
         progress.set_file(name);
         let source = CString::new(format!("source-{index}")).map_err(io::Error::other)?;
         let name = CString::new(name.replace('/', "\\")).map_err(io::Error::other)?;
         let ok = unsafe {
            FCIAddFile(
               handle.0,
               PCSTR(source.as_ptr().cast()),
               PCSTR(name.as_ptr().cast()),
               false,
               Some(next_cab),
               Some(status),
               Some(open_info),
               (tcompTYPE_LZX | (21 << tcompSHIFT_LZX_WINDOW)) as u16,
            )
         };
         if !ok.as_bool() {
            return Err(failed("compress cabinet", &erf));
         }
      }
      progress.set_file("Writing the cab");
      if !unsafe { FCIFlushCabinet(handle.0, false, Some(next_cab), Some(status)) }.as_bool() {
         return Err(failed("finish cabinet", &erf));
      }
      drop(handle);
      if fs::metadata(path)?.len() > MAX_CAB_BYTES {
         return Err(io::Error::other("Cab exceeds the approximately 2 GB single-cab limit."));
      }
      Ok(())
   }

   /// Mengekstrak ke folder kosong; traversal path, duplikat dan cab terpecah ditolak.
   pub fn extract(path: &Path, target: &Path, progress: Arc<UpdateProgress>) -> io::Result<()> {
      Self::extract_inner(path, target, progress, false)
   }

   /// Membaca metadata cab untuk memilih sumber sebelum mengekstrak payload ke staging instalasi.
   pub fn metadata(path: &Path, target: &Path, progress: Arc<UpdateProgress>) -> io::Result<()> {
      Self::extract_inner(path, target, progress, true)
   }

   fn extract_inner(path: &Path, target: &Path, progress: Arc<UpdateProgress>, metadata_only: bool) -> io::Result<()> {
      fs::create_dir_all(target)?;
      if fs::read_dir(target)?.next().is_some() {
         return Err(io::Error::other("Cab extraction target must be empty."));
      }
      let mut paths = HashMap::new();
      paths.insert("input.cab".into(), path.to_path_buf());
      let _scope = begin(target, paths, progress.clone(), true)?;
      STATE.with(|s| {
         if let Some(s) = s.borrow_mut().as_mut() {
            s.metadata_only = metadata_only;
            s.counting = (!metadata_only).then_some(0);
         }
      });
      let mut erf = ERF::default();
      let handle = Fdi(unsafe {
         FDICreate(
            Some(allocate),
            Some(fdi_free),
            Some(open),
            Some(read),
            Some(write),
            Some(close),
            Some(seek),
            cpu80386,
            &mut erf,
         )
      });
      if handle.0.is_null() {
         return Err(failed("open cabinet", &erf));
      }
      if !metadata_only {
         // Skipping every entry reads only the file headers, so this pass is quick.
         Self::copy(&handle, &erf)?;
         let total = STATE.with(|s| s.borrow_mut().as_mut().and_then(|s| s.counting.take()).unwrap_or(0));
         progress.begin(UpdatePhase::Unpacking, total);
      }
      Self::copy(&handle, &erf)
   }

   fn copy(handle: &Fdi, erf: &ERF) -> io::Result<()> {
      if !unsafe {
         FDICopy(
            handle.0,
            PCSTR(c"input.cab".as_ptr().cast()),
            PCSTR(c"".as_ptr().cast()),
            0,
            Some(notify),
            None,
            None,
         )
      }
      .as_bool()
      {
         return Err(failed("extract cabinet", erf));
      }
      Ok(())
   }
}

fn begin(
   root: &Path,
   paths: HashMap<String, PathBuf>,
   progress: Arc<UpdateProgress>,
   extracting: bool,
) -> io::Result<Scope> {
   STATE.with(|state| {
      if state.borrow().is_some() {
         return Err(io::Error::other(
            "A cabinet operation is already active on this thread.",
         ));
      }
      *state.borrow_mut() = Some(State {
         root: root.to_path_buf(),
         paths,
         seen: HashSet::new(),
         temporary: 0,
         bytes: 0,
         progress,
         error: None,
         extracting,
         metadata_only: false,
         counting: None,
      });
      Ok(Scope)
   })
}
fn valid_path(path: &str) -> io::Result<()> {
   let reserved = |segment: &str| {
      let base = segment.split('.').next().unwrap_or("").to_ascii_uppercase();
      ["CON", "PRN", "AUX", "NUL"].contains(&base.as_str())
         || (base.len() == 4
            && (base.starts_with("COM") || base.starts_with("LPT"))
            && matches!(base.as_bytes()[3], b'1'..=b'9'))
   };
   if path.split('/').any(reserved)
      || ReleaseFile::check_path(path).is_some()
      || !(path == "release.json" || path == "release.json.sig" || path.starts_with("binaries/"))
      || path
         .split('/')
         .any(|s| s.ends_with(['.', ' ']) || s.chars().any(|c| c < ' ' || "<>\"|?*".contains(c)))
   {
      return Err(io::Error::other("Cab contains an unsafe or unexpected path."));
   }
   Ok(())
}
fn failed(action: &str, erf: &ERF) -> io::Error {
   STATE.with(|s| {
      io::Error::other(
         s.borrow()
            .as_ref()
            .and_then(|s| s.error.clone())
            .unwrap_or_else(|| format!("Cannot {action} (cabinet error {}, type {}).", erf.erfOper, erf.erfType)),
      )
   })
}
fn remember(error: impl ToString) {
   STATE.with(|s| {
      if let Some(s) = s.borrow_mut().as_mut() {
         s.error = Some(error.to_string());
      }
   });
}
fn allowed() -> bool {
   STATE.with(|s| s.borrow().as_ref().is_some_and(|s| !s.progress.is_cancelled()))
}
unsafe fn string(ptr: PCSTR) -> io::Result<String> {
   unsafe {
      CStr::from_ptr(ptr.0.cast())
         .to_str()
         .map(str::to_owned)
         .map_err(io::Error::other)
   }
}

unsafe extern "system" fn allocate(bytes: u32) -> *mut c_void {
   let size = bytes as usize + 8;
   let Ok(layout) = Layout::from_size_align(size, 8) else {
      return std::ptr::null_mut();
   };
   unsafe {
      let ptr = alloc(layout);
      if ptr.is_null() {
         return ptr.cast();
      }
      ptr.cast::<usize>().write(size);
      ptr.add(8).cast()
   }
}
unsafe extern "system" fn release_alloc(ptr: *mut c_void) {
   if ptr.is_null() {
      return;
   }
   unsafe {
      let base = ptr.cast::<u8>().sub(8);
      let size = base.cast::<usize>().read();
      if let Ok(layout) = Layout::from_size_align(size, 8) {
         dealloc(base, layout);
      }
   }
}
unsafe extern "system" fn fdi_free(ptr: *const c_void) {
   unsafe {
      release_alloc(ptr.cast_mut());
   }
}
unsafe extern "system" fn open(name: PCSTR, flags: i32, _: i32) -> isize {
   if !allowed() {
      return -1;
   }
   let result = (|| {
      let name = unsafe { string(name)? };
      let path = STATE
         .with(|s| s.borrow().as_ref().and_then(|s| s.paths.get(&name).cloned()))
         .ok_or_else(|| io::Error::other("Cabinet requested an unknown path."))?;
      let mut options = OpenOptions::new();
      options.read(flags & 3 != 1).write(flags & 3 != 0);
      options.create(flags & 0x100 != 0).truncate(flags & 0x200 != 0);
      options.open(path)
   })();
   match result {
      Ok(file) => Box::into_raw(Box::new(file)) as isize,
      Err(error) => {
         remember(error);
         -1
      }
   }
}
unsafe extern "system" fn read(handle: isize, buffer: *mut c_void, count: u32) -> u32 {
   if !allowed() {
      return u32::MAX;
   }
   unsafe {
      match (&mut *(handle as *mut File)).read(std::slice::from_raw_parts_mut(buffer.cast(), count as usize)) {
         Ok(n) => n as u32,
         Err(e) => {
            remember(e);
            u32::MAX
         }
      }
   }
}
unsafe extern "system" fn write(handle: isize, buffer: *const c_void, count: u32) -> u32 {
   if !allowed() {
      return u32::MAX;
   }
   let limit = STATE.with(|s| {
      let mut state = s.borrow_mut();
      if let Some(state) = state.as_mut() {
         state.bytes += u64::from(count);
         if state.extracting && !state.metadata_only {
            state.progress.add_done(u64::from(count));
         }
         state.extracting && state.bytes > MAX_CAB_BYTES
      } else {
         true
      }
   });
   if limit {
      remember("Cab data exceeds the single-cab size limit.");
      return u32::MAX;
   }
   unsafe {
      match (&mut *(handle as *mut File)).write_all(std::slice::from_raw_parts(buffer.cast(), count as usize)) {
         Ok(()) => count,
         Err(e) => {
            remember(e);
            u32::MAX
         }
      }
   }
}
unsafe extern "system" fn close(handle: isize) -> i32 {
   unsafe {
      drop(Box::from_raw(handle as *mut File));
   }
   0
}
unsafe extern "system" fn seek(handle: isize, distance: i32, kind: i32) -> i32 {
   let from = match kind {
      0 => SeekFrom::Start(distance as u64),
      1 => SeekFrom::Current(i64::from(distance)),
      2 => SeekFrom::End(i64::from(distance)),
      _ => return -1,
   };
   unsafe {
      match (&mut *(handle as *mut File)).seek(from) {
         Ok(n) => i32::try_from(n).unwrap_or(-1),
         Err(e) => {
            remember(e);
            -1
         }
      }
   }
}
unsafe extern "system" fn fci_open(name: PCSTR, flags: i32, mode: i32, _: *mut i32, _: *mut c_void) -> isize {
   unsafe { open(name, flags, mode) }
}
unsafe extern "system" fn fci_read(h: isize, p: *mut c_void, n: u32, _: *mut i32, _: *mut c_void) -> u32 {
   unsafe { read(h, p, n) }
}
unsafe extern "system" fn fci_write(h: isize, p: *mut c_void, n: u32, _: *mut i32, _: *mut c_void) -> u32 {
   unsafe { write(h, p, n) }
}
unsafe extern "system" fn fci_close(h: isize, _: *mut i32, _: *mut c_void) -> i32 {
   unsafe { close(h) }
}
unsafe extern "system" fn fci_seek(h: isize, d: i32, k: i32, _: *mut i32, _: *mut c_void) -> i32 {
   unsafe { seek(h, d, k) }
}
unsafe extern "system" fn fci_delete(name: PCSTR, _: *mut i32, _: *mut c_void) -> i32 {
   let result = unsafe { string(name) }
      .and_then(|name| {
         STATE.with(|s| {
            s.borrow()
               .as_ref()
               .and_then(|s| s.paths.get(&name).cloned())
               .ok_or_else(|| io::Error::other("Unknown cabinet temp file"))
         })
      })
      .and_then(fs::remove_file);
   if result.is_ok() { 0 } else { -1 }
}
unsafe extern "system" fn placed(_: *mut CCAB, _: PCSTR, _: i32, _: BOOL, _: *mut c_void) -> i32 {
   0
}
unsafe extern "system" fn next_cab(_: *mut CCAB, _: u32, _: *mut c_void) -> BOOL {
   remember("Release exceeds the approximately 2 GB single-cab limit; spanning is not supported.");
   BOOL(0)
}
unsafe extern "system" fn status(kind: u32, _: u32, uncompressed: u32, _: *mut c_void) -> i32 {
   if kind == statusFile {
      STATE.with(|s| {
         if let Some(s) = s.borrow().as_ref() {
            s.progress.add_done(u64::from(uncompressed));
         }
      });
   }
   if allowed() { 0 } else { -1 }
}
unsafe extern "system" fn open_info(
   name: PCSTR,
   date: *mut u16,
   time: *mut u16,
   attributes: *mut u16,
   _: *mut i32,
   _: *mut c_void,
) -> isize {
   unsafe {
      *date = 0x21;
      *time = 0;
      *attributes = _A_NAME_IS_UTF as u16;
      open(name, 0, 0)
   }
}
unsafe extern "system" fn temp_file(name: PSTR, capacity: i32, _: *mut c_void) -> BOOL {
   let token = STATE.with(|s| {
      let mut state = s.borrow_mut();
      let state = state.as_mut()?;
      state.temporary += 1;
      let token = format!("temp-{}.cab", state.temporary);
      state.paths.insert(token.clone(), state.root.join(&token));
      Some(token)
   });
   let Some(token) = token else {
      return BOOL(0);
   };
   if token.len() + 1 > capacity as usize {
      return BOOL(0);
   }
   unsafe {
      std::ptr::copy_nonoverlapping(token.as_ptr(), name.0, token.len());
      *name.0.add(token.len()) = 0;
   }
   BOOL(1)
}
unsafe extern "system" fn notify(kind: FDINOTIFICATIONTYPE, notice: *mut FDINOTIFICATION) -> isize {
   if !allowed() {
      return -1;
   }
   unsafe {
      let notice = &*notice;
      if kind == fdintNEXT_CABINET || kind == fdintPARTIAL_FILE {
         remember("Multi-part cabinets are not supported.");
         return -1;
      }
      if kind == fdintCLOSE_FILE_INFO {
         close(notice.hf);
         return 1;
      }
      if kind != fdintCOPY_FILE {
         return 0;
      }
      let counted = STATE.with(|s| {
         let mut state = s.borrow_mut();
         let count = state.as_mut().and_then(|s| s.counting.as_mut())?;
         *count += u64::try_from(notice.cb).unwrap_or(0);
         Some(())
      });
      if counted.is_some() {
         return 0;
      }
      let result = (|| {
         let name = string(PCSTR(notice.psz1.0))?.replace('\\', "/");
         valid_path(&name)?;
         if STATE.with(|s| s.borrow().as_ref().is_some_and(|s| s.metadata_only)) && name.starts_with("binaries/") {
            return Ok(None);
         }
         if !name.starts_with("binaries/") && notice.cb > 16 * 1024 * 1024 {
            return Err(io::Error::other("Cab metadata exceeds 16 MB."));
         }
         let target = STATE.with(|s| {
            let mut state = s.borrow_mut();
            let state = state
               .as_mut()
               .ok_or_else(|| io::Error::other("Missing cabinet state"))?;
            if notice.cb < 0 || !state.seen.insert(name.to_ascii_lowercase()) {
               return Err(io::Error::other("Duplicate or oversized cabinet entry."));
            }
            if state.seen.len() > 100_000 {
               return Err(io::Error::other("Too many cabinet entries."));
            }
            Ok(state.root.join(name.replace('/', "\\")))
         })?;
         fs::create_dir_all(
            target
               .parent()
               .ok_or_else(|| io::Error::other("Invalid cabinet entry"))?,
         )?;
         OpenOptions::new().write(true).create_new(true).open(target).map(Some)
      })();
      match result {
         Ok(Some(file)) => Box::into_raw(Box::new(file)) as isize,
         Ok(None) => 0,
         Err(e) => {
            remember(e);
            -1
         }
      }
   }
}
