# Frontend Em

- `Em.Ui.Wpf.slnx` memuat host desktop `Em.Ui.Wpf` dan library bersama yang dibutuhkan.
- `Em.Ui.Maui.slnx` memuat host Android `Em.Ui.Maui` dan library bersama yang dibutuhkan.
- `Launcher` adalah installer dan updater Rust untuk host WPF. Identitas produk ada di `Launcher/product.toml`. Jalankan `pwsh -File src/frontend/Launcher/build-dist.ps1` dari root repo untuk menghasilkan `dist/launcher/launcher.exe`; build WPF akan menyalin file tersebut bila tersedia.

Kedua host memakai engine UI tanpa modul bisnis dari proyek asal. Build dengan `dotnet build src/frontend/Em.Ui.Wpf.slnx` dan `dotnet build src/frontend/Em.Ui.Maui.slnx`.
