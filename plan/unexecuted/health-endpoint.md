# Plan — Health endpoint

Status: **belum dieksekusi — usulan, menunggu keputusan**
Dibuat: 2026-09-22 — pecahan dari item TODO *"Putuskan mana dari CORS, HTTPS redirect, rate limit,
dan health endpoint yang dibutuhkan"*. Tiga yang lain sudah diputuskan di hari yang sama dan
tercatat di [src/backend/CLAUDE.md](../../src/backend/CLAUDE.md) bagian *Network-level protections*;
yang ini disisihkan karena bentuknya perlu dipikirkan dulu, bukan karena ditolak.

---

## 1. Kenapa ada berkas ini

Sekarang satu-satunya cara tahu API-nya hidup adalah memanggil sebuah action dan melihat apa yang
keluar — dan setiap action yang berarti menuntut identitas lebih dulu. Akibatnya tidak ada cara
menjawab pertanyaan "server ini hidup?" tanpa punya akun di dalamnya.

Empat pemakai yang berbeda menanyakan hal itu, dan keempatnya butuh jawaban dengan bentuk yang
berbeda pula:

| Yang bertanya | Yang ditanyakan | Bentuk jawaban yang ia mengerti |
| --- | --- | --- |
| Reverse proxy / load balancer | boleh tidak request diarahkan ke sini | status code, 200 atau 503, tanpa membaca isinya |
| Monitoring / uptime checker | sejak kapan ia mati | status code, dipanggil berulang tiap beberapa detik |
| Yang baru men-deploy | apa yang sudah jalan benar | keterangan terperinci, sekali panggil |
| Client sebelum login | server yang dipilih di combobox koneksi menjawab tidak | status code, cepat, tanpa punya token |

Yang pertama dan kedua yang menentukan bentuknya, karena merekalah yang memanggil paling sering dan
paling tidak bisa menawar formatnya.

## 2. Yang perlu diputuskan lebih dulu

Lima hal. Usulan untuk masing-masing ada di bagian berikutnya; yang di sini hanya daftarnya, supaya
kelihatan bahwa ini bukan satu keputusan melainkan lima.

1. Tempatnya — route tersendiri di luar dispatcher, atau action biasa lewat `/api/...`?
2. Apa yang diperiksa — cukup "prosesnya hidup", atau sampai "databasenya menjawab"?
3. Kalau sampai database: yang mana dari 13 koneksi yang terdaftar?
4. Berapa banyak yang boleh dilihat pemanggil yang tidak dikenal?
5. Ongkosnya — ia satu-satunya jalur yang berada di luar batas jatah request.

## 3. Usulan

### 3.1 Tempat — route tersendiri, bukan action

**Usulan: `/health`, dipetakan langsung di `EmApp.Run()`, di luar `/api/{module}/{action}`.**

Alasannya bukan selera. Sebuah action harus lewat `ProcessRequest`, dan di sana ia mewarisi tiga hal
yang justru salah untuk sebuah probe: ia dibungkus envelope `ActionResult` yang harus di-parse (probe
tidak mem-parse apa pun, ia membaca status code), ia melewati gerbang identitas (probe tidak punya
identitas), dan ia ikut dihitung jatah request — padahal probe yang memanggil tiap lima detik memang
seharusnya tidak pernah ditolak karena terlalu sering.

Konsekuensi yang harus diterima sadar: `/health` karena itu **tidak dibatasi jatah request**. Itu
yang membuat butir 3.5 di bawah wajib, bukan pemanis.

Catatan teknis: `MapFallback` di `Run()` sekarang menangkap segala yang bukan `/api/...`, jadi route
ini harus dipetakan **sebelum** baris itu.

### 3.2 Apa yang diperiksa — dua route, bukan satu

**Usulan: `/health` menjawab tanpa menyentuh database, `/health/ready` menyentuh database.**

Dua pertanyaan yang sering dikira satu:

- **hidup** (*liveness*) — prosesnya masih menjawab. Yang menanyakannya adalah yang berwenang
  me-restart: kalau jawabannya tidak, restart menolong.
- **siap** (*readiness*) — ia bisa benar-benar melayani, database ikut menjawab. Yang menanyakannya
  adalah yang mengatur lalu lintas: kalau jawabannya tidak, alihkan dulu ke tempat lain.

Menggabungkannya jadi satu route membuat database yang lambat terbaca sebagai "server mati", dan yang
terjadi kemudian adalah restart beruntun yang tidak memperbaiki apa pun — sementara satu-satunya yang
sakit ada di mesin lain.

### 3.3 Database mana yang diperiksa

**Usulan: hanya `Default`.**

Dua belas koneksi lain adalah database legacy milik module. Kalau salah satunya mati, yang mati
adalah satu module, bukan API-nya — dan menjawab 503 untuk seluruh API karena "Legacy Web Database"
tidak menjawab berarti mematikan sebelas module yang sehat.

Kalau nanti ada koneksi lain yang memang tidak boleh mati, bentuk yang cocok adalah penanda di tempat
pendaftarannya, bukan daftar terpisah yang bisa ketinggalan:

