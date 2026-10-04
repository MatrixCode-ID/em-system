# Engine stream dua arah (`[StreamAction]`)

- **Tanggal:** 2026-10-03
- **Status:** diskusi (belum ada keputusan final, belum ada plan, belum ada kode)
- **Plan turunan:** belum ada
- **Catatan terkait:** [chat-ai.md](chat-ai.md) (konsumen pertama engine ini: kecocokan dengan Em, render Markdown, keamanan render)

Catatan ini merangkum diskusi tentang kemampuan stream dua arah di server Em. Pemicunya adalah pertanyaan apakah Em bisa dipakai untuk antarmuka chat AI, tetapi engine yang dibahas bersifat umum: chat hanyalah konsumen pertamanya. Isinya gagasan, bukan perintah kerja; agent tidak boleh mengeksekusinya sebelum dijadikan plan di `plan/unexecuted/`.

## Tujuan

Server mendapat kemampuan stream **dua arah** dan **push dari server**, sehingga modul bisa:

- mengalirkan hasil panjang ke client tanpa menunggu selesai (chat AI, progres business task);
- menerima pesan dari client di tengah aliran yang sama (stop/cancel, input lanjutan);
- mendorong peristiwa tanpa diminta client (hub MY TASKS, notifikasi approval, status Container Manager yang sekarang dipoll).

## Kondisi backend saat ini (diperiksa 2026-10-03, hanya dibaca, belum dijalankan)

| Arah | Status | Lokasi |
|---|---|---|
| Client ke server (upload) | Ada: `[PostAction]` dengan parameter `Stream` menerima body mentah tanpa dibuffer. | `EmApp.cs` (`TryBindStreamArguments`) |
| Server ke client (download) | Ada: action `Task<Stream>` dikirim lewat `Results.Stream`; `ApiClient.GetStreamAsync` membacanya dengan `ResponseHeadersRead` tanpa batas waktu. | `EmApp.cs`, `ApiClient.cs` |
| POST dengan jawaban berupa stream | Server tampaknya sudah bisa (`Task<Stream>` tidak dilarang di `[PostAction]`), tetapi client belum punya methodnya dan belum pernah diuji. | `EmAppBuilder.EnsureAsyncAction` |
| Dua arah serentak / push | Tidak ada. Tidak ditemukan SignalR, WebSocket, gRPC, maupun listener Kestrel tambahan. | |

Hal teknis dari pembacaan kode yang berlaku untuk stream apa pun:

- `AbortToken` pada `ServicesBase` berasal dari CTS yang di-dispose begitu `ProcessAsync` selesai. Stream yang diisi sesudah action mengembalikan nilai harus memakai `HttpContext.RequestAborted`, bukan `AbortToken`.
- Content-Type download dipatok `application/octet-stream`; framing sendiri (mis. NDJSON) tetap bisa dipakai di atasnya.
- Error di tengah stream tidak bisa mengubah status HTTP (header 200 sudah terkirim); perlu frame error di dalam stream.
- Flush per tulisan di Kestrel diperkirakan langsung terkirim dan tidak ada response compression aktif di backend, tetapi **belum dibuktikan**. Reverse proxy di produksi bisa membuffer.

## Arah yang dipilih (kecenderungan, belum final)

1. **Lewat Kestrel, bukan socket/protokol terpisah.** Port baru atau TCP custom mengulang semua gerbang `EmApp` (rate limit, identitas token, claim, jejak request), menambah urusan TLS dan sertifikat, sering diblokir firewall, tidak bisa lewat reverse proxy HTTP biasa, dan tidak bisa diuji dengan `curl` atau skrip Python yang ada. Satu-satunya alasan kuat untuk socket terpisah adalah kendali penuh atas protokol; itu dinilai tidak sebanding.
2. **WebSocket bawaan ASP.NET Core** sebagai transport. Handshake-nya request HTTP biasa, jadi bisa melewati gerbang yang sama; sesudahnya `AcceptWebSocketAsync`. TLS, framing pesan, ping/pong, dan lewat reverse proxy sudah disediakan. Client memakai `ClientWebSocket` bawaan .NET (WPF dan MAUI Android). Karena `ClientWebSocket` bisa menyetel header, token yang sama dengan `ApiClient` dikirim di `Authorization`; mekanisme ticket yang diperlukan pada socket mentah tidak perlu.
3. **Envelope pesan tipis buatan sendiri**, bukan protokol byte. Satu koneksi per sesi client, beberapa stream logis di dalamnya, frame JSON sederhana, kira-kira `{ id, kind: open | data | cancel | close | error, action, payload }`.
4. **HTTP tetap untuk action biasa**: JSON, upload/download file, login. Engine stream melengkapi, bukan menggantikan.

