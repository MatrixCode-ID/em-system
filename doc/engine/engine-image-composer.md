# Image composer (WPF)

`ImageComposer` in `Em.Ui.Wpf.Core` arranges multiple images and arrows on a white
canvas measured in millimeters. It exports a single PNG; the containing screen
owns validation, persistence and any access checks. No server service, database
migration or permission registration is required.

## Add the control

Create a public `ImageComposerVm` property in the screen view model using its
normal `Get`/`Set` property storage, then bind the control without passing a view
reference to that view model:

```xml
xmlns:controls="clr-namespace:Em.Ui.Wpf.Controls;assembly=Em.Ui.Wpf.Core"

<controls:ImageComposer Composer="{Binding Picture}"
                        CanvasWidthMm="{Binding PictureWidthMm, Mode=TwoWay}"
                        CanvasHeightMm="{Binding PictureHeightMm, Mode=TwoWay}"
                        OutputDpi="300"
                        IsReadOnly="{Binding IsReadOnly}"
                        ShowToolbar="True" />
```

Without `Composer`, the control creates its own view model. Explicit control
settings are forwarded to a supplied view model; otherwise its existing settings
are retained. Width and height default to 100 mm and clamp to 10–1000 mm. Output
resolution defaults to 300 dpi and clamps to 72–1200 dpi. Size and resolution can
also be set directly on `Picture`. Changing canvas dimensions preserves every
item's millimeter coordinates and clips content beyond the new canvas edges.

`IsCanvasSizeEditable="False"` hides the built-in size inputs. `ShowToolbar="False"`
hides all toolbar controls while keeping commands available to the screen, for
example `Command="{Binding Picture.Commands[ClearCommand]}"`. All commands and
pointer/keyboard edits are disabled by `IsReadOnly`; the toolbar stays visible.
Collection and item setters remain available for deliberate host-side changes.

## Insert and arrange

Insert accepts multiple PNG, JPEG, BMP, GIF or TIFF files. Paste/Ctrl+V accepts an
image or copied image files. Images are fully decoded and frozen, so streams may
be disposed after insertion. Invalid files show a themed error while other files
continue loading. New images use their source DPI (96 if absent), fit within 80%
of the canvas and are centered. For extremely thin images, the minimum one mm
item size takes precedence over the 80% limit.

Click to select, drag to move, and use the eight handles to resize. Corner handles
preserve aspect ratio unless Shift is held; side handles resize one axis. The
rotation handle snaps to 15 degrees; Shift allows free rotation. Arrow keys move
one mm, or 0.1 mm with Ctrl. Delete removes the selected item, and Escape cancels
crop or clears the selection. Bring to front / Send to back change z-order. Fit
contains the selected image within the full canvas and resets rotation to zero.

Crop operates in the rotated image's local frame. Move or resize its temporary
rectangle, then Apply to discard outside pixels permanently. The retained region
stays at the same canvas position. Cancel/Escape leaves the image unchanged.
During crop, only Apply and Cancel commands are available. There is no undo.

The Arrow tool creates an arrow by dragging at least two mm. Drag its body to
move it or its endpoints individually. Four fixed printing inks are available:
red (#D32F2F), black (#000000), blue (#1565C0), yellow (#F9A825). Thickness is 0.5,
1 or 2 mm. A toolbar change updates the selected arrow and the next arrow's default.
Selecting an existing arrow shows its ink and thickness as the current defaults.
After drawing an arrow, the control returns to selection mode.

Image warnings indicate effective resolution below 150 dpi, computed from source
pixels and current physical size. They are advisory and never block export.
Chrome uses the shared Material theme; the canvas stays white in both themes.
Selection, rotation, crop and warning overlays are preview-only.

The zoom bar below the canvas scales the preview from 25% to 800%, where 100%
fits the canvas to the workspace. Ctrl+mouse wheel zooms around the pointer, and
dragging with the middle mouse button pans the view. The fit button or Ctrl+0
returns to 100% without panning. Zoom and pan (`ZoomPercent`, `PanX`, `PanY` on the
view model) are view state only: they never set `HasChanges` and do not affect
`FlattenPng`. They remain available when the control is read-only or the toolbar is
hidden.

## Load and save

```csharp
Picture.LoadInitialImage(savedPngBytes); // Also accepts a Stream.
// On the owning WPF UI/STA thread:
byte[] png = Picture.FlattenPng();
// Persist png successfully before resetting the dirty flag.
Picture.MarkSaved();
```

Loading a saved PNG replaces the composition with a locked base layer stretched
to the canvas and sets `HasChanges = false`. Individual images cannot be recovered
from flattened content. Only Clear is available until the user confirms clearing
the canvas. Size inputs are disabled in this state; host-side size setters still
work and stretch the base layer to the new dimensions.

`HasChanges` tracks item edits, collection/z-order edits and output settings. The
VM property has a public setter and `MarkSaved()` resets it; the control mirrors
it through a read-only dependency property. `IsEmpty`, `HasInitialImage` and
`HasLowResolution` are available on the VM. Clear asks for confirmation and sets
`HasChanges = true` even when clearing an empty canvas. The screen decides whether
an empty composition means deleting its stored image.

Flatten uses a high-quality `DrawingVisual` and `RenderTargetBitmap`, clipping to
the physical canvas. Pixel dimensions are rounded from mm / 25.4 × output DPI:
100 × 50 mm at 300 dpi produces 1181 × 591 pixels with DPI metadata. Large canvases
at high DPI require proportionally more memory. Export on the owning UI/STA thread;
do not call it through `Task.Run`.

This control is WPF-only, outputs PNG only and does not support drop, editable
saved layouts, text, filters, freehand drawing, manual zoom or undo. Visual
verification of the toolbar, handles, arrows, scaling and light/dark states is
pending; automated verification uses console tests only.