```csharp
builder.AddExtraDbConn(nameof(MssqlNsmDb), MssqlNsmDb(), DatabaseProvider.MicrosoftSqlServer,
   requiredForHealth: true);
```

Belum diusulkan untuk dibangun sekarang — disebut di sini supaya kalau kebutuhannya muncul, yang
ditambah adalah parameter ini dan bukan mekanisme kedua.

Pemeriksaannya sendiri satu query paling murah yang membuktikan koneksi benar-benar terbuka (bukan
sekadar objek `DbContext` yang berhasil dibuat), dengan batas waktunya sendiri yang pendek — beberapa
detik — supaya probe tidak ikut menggantung saat databasenya menggantung.

### 3.4 Berapa yang boleh dilihat — satu kata

**Usulan: pemanggil tanpa identitas hanya mendapat satu kata dan satu status code.**

`Healthy` + 200, atau `Unhealthy` + 503. Tidak ada nama database, nama host, nama provider, versi,
uptime, maupun pesan error. Sebuah endpoint terbuka yang menyebutkan "SQL Server <server-dev> tidak
menjawab" adalah peta gratis untuk siapa pun yang mengetuk, dan ia justru paling terbuka persis saat
sedang paling rapuh.

Keterangan terperinci — koneksi mana yang gagal, berapa lama, sejak kapan — tetap ada gunanya untuk
yang baru men-deploy, tapi tempatnya action biasa yang menuntut hak administrator, bukan route ini.
Namanya mengikuti konvensi: `GetMeta_Health` di `ApiCoreServices`, di region `Meta's`. **Ini bagian
yang paling pantas ditunda** sampai ada yang benar-benar membutuhkannya; `/health` dan
`/health/ready` sudah menjawab empat pemakai di tabel bagian 1.

### 3.5 Ongkos

`/health` (3.2) tidak menyentuh apa pun, jadi ia murah walau dipanggil sesering apa pun.

`/health/ready` tidak. Ia membuka koneksi database, ia terbuka untuk siapa saja, dan ia di luar batas
jatah request — tiga sifat yang kalau bertemu menjadikannya jalur termurah untuk membanjiri database
tanpa perlu akun sama sekali.

**Usulan: hasilnya disimpan beberapa detik** (mis. 5), dan panggilan yang datang dalam rentang itu
dijawab dari simpanan tanpa menyentuh database lagi. Probe tiap 5 detik tidak kehilangan apa pun —
itu memang setajam yang ia minta — sementara seribu panggilan dalam satu detik tetap hanya jadi satu
query.

## 4. Bentuk kasarnya

Belum kode final, baru untuk memperlihatkan berapa besar pekerjaannya.

Di `EmApp.Run()`, sebelum `MapFallback`:

```csharp
app.MapGet("/health", () => Results.Text("Healthy"));
app.MapGet("/health/ready", ReportReadinessAsync);
```

Satu knob di `EmAppBuilder`, supaya route-nya bisa dimatikan atau dipindah tanpa menyunting engine
— pilihan namanya masih terbuka, dan `null` berarti tidak dipetakan sama sekali:

```csharp
public string? HealthEndpointPath { get; set; } = "/health";
```

Yang perlu ditulis: satu penyimpan hasil ber-umur pendek (3.5), satu pemeriksa koneksi `Default`
dengan batas waktunya sendiri (3.3), dan dua baris pemetaan di atas. Tidak ada tabel baru, tidak ada
model baru, tidak ada perubahan pada dispatcher.

**Yang belum diputuskan:** memakai `AddHealthChecks()` bawaan ASP.NET Core atau menulis sendiri.
Bawaannya sudah memberi format jawaban yang tepat (satu kata, 200/503) dan pemeriksa `DbContext` siap
pakai, tapi ia membawa serta konsep sendiri — `IHealthCheck`, tag, `HealthCheckOptions` — yang ikut
muncul di permukaan publik `Em.Api.Core` yang akan dipaketkan sebagai NuGet. Perlu dilihat dulu
seberapa banyak yang bocor ke sana sebelum memilih.

## 5. Yang tidak termasuk

- Dashboard, halaman status, grafik uptime.
- Metrics (Prometheus/OpenTelemetry). Pertanyaan yang dijawabnya berbeda — "seberapa sibuk", bukan
  "hidup atau mati" — dan jalurnya sendiri.
- Client yang memeriksa `/health` sebelum login (baris keempat tabel bagian 1). Berguna, dan
  `ApiConnection` sisi client tempatnya, tapi ia pekerjaan sisi UI di dua UI core sekaligus — layak
  jadi item tersendiri setelah route-nya ada.

## 6. Pertanyaan untuk user

1. Setuju `/health` berdiri di luar `/api/...`, dengan konsekuensi ia tidak ikut dibatasi jatah
   request (3.1)?
2. Setuju dipisah dua — `/health` tanpa database, `/health/ready` dengan database (3.2)?
3. Setuju hanya koneksi `Default` yang diperiksa (3.3)?
4. Setuju pemanggil tanpa identitas hanya mendapat satu kata, dan `GetMeta_Health` yang terperinci
   ditunda sampai ada yang meminta (3.4)?
5. Berapa detik hasil `/health/ready` disimpan — usulan 5 (3.5)?
