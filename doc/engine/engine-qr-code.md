# QR codes

The engine turns a short text (a serial number, a document id, a URL) into a QR code image. The same
service runs on the server, on WPF and on MAUI (Android); each UI core adds an image helper and a
themed control.

## Service

`Em.Shared.IQrCodeService` lives in `Em.Libs`. The engine registers `QrCodeService.Default` as a
singleton in `Em.Api.Core`, `Em.Ui.Wpf.Core` and `Em.Ui.Maui.Core`, so a host does not need to call
anything. There is no HTTP action: clients create codes locally.

```csharp
var qr = app.ServiceProvider.GetRequiredService<IQrCodeService>();
byte[] png = qr.CreatePng("0123456789");
byte[] dense = qr.CreatePng(url, new QrCodeOptions { ErrorCorrection = QrErrorCorrection.L, PixelsPerModule = 4 });
```

`QrCodeOptions`:

| Property | Default | Meaning |
| --- | --- | --- |
| `ErrorCorrection` | `M` | `L` (~7%), `M` (~15%), `Q` (~25%), `H` (~30%) of the code can be damaged and still read |
| `PixelsPerModule` | `10` | Pixel size of one module, 1-100 |
| `QuietZone` | `true` | Light border of 4 modules; keep it unless the area around the code is light and empty |
| `Dark` / `Light` | black / white | `QrColor` RGB of the modules and the background |

Text in digits or upper-case alphanumeric characters is encoded compactly. Text outside ISO-8859-1 is
encoded as UTF-8 with an ECI marker, so scanners read it back unchanged.

Errors: `null` text throws `ArgumentNullException`; empty text, or text too long for the chosen level
(at most 7089 digits at `L`, 3057 at `H`, fewer for other characters), throws `ArgumentException`
with `ParamName` `text`; a `PixelsPerModule` outside 1-100 throws `ArgumentOutOfRangeException`.

The implementation uses QRCoder 1.8.1 (MIT) and its managed PNG renderer: no `System.Drawing`,
SkiaSharp or native code. `QrCodeService` holds no state and is thread-safe.

## WPF

`Em.Ui.Wpf.Shared.QrCodeImage.Create(text, options, service)` returns a frozen `BitmapSource`, so it
can be built off the UI thread and bound from a view model.

`Em.Ui.Wpf.Controls.QrCodeView` (default style in `Themes/Generic.xaml`):

```xml
<controls:QrCodeView Text="{Binding Serial}" Size="160" ErrorCorrection="M" />
```

| Property | Default | Meaning |
| --- | --- | --- |
| `Text` | `null` | Encoded text; `null`/empty shows an empty tile |
| `Size` | `160` | Side of the square in device-independent units, quiet zone included |
| `ErrorCorrection` | `M` | Error correction level |
| `Error` | - | Read-only; why the last text could not be drawn (e.g. too long), else `null` |

Colors come from `Background`/`Foreground` (default `themeWindowBackgroundBrush` and
`themeWindowForegroundBrush`). The lighter of the two always becomes the tile and quiet zone and the
darker one the modules, so in a dark theme the code shows as a light rounded tile: inverted
(light-on-dark) codes are not read by many scanners. The image is drawn at the screen DPI and redrawn
when the text, the level or the theme changes.

## MAUI

`Em.Ui.Maui.Shared.QrCodeImage.Create(text, options, service)` returns an `ImageSource`.

`Em.Ui.Maui.Controls.QrCodeView` has the same `Text`, `Size`, `ErrorCorrection` and `Error`. Its
colors are the palette `surface` and `onSurface` roles of the current theme, with the same
dark-on-light rule; it follows `Application.RequestedThemeChanged` and draws at the display density.

```xml
<controls:QrCodeView Text="{Binding Serial}" Size="160" />
```

## Limits

- The controls only display; saving or printing a code goes through `CreatePng`.
- Not yet verified on screen: the WPF control in a running window and the MAUI control on a device or
  emulator, including scanning in a dark theme.