Alternatif di atas Kestrel yang dibahas dan **tidak dipilih**:

- **HTTP/2 full-duplex:** secara teknis bisa, tetapi rapuh. Butuh TLS dan ALPN, tidak jalan di HTTP/1.1, dan proxy sering membuffer.
- **gRPC:** terlalu berat untuk kebutuhan ini dan menambah dependensi di MAUI.
- **SignalR:** tidak ditolak, tetapi belum dipilih; lihat pertanyaan terbuka nomor 1.
5. **Satu atribut `[StreamAction]`**, perilakunya ditentukan dari signature method.

## Rancangan `[StreamAction]`

Selaras dengan `[GetAction]`/`[PostAction]`: dibaca lewat `ReadActionMarker`, termasuk dari method di interface (kontrak bersama di project Models, seperti `ITestServices`).

- Properti: `Claim`; tanpa `RequestTimeout` (stream tidak dibatasi waktu total, seperti `[PostAction]`), mungkin `IdleTimeout`; mungkin `MaxPerUser`/`MaxPerConnection`.
- Bentuk method divalidasi **saat startup** seperti `ResolveStreamParameters`: bentuk yang salah menggagalkan startup, bukan request pertama. Paling banyak satu payload pembuka ditambah satu objek stream atau `CancellationToken`.
- `ActionDefinition` perlu penanda jenis (HTTP atau stream): action stream tidak bisa dipanggil lewat HTTP (405) dan sebaliknya.
- Aturan nama: method tanpa atribut diam-diam tidak terdaftar (lihat memori "nama action wajib beratribut"); startup check bisa memperingatkan method berawalan konvensi stream yang tidak beratribut. Konvensi awalan nama stream belum ditetapkan.

Tiga bentuk method (gambaran, bukan rancangan final):

```csharp
// 1. Server ke client: satu pertanyaan, jawaban mengalir
[StreamAction(Claim = "Chat Access")]
IAsyncEnumerable<ChatToken> StreamChat(ChatRequest request, CancellationToken ct);

// 2. Dua arah
[StreamAction]
Task StreamChatLive(ChatOpen open, IDuplexStream<ChatIn, ChatOut> stream);

// 3. Langganan / push: terbuka sampai client batal
[StreamAction]
Task StreamTaskUpdates(TaskFilter filter, IStreamWriter<TaskChanged> writer);
```

## Yang berbeda dari action HTTP

1. **Lifetime service.** Satu DI scope per stream yang dibuka, bukan per koneksi socket dan bukan per request upgrade; kalau tidak, `DbContext` ikut tertahan berjam-jam.
2. **Identitas dan pembatalan.** `ServicesBase` (`HttpContext`, `Request`, `AbortToken`) diisi per stream. `AbortToken` berasal dari CTS per stream yang tersambung ke koneksi socket dan ke frame `cancel`.
3. **Claim dan token berumur panjang.** Claim diperiksa saat stream dibuka. Token yang diperbarui di tengah koneksi memicu evaluasi ulang hak; stream yang haknya hilang ditutup.
4. **Error.** `ActionException` diterjemahkan menjadi frame `error` dengan kode setara status HTTP, lalu stream ditutup.
5. **Sisi client.** Interface yang sama di Models; implementasi di `ServiceUiBase` memanggil API stream di `ApiClient`, seperti `GetStreamAsync` untuk download.

## Hal yang harus diputuskan sejak awal

- **Token kedaluwarsa di koneksi panjang:** frame untuk memperbarui token, atau server menutup dengan kode khusus lalu client sambung ulang dengan token baru.
- **Lebih dari satu instance API:** registri koneksi ada di memori satu proses. Push lintas instance butuh backplane (DB atau Redis). Untuk satu instance aman.
- **Shutdown:** Kestrel menutup WebSocket dengan rapi saat server berhenti, dan client menganggapnya normal.
- **Backpressure:** loop kirim dan terima menghormati client yang lambat agar memori server tidak membengkak.
- **Sambung ulang di client:** terutama MAUI Android (aplikasi ditidurkan OS, perpindahan jaringan).

