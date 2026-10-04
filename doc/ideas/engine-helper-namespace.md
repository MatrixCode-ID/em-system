# Namespace untuk class `Helper` engine

- Tanggal: 2026-10-04
- Status: diskusi

## Latar belakang

`Em.Api.Core` punya `public static class Helper` tanpa namespace (global namespace, `src/backend/Em.Api.Core/Api/Core/Helper.cs`). Saat host menambahkan `Helper` sendiri (`Em.Api.Helper`, `EmPoriumHouse.Api.Helper` untuk `ApplyConfig`), `Program.cs` yang top-level tidak bisa memanggilnya sebagai `Helper.ApplyConfig(...)`:

- `using Em.Api;` membuat `Helper` tetap teresolusi ke class global milik engine (CS0117).
- Alias `using Helper = ...;` ditolak karena bentrok dengan tipe di global namespace (CS0576).

Akibatnya host memanggil dengan nama lengkap: `Em.Api.Helper.ApplyConfig(builder)`.

## Usulan

Pindahkan `Helper` engine ke namespace, misalnya `Em.Api.Core`, sehingga host bebas memakai nama `Helper` sendiri dan `Program.cs` cukup menulis `Helper.ApplyConfig(builder)`.

## Pertanyaan terbuka

- Berapa banyak pemakai `Helper` global di engine, modul, dan EmPorium yang perlu ditambah `using`? Perubahan ini breaking untuk modul pihak lain yang memakainya.
- Apakah namanya sekalian dibuat lebih spesifik (misalnya `EmApiHelper`) atau cukup dipindah namespace?
