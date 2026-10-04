# Plan — Banyak koneksi database dan multi-provider

Status: **sudah dieksekusi** — 2026-09-17
Dibuat: 2026-09-17 — hasil pembahasan stub `builder.AddExtraDbConn` di `src/backend/Em.Api/Program.cs`
dan `Models/Context.cs` milik module module contoh.

---

## Kenapa ada berkas ini

Fondasi ini akan menampung migrasi aplikasi lama yang datanya tersebar di beberapa database, dan
tidak semuanya tentu di provider yang sama. Sekarang seluruh backend masih terikat pada satu
connection string tunggal: `EmAppBuilder.SetDbProvider` menyimpannya di satu pasang field, dan
`ApplyDbContextRegistrations` (`src/backend/Em.Api.Core/Api/Core/EmApp.cs:127`) meneruskan
pasangan itu ke setiap registrasi DbContext. Tidak ada jalan bagi module untuk menyebut database
lain.

Tiga kondisi yang jadi titik berangkat — **backend saat ini tidak kompilasi**:

1. `src/backend/Em.Api/Program.cs:28-29` memanggil `builder.AddExtraDbConn(...)` yang belum ada di
   `EmAppBuilder`.
2. `src/backend/modules/8XX-NSM/Em.Sample/Extensions.cs:12` memanggil
   `builder.AddDbContext<Context>("MssqlNsmDb")` — overload bernama itu belum ada; yang ada hanya
   `AddDbContext<TContext>()` tanpa parameter.
3. `src/backend/modules/8XX-NSM/Em.Sample/Models/Context.cs:7` menerima
   `DbContextOptions<ApiCoreContext>`, bukan `DbContextOptions<Context>`.

`DatabaseProvider.PostgreSql` juga sudah ditambahkan ke enum, tapi belum ada satu pun kode yang
menanganinya.

## Keputusan desain yang sudah diambil

| Hal | Keputusan |
| --- | --- |
| Nama koneksi disebut di mana | di titik registrasi — `builder.AddDbContext<TContext>("MssqlNsmDb")`. **Bukan** lewat overload ctor `EmDbContext(options, name)` |
| Kenapa begitu | engine tahu namanya sebelum `DbContextOptions` dibangun, jadi tidak perlu menyentuh `OnConfiguring` maupun internal EF, dan nama yang salah ketik bisa ditolak saat startup |
| Koneksi default | ikut registry yang sama dengan nama cadangan `"Default"`; `SetDbProvider` yang mengisinya |
| Jalur ADO/Dapper | keyed DI — `GetService<IDbConnection>("MssqlNsmDb")`, plus factory untuk query yang jalan paralel |
| Lingkup | daftar koneksi **beku sejak startup**; belum ada koneksi yang ditentukan per-request |
| Provider | ditentukan per koneksi, bukan satu untuk seluruh aplikasi |

## Langkah

### 1. Registry koneksi di `EmAppBuilder`

Berkas: `src/backend/Em.Api.Core/Api/Shared/EmAppBuilder.cs`

- Record kecil `DbConnectionInfo(string Name, string ConnectionString, DatabaseProvider Provider)`.
- `Dictionary<string, DbConnectionInfo>` ber-comparer `OrdinalIgnoreCase`, plus const
  `DefaultConnectionName = "Default"`.
- `SetDbProvider(connectionString, provider)` mengisi entri `"Default"` di registry itu. Properti
  `ConnectionString`/`DatabaseProvider` yang ada sekarang dibaca dari entri tersebut, supaya
  pemeriksaan "connection string belum diisi" di `ApplyDbContextRegistrations` tetap berlaku.
- `AddExtraDbConn(name, connectionString, provider)` — publik, menolak dengan pesan jelas kalau:
  namanya kosong, connection string-nya kosong, namanya sama dengan `"Default"`, atau namanya sudah
  terdaftar (dibandingkan case-insensitive). Pola validasi dan `Logger.LogInformation`-nya mengikuti
  `AddClaims` serta `AddDebugToken` yang sudah ada di berkas yang sama.

### 2. Registrasi DbContext per nama koneksi

Berkas: `EmAppBuilder.cs`, `EmApp.cs`

- `DbContextRegistrations` sekarang `List<Action<IServiceCollection, DatabaseProvider, string>>` —
  provider dan connection string disuntikkan saat apply. Ganti jadi delegate yang menerima
  **registry**-nya, sehingga tiap registrasi mencari namanya sendiri saat apply. Efeknya: urutan
  pemanggilan di `Program.cs` tidak lagi penting — module boleh didaftarkan sebelum
  `AddExtraDbConn` dipanggil.