## Pertanyaan yang masih terbuka

1. **SignalR atau envelope sendiri di atas WebSocket?** Envelope sendiri sejalan dengan gaya Em (atribut, `ActionDefinition`, claim, trace) dan tanpa dependensi baru, tetapi reconnect, grup, dan registri ditulis sendiri. SignalR menghemat itu, tetapi memakai pipeline autentikasi dan hub sendiri (gerbang Em harus dijembatani), menambah paket di kedua sisi, dan memberi dua gaya deklarasi. Kecenderungan: envelope sendiri; belum diputuskan pengguna.
2. **Satu atau dua atribut.** Stream per-request (chat) dan langganan topik (push) dibedakan dari signature, atau dipisah menjadi `[StreamAction]` dan `[SubscribeAction]`. Kecenderungan: satu atribut dulu.
3. **Push dari mana saja.** Modul lain sering perlu memancarkan peristiwa tanpa sedang berada di dalam stream (mis. approval selesai). Itu berarti perlu layanan semacam `IStreamHub.Publish(topic, message)`. Bagian ini paling menentukan nilai engine untuk kasus non-chat.
4. **Bentuk API dua arah.** `IDuplexStream<TIn,TOut>` dengan `ReadAllAsync`/`WriteAsync`, atau `IAsyncEnumerable<TIn>` masuk dan `IAsyncEnumerable<TOut>` keluar. Yang kedua lebih idiomatis C#; yang pertama lebih mudah bila membaca dan menulis tidak berpasangan.
5. **Konvensi nama method stream** (awalan) dan apakah method stream perlu region sendiri di service.
6. **Nama method client untuk POST yang jawabannya berupa stream.** `PostStreamAsync` sudah dipakai untuk upload (mengirim stream, membaca jawaban JSON), jadi varian baru perlu nama lain atau overload dengan parameter berbeda (aturan memori: tambah overload, jangan rename; overload dengan tipe kembalian berbeda saja tidak bisa).

## Konsumen pertama: chat AI

Chat memakai semua kemampuan engine sekaligus (aliran panjang, dua arah, cancel), jadi cocok sebagai pembuktian fondasi. Alurnya: client membuka stream, mengirim frame prompt, server mengalirkan frame token dari API Anthropic, dan tombol stop mengirim frame `cancel` yang menghentikan panggilan ke Anthropic. Protokol bukan penghambat kinerja chat; token LLM datang puluhan per detik dan waktu tunggu modelnya jauh lebih besar daripada overhead framing.

Catatan lain dari diskusi chat:

- API key Anthropic hanya di server (`Em.Api` sebagai proxy); client WPF/MAUI tidak pernah memegangnya.
- Riwayat percakapan disimpan sebagai **Markdown mentah** di tabel `ta_`, tidak pernah HTML; render dilakukan saat tampil.
- Render: satu WebView2 (WPF) dengan template HTML bersama, dipakai ulang di WebView MAUI, atau blok kontrol native dari AST Markdig. Streaming dengan membuffer token (30-60 ms), parse ulang seluruh teks pesan, lalu DOM diff di sisi HTML. Pilihan renderer belum diputuskan.
- Keamanan render: `DisableHtml()`, link hanya `http`/`https`/`mailto`, gambar eksternal diblokir atau butuh konfirmasi (vektor kebocoran data lewat URL gambar), dan latar WebView2 diset ke warna tema (aturan kilatan putih di `claude.md`).
- Kuota dan biaya per user serta pemangkasan/peringkasan konteks panjang belum dibahas rinci.

## Verifikasi yang perlu dilakukan sebelum rancangan dikunci

- Uji kecil: action POST yang mengembalikan stream lambat (menulis sepotong tiap ±200 ms) dan client yang membacanya bertahap, untuk membuktikan flush per tulisan dan perilaku di balik proxy.
- Prototipe WebSocket yang melewati gerbang `EmApp` yang sama (rate limit, token, claim) untuk memastikan gerbang itu bisa dipakai ulang tanpa menyalin logikanya.
