use std::rc::Rc;

use windows::Win32::Foundation::{HINSTANCE, HWND, LPARAM, RECT};
use windows::Win32::Graphics::Gdi::*;
use windows::Win32::Graphics::Imaging::*;
use windows::Win32::System::Com::*;
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::HiDpi::GetDpiForWindow;
use windows::Win32::UI::Shell::SHCreateMemStream;
use windows::Win32::UI::WindowsAndMessaging::*;
use windows::core::{BOOL, PCWSTR};
use winsafe::{gui, prelude::*};

use crate::product;

// WIC is part of Windows: no PNG decoder or compression library is linked into the exe.
pub(super) struct Logo;
struct Pixels {
   width: u32,
   height: u32,
   bytes: Vec<u8>,
}
struct ShiftContext {
   parent: HWND,
   delta: i32,
}

impl Logo {
   pub(super) fn attach(wnd: &gui::WindowMain) {
      Self::attach_to(wnd, wnd.on());
   }

   pub(super) fn attach_to(wnd: &(impl GuiParent + 'static), events: &impl GuiEventsParent) {
      let pixels = Rc::new(if product::LOGO.is_empty() {
         None
      } else {
         decode(product::LOGO).ok()
      });
      events.wm_paint({
         let wnd = wnd.clone();
         move || {
            unsafe {
               paint(HWND(wnd.hwnd().ptr()), pixels.as_ref().as_ref());
            }
            Ok(())
         }
      });
   }
   pub(super) fn created(wnd: &impl GuiWindow) {
      let hwnd = HWND(wnd.hwnd().ptr());
      let dpi = unsafe { GetDpiForWindow(hwnd) }.max(96);
      let context = ShiftContext {
         parent: hwnd,
         delta: (64 * dpi / 96) as i32,
      };
      unsafe {
         let _ = EnumChildWindows(
            Some(hwnd),
            Some(shift_child),
            LPARAM(&context as *const ShiftContext as isize),
         );
      }
   }
}

unsafe extern "system" fn shift_child(child: HWND, offset: LPARAM) -> BOOL {
   unsafe {
      let context = &*(offset.0 as *const ShiftContext);
      if GetParent(child).ok() != Some(context.parent) {
         return BOOL(1);
      }
      let mut rect = RECT::default();
      if GetWindowRect(child, &mut rect).is_ok() {
         let parent = GetParent(child).unwrap_or_default();
         let mut point = windows::Win32::Foundation::POINT {
            x: rect.left,
            y: rect.top,
         };
         let _ = ScreenToClient(parent, &mut point);
         let _ = SetWindowPos(
            child,
            None,
            point.x,
            point.y + context.delta,
            0,
            0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE,
         );
      }
      BOOL(1)
   }
}

fn decode(bytes: &[u8]) -> windows::core::Result<Pixels> {
   unsafe {
      let initialized = CoInitializeEx(None, COINIT_APARTMENTTHREADED).is_ok();
      let result = (|| {
         let factory: IWICImagingFactory = CoCreateInstance(&CLSID_WICImagingFactory, None, CLSCTX_INPROC_SERVER)?;
         let stream = SHCreateMemStream(Some(bytes)).ok_or_else(windows::core::Error::from_thread)?;
         let decoder = factory.CreateDecoderFromStream(&stream, std::ptr::null(), WICDecodeMetadataCacheOnLoad)?;
         let frame = decoder.GetFrame(0)?;
         let (mut width, mut height) = (0, 0);
         frame.GetSize(&mut width, &mut height)?;
         if width == 0 || height == 0 || u64::from(width) * u64::from(height) > 64_000_000 {
            return Err(windows::core::Error::from_thread());
         }
         let converter = factory.CreateFormatConverter()?;
         converter.Initialize(
            &frame,
            &GUID_WICPixelFormat32bppPBGRA,
            WICBitmapDitherTypeNone,
            None,
            0.0,
            WICBitmapPaletteTypeCustom,
         )?;
         let mut pixels = vec![0; (width * height * 4) as usize];
         converter.CopyPixels(std::ptr::null(), width * 4, &mut pixels)?;
         Ok(Pixels {
            width,
            height,
            bytes: pixels,
         })
      })();
      if initialized {
         CoUninitialize();
      }
      result
   }
}

unsafe fn paint(hwnd: HWND, image: Option<&Pixels>) {
   unsafe {
      let mut ps = PAINTSTRUCT::default();
      let dc = BeginPaint(hwnd, &mut ps);
      let _ = FillRect(dc, &ps.rcPaint, GetSysColorBrush(COLOR_WINDOW));
      let scale = f64::from(GetDpiForWindow(hwnd).max(96)) / 96.0;
      let x = (16.0 * scale).round() as i32;
      let y = (12.0 * scale).round() as i32;
      if let Some(image) = image {
         let ratio = (240.0 * scale / f64::from(image.width)).min(48.0 * scale / f64::from(image.height));
         let width = (f64::from(image.width) * ratio).round() as i32;
         let height = (f64::from(image.height) * ratio).round() as i32;
         let info = BITMAPINFO {
            bmiHeader: BITMAPINFOHEADER {
               biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
               biWidth: image.width as i32,
               biHeight: -(image.height as i32),
               biPlanes: 1,
               biBitCount: 32,
               biCompression: BI_RGB.0,
               ..Default::default()
            },
            ..Default::default()
         };
         let mut bits = std::ptr::null_mut();
         if let Ok(bitmap) = CreateDIBSection(Some(dc), &info, DIB_RGB_COLORS, &mut bits, None, 0) {
            std::ptr::copy_nonoverlapping(image.bytes.as_ptr(), bits.cast::<u8>(), image.bytes.len());
            let memory = CreateCompatibleDC(Some(dc));
            let previous = SelectObject(memory, HGDIOBJ(bitmap.0));
            let _ = AlphaBlend(
               dc,
               x,
               y + ((48.0 * scale) as i32 - height) / 2,
               width,
               height,
               memory,
               0,
               0,
               image.width as i32,
               image.height as i32,
               BLENDFUNCTION {
                  BlendOp: AC_SRC_OVER as u8,
                  BlendFlags: 0,
                  SourceConstantAlpha: 255,
                  AlphaFormat: AC_SRC_ALPHA as u8,
               },
            );
            SelectObject(memory, previous);
            let _ = DeleteDC(memory);
            let _ = DeleteObject(HGDIOBJ(bitmap.0));
         }
      } else if let Ok(module) = GetModuleHandleW(None)
         && let Ok(icon) = LoadIconW(Some(HINSTANCE(module.0)), PCWSTR(std::ptr::without_provenance(1)))
      {
         let edge = (48.0 * scale).round() as i32;
         let _ = DrawIconEx(dc, x, y, icon, edge, edge, 0, None, DI_NORMAL);
      }
      let _ = EndPaint(hwnd, &ps);
   }
}