- Overload baru, nama method tetap: `AddDbContext<TContext>(string connectionName)` dan
  `AddDbContextFactory<TContext>(string connectionName)`. Yang tanpa parameter berarti `"Default"`.
- Nama yang tidak dikenal dilempar sebagai `InvalidOperationException` dari
  `ApplyDbContextRegistrations`, menyebut tipe context-nya, nama yang diminta, dan daftar nama yang
  memang terdaftar. Karena apply berjalan setelah seluruh callback builder selesai, salah ketik
  ketahuan saat server start — bukan saat query pertama.
- Satu module boleh mendaftarkan lebih dari satu context; tidak ada batasan "satu module satu
  database". Yang berlaku sebaliknya: **satu database satu tipe context**, karena model EF (pemetaan
  entity ke tabel) menempel pada tipe context-nya, sehingga satu tipe tidak bisa menunjuk dua
  database sekaligus.

### 2a. Satu module yang menyentuh beberapa database

Berkas: `src/backend/modules/8XX-NSM/Em.Sample/*`

Satu action di `SampleServices` bisa saja butuh data dari dua database sekaligus. Bentuknya: satu
tipe context per database, didaftarkan berdampingan —

```csharp
builder.AddDbContext<NsmContext>("MssqlNsmDb");
builder.AddDbContext<FamContext>("MssqlFamDb");
```

— dan di dalam action keduanya diminta lewat tipenya, tanpa key dan tanpa menyebut nama koneksi
lagi:

```csharp
var nsm = GetService<NsmContext>()!;
var fam = GetService<FamContext>()!;
```

Konsekuensi penamaan: `Models/Context.cs` yang sekarang bernama `Context` saja tidak cukup begitu
module-nya menyentuh dua database. Ganti jadi nama yang menyebut databasenya (`NsmContext`,
`FamContext`) sekalian saat langkah 5 dikerjakan, supaya tidak ada rename kedua nanti.

Kalau nanti ada dua database yang **strukturnya sama persis** — misalnya satu skema penjualan yang
disalin per PT — bentuknya lain lagi: satu kelas context abstrak berisi seluruh `DbSet`, lalu dua
turunan tipis yang hanya membedakan identitas tipenya. Belum dibutuhkan sekarang; dicatat supaya
`DbSet` tidak disalin dua kali saat kasus itu muncul.

### 3. Koneksi ADO/Dapper untuk module

Berkas: `EmAppBuilder.cs` (`RegisterDbConnection`, `RegisterDbCommand`, `CreateEmConnection`),
`src/backend/Em.Api.Core/Api/Core/ServicesBase.cs`

- Satu factory singleton, `IEmDbConnectionFactory` — `Create(string name)` mengembalikan
  `IDbConnection` baru untuk nama itu. Ini jadi primitifnya; isi `CreateEmConnection` yang sekarang
  private pindah ke sana. Awalan `Em` dipakai dengan alasan yang sama seperti `EmDbContext`:
  `IDbConnectionFactory` polos adalah nama yang juga dipakai beberapa library data lain, dan
  `Em.Api.Core` nanti terbit sebagai paket yang dipakai bersama library semacam itu.
- `RegisterDbConnection` mendaftarkan **keyed scoped** `IDbConnection` untuk setiap entri registry,
  key-nya nama koneksi, isinya panggilan ke factory di atas. Entri `"Default"` tetap juga
  didaftarkan tanpa key, jadi `GetService<IDbConnection>()` yang ada sekarang tidak berubah arti.
- Pendaftaran keyed dan non-keyed adalah dua entri berbeda di DI, jadi kalau keduanya sama-sama
  membuat sambungan sendiri, satu request yang menyebut `GetService<IDbConnection>()` dan
  `GetService<IDbConnection>("Default")` akan memegang dua sambungan ke database yang sama. Yang
  non-keyed karena itu tidak membuat apa-apa, hanya meneruskan ke yang ber-key `"Default"` —
  supaya satu request tetap satu sambungan per database, apa pun cara memintanya.
- `RegisterDbCommand` mengikuti pola yang sama, tetap transient dengan alasan yang sudah ditulis di
  doc method-nya.
- `ServicesBase` dapat overload `GetService<T>(string connectionName)` dan
  `GetService(Type, string connectionName)` yang meneruskan ke `GetKeyedService`. Nama method tetap,
  hanya bertambah overload.
- Nama koneksi di sini muncul di dalam badan method, jadi tidak bisa diperiksa saat startup seperti
  nama di `AddDbContext`. Supaya salah ketik tidak menyamar jadi `NullReferenceException` beberapa
  baris kemudian, overload itu melempar dengan pesan yang menyebut daftar nama terdaftar begitu
  nama koneksinya sendiri tidak dikenal — beda dari `GetService<T>()` biasa yang memang boleh
  mengembalikan `null` untuk service yang tidak terdaftar.

