# WPF login branding

The WPF login screen uses `LoginStyle.Material` by default. The developer chooses the style through
`ApplyBranding`; end users do not get a style switch.

```csharp
builder.ApplyBranding(new BrandingInfo {
   Title = "My Application",
   Tagline = "Your workspace",
   LoginStyle = LoginStyle.Material,
   LightLoginBackground = "pack://application:,,,/My.App;component/Assets/LoginLight.png",
   DarkLoginBackground = "pack://application:,,,/My.App;component/Assets/LoginDark.png"
});
```

## Background images

- Adjust the assembly name and mark the images as WPF `Resource`. Absolute image URIs are accepted too.
- The core loads bitmaps (PNG, JPEG, BMP, ICO). Other formats need an `ILogoImageLoader` registered by the
  application (`AddLogoImageLoader<T>()`).
- A width of about 1920 pixels is recommended. Larger bitmaps are decoded at a maximum width of 1920;
  smaller images are not scaled up. The image fills the background with `UniformToFill`, so its edges may
  be cropped.

The image for the active theme is tried first. If it is empty or fails to load, the other theme's image is
tried. If only one image exists, both themes use it. Dark mode borrowing the light image adds a `Scrim`
layer at 0.35 opacity; a dedicated dark image is not dimmed. If both fail, the background becomes a tonal
gradient from `PrimaryContainer` through `SurfaceContainerLow` to `Surface`, following the active theme.
Switching themes reloads the image choice and the dimming layer.

## Classic style

Set `LoginStyle = LoginStyle.Classic` for the older layout. Classic ignores both background properties and
still shows `Description`. Material shows the logo, title, tagline and copyright; it does not show
`Description`. MAUI does not use `LoginStyle`, `LightLoginBackground` or `DarkLoginBackground` yet.

## Material fields

The Material styles use their own keys in the shared resource dictionary.

- `shared:FieldLabel.Text` sets the floating label; `Tag` remains free for other uses.
- `FieldLabel.NotchBackground` sets the background behind the label notch.
- `FieldValidation.HasError` shows the error state.
- Label animation follows the application's `EnableAnimation`. Other screens using these fields can set
  `FieldLabel.EnableAnimation` on a parent element.
