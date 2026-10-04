# Branding login WPF

Layar login WPF memakai `LoginStyle.Material` secara default. Pengembang memilih tampilan melalui `ApplyBranding`; pengguna akhir tidak mendapat tombol pemilihan tampilan.

```csharp
builder.ApplyBranding(new BrandingInfo {
   Title = "My Application",
   Tagline = "Your workspace",
   LoginStyle = LoginStyle.Material,
   LightLoginBackground = "pack://application:,,,/My.App;component/Assets/LoginLight.png",
   DarkLoginBackground = "pack://application:,,,/My.App;component/Assets/LoginDark.png"
});
```

Sesuaikan nama assembly dan tandai gambar sebagai WPF `Resource`. URI absolut ke gambar juga diterima. Core memuat bitmap (PNG, JPEG, BMP, ICO); format lain memerlukan `ILogoImageLoader` yang didaftarkan aplikasi. Lebar sekitar 1920 piksel disarankan. Bitmap yang lebih besar didekode dengan lebar maksimum 1920 tanpa memperbesar gambar kecil. Gambar memenuhi latar dengan `UniformToFill`, sehingga tepinya dapat terpotong.

Gambar mode aktif dicoba terlebih dahulu. Jika kosong atau gagal dimuat, gambar mode lain dicoba. Jika hanya satu gambar tersedia, kedua mode memakainya. Mode gelap yang meminjam gambar terang memakai lapisan `Scrim` dengan opacity 0,35; gambar gelap sendiri tidak diredupkan. Jika keduanya gagal, latar menjadi gradien tonal dari `PrimaryContainer`, melalui `SurfaceContainerLow`, ke `Surface`, mengikuti tema aktif. Ganti tema memuat ulang pilihan gambar dan lapisan redup.

Untuk memakai tampilan lama, atur `LoginStyle = LoginStyle.Classic`. Classic mengabaikan kedua properti background dan tetap memakai `Description`. Material menampilkan logo, judul, tagline, dan hak cipta; `Description` tidak ditampilkan. MAUI belum memakai `LoginStyle`, `LightLoginBackground`, atau `DarkLoginBackground`.

Style Material baru memakai kunci tersendiri di dictionary bersama. Field memakai `shared:FieldLabel.Text` untuk label mengambang; `Tag` tetap bebas. Latar takik dapat diatur melalui `FieldLabel.NotchBackground`, error melalui `FieldValidation.HasError`. Animasi label mengikuti `EnableAnimation` aplikasi; pemakai field di layar lain dapat memasang `FieldLabel.EnableAnimation` pada induknya.