Bentuk akhirnya di service module:

```csharp
var conn = GetService<IDbConnection>("MssqlNsmDb")!;
var rows = await conn.QueryAsync<Foo>("select ...");
```

Yang scoped itu satu sambungan per-request per-database, jadi dua query yang dijalankan barengan
lewat `Task.WhenAll` akan bertabrakan di sambungan yang sama — persis alasan `AddDbContextFactory`
berdampingan dengan `AddDbContext`. Untuk kasus itu factory-nya dipanggil langsung, dan yang
memanggil pula yang menutup sambungannya.

### 4. PostgreSQL

Berkas: `src/backend/Em.Api.Core/Em.Api.Core.csproj`,
`src/backend/Em.Api.Core/Api/Core/Extensions.cs`, `EmAppBuilder.cs`

- Tambah `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 dan `Npgsql` 10.0.3. Paket ADO-nya disebut
  eksplisit mengikuti pola MySQL, yang juga menyebut `MySqlConnector` di samping paket EF-nya —
  karena jalur `IDbConnection` memakai tipe ADO itu langsung.
- `UseEmProvider`: `DatabaseProvider.PostgreSql` → `opt.UseNpgsql(connectionString)`.
- `CreateEmConnection`: `DatabaseProvider.PostgreSql` → `new NpgsqlConnection(connectionString)`.
- Buang cabang `default:` dan `or _` yang sekarang menjatuhkan provider tak dikenal ke SQL Server,
  ganti jadi lemparan. Tanpa itu, koneksi yang didaftarkan sebagai PostgreSQL hari ini dibuka
  sebagai SQL Server tanpa satu pun pesan.

### 5. Beresi stub yang sudah ditulis

- `Models/Context.cs`: `DbContextOptions<ApiCoreContext>` → `DbContextOptions<Context>`. Kalau tetap
  `<ApiCoreContext>`, DI menyuntikkan options milik core context — artinya module justru menempel ke
  database default, kebalikan dari yang dimaui. Sekalian ganti namanya jadi `NsmContext` sesuai
  langkah 2a.

### 6. Verifikasi

- Build `src/backend/Em.Api.slnx` dan `src/frontend/Em.Ui.Wpf.slnx`.
- Jalankan API dengan nama koneksi yang sengaja disalahketik — harus gagal saat start, dengan pesan
  yang menyebut nama-nama yang terdaftar.
- Jalankan API dengan nama yang benar — context milik module contoh harus resolve dan menunjuk
  `MssqlNsmDb`, bukan database default.

## Yang sengaja tidak dikerjakan di sini

- **Koneksi per-request.** Kalau nanti satu deployment melayani beberapa PT dengan salinan database
  masing-masing, connection string baru ketahuan setelah tahu siapa pemanggilnya — dan itu tidak
  bisa didaftarkan di DI saat start. Perubahannya jauh lebih besar: resolver yang berjalan di dalam
  scope request, `DbContextOptions` yang dibangun per request, dan cache model EF per koneksi.
  Registry di langkah 1 adalah satu-satunya tempat nama diterjemahkan jadi connection string, jadi
  kalau kebutuhan itu datang, yang berubah isi registry — bukan setiap module.
- **Transaksi dan join lintas database.** Dua koneksi berarti dua transaksi, dan EF tidak bisa
  menjoin dua context. Query aplikasi lama yang memakai nama tiga bagian (`[Db].[dbo].[Tabel]`)
  harus tetap lewat satu koneksi dengan SQL mentah, bukan dipecah jadi dua DbContext.
  <br>Ini langsung terasa begitu satu action menulis ke dua database (langkah 2a): `SaveChangesAsync`
  yang pertama sudah commit sebelum yang kedua dicoba, jadi kalau yang kedua gagal, yang pertama
  tidak ikut batal. Selama kedua database berada di instance SQL Server yang sama, jalan yang murah
  adalah satu koneksi ke salah satunya lalu menyebut yang lain dengan nama tiga bagian lewat SQL
  mentah; `TransactionScope` lintas koneksi menyeret MSDTC dan tidak sepadan. Kalau nanti memang ada
  action yang harus menulis ke dua database secara utuh, itu perlu dibahas tersendiri.
- **Migration per database.**
- **Memindahkan connection string ke appsettings/environment.** Sekarang semuanya tertulis di
  `Program.cs` lengkap dengan user dan password, dan ikut ter-commit.
