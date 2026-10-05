# EmSys.Ui.Maui.Core

Namespace: `Em.Ui.Maui.Core`.

.NET MAUI UI components for Em System Android applications. The package includes navigation and account screens, controls, themes, and session storage backed by MAUI SecureStorage. It is a UI library; the `Em.Ui.Maui` application host is separate.

## Compatibility

Targets .NET 10 for Android (`net10.0-android`) with Android API level 21 or later. Depends on `EmSys.Libs` and `EmSys.Ui.Core`.

The consuming MAUI project must set `<UseMaui>true</UseMaui>` and reference `Microsoft.Maui.Controls` explicitly, as required by the .NET MAUI SDK.

## Install

```sh
dotnet package add EmSys.Ui.Maui.Core --prerelease
```

Source: [Em System](https://github.com/MatrixCode-ID/em-system). License: MIT.
