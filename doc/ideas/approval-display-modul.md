# Tampilan approval yang disediakan modul

- **Tanggal:** 2026-10-07
- **Status:** diskusi
- **Plan turunan:** belum ada

Catatan gagasan, bukan perintah implementasi. Pembahasan di repo ini hanya mencakup fasilitas engine generik; tampilan dan aturan bisnis modul dibuat di repo pemilik modul.

## Gagasan pengguna

Modul menyediakan tampilan XAML untuk membaca dokumen yang sedang dimintakan approval. Isinya dapat berupa TextBlock readonly, rincian dokumen, dan informasi sampai mana approval berjalan. Card tambahan ditampilkan sesuai hak pengguna yang login, misalnya pengguna dengan peran perencanaan produksi.

## Fondasi yang sudah ada

- Approval Manager menyediakan PDF, langkah approval, riwayat, dan tindakan keputusan melalui engine yang sama.
- `AddApprovalInfoPanel<TView>` memasang card milik modul berdasarkan jenis dokumen, langkah terpilih, claim opsional, dan urutan.
- `AddApprovalStepPanel<TView, TViewModel>` memasang panel isian untuk satu langkah; payload dikirim bersama keputusan.
- `IApprovalPanel` menerima `IApprovalPanelHost` dan mempunyai `LoadAsync`. Host menyediakan ringkasan request, nama langkah terpilih, payload, validitas isian, dan refresh.
- `AddApprovalDocumentOpener` menghubungkan Approval Manager dengan layar sumber dokumen.
- Contoh card dan panel langkah tersedia di modul uji Em.Test.
- Filter claim card sudah dilakukan di UI. Pembatasan data card tetap harus diterapkan oleh API pemilik datanya.
- Belum ada registrasi khusus untuk menjadikan view dokumen modul sebagai tampilan utama pengganti area PDF. Mode compact juga menyembunyikan card informasi saat ini.
- Host panel belum menyediakan koleksi rincian langkah dan timeline; data tersebut tersedia pada Approval Manager melalui detail request.

## Keputusan yang sudah jelas

- Pengguna mengusulkan tampilan approval berbasis XAML yang disediakan modul dan card sesuai hak pengguna.
- Konfirmasi 2026-10-07: XAML menjadi tampilan utama. Tombol **Document preview** tersedia bila dokumen preview ada.
- Dokumen dirender sesuai state approval. Tampilan utama dan preview harus mengacu pada request serta versi dokumen yang sama.
- Konfirmasi 2026-10-07: engine menyediakan kontrak interface untuk tampilan approval; developer modul membuat implementasinya sesuai kebutuhan management dan mendaftarkannya saat builder.
- Kebutuhan 2026-10-07: desain harus memperhitungkan client mobile yang akan tersedia. Pengguna menanyakan kebutuhan abstract class; pilihan bentuk base class belum diputuskan.
- Acuan visual 2026-10-07: pengguna menyediakan [mockup Approval Editor](../diagram/Approval%20Edito%20Propose%20Layout.png). Berkas sumbernya [draw.io](../diagram/Approval%20Edito%20Propose%20Layout.drawio).
- Konfirmasi 2026-10-07: **User Chat Area** adalah komentar pada request approval. Pengguna yang mempunyai akses membuka request itu dapat memberikan komentar, tanpa harus menjadi penanda tangan.
- Konfirmasi 2026-10-07: referensi attachment di komentar dapat diklik untuk membuka attachment window dengan file tersebut terpilih. Jika preview tersedia, tampilkan preview; jika tidak, sediakan Download.
- Kebutuhan 2026-10-07: approval mempunyai attachment. Pengguna meminta usulan tampilannya langsung di mockup draw.io; tata letak attachment belum menjadi keputusan final.
- Belum ada keputusan untuk mengubah kode, membuat plan, atau menghapus kebutuhan PDF.

## Usulan desain untuk dibahas

- Modul memiliki view readonly untuk isi dokumen. Engine memiliki bagian bersama untuk progres, penanda tangan, riwayat, guard, dan tindakan approval.
- Jika dibutuhkan sebagai tampilan utama, tambahkan titik registrasi view dokumen pada engine UI; nama dan kontrak final belum diputuskan. Gunakan kembali registry dan siklus pemuatan panel yang sudah ada bila sesuai.
- Card khusus pengguna memakai claim yang diberikan melalui role, bukan pemeriksaan string nama jabatan. API memeriksa akses data dan server approval tetap menentukan apakah pengguna boleh memutuskan langkah.
- Bedakan filter card berdasarkan hak pengguna dengan filter berdasarkan langkah yang sedang dipilih. Memilih langkah lain tidak boleh memberikan hak baru kepada pengguna.
- Status approval dibaca dari engine. Jika panel modul perlu menampilkan progres sendiri, pertimbangkan context readonly yang menyediakan rincian langkah sehingga modul tidak perlu menghitung ulang status.
- Isi transaksi yang ditinjau harus sesuai versi request. Pilihan antara snapshot dan pembacaan dokumen terkunci dengan pemeriksaan versi perlu diputuskan. Informasi pendamping yang berubah, seperti ketersediaan sumber daya, diberi penanda sebagai kondisi terkini.
- PDF dapat tetap tersedia sebagai tampilan alternatif dan hasil bertanda tangan. Menampilkan XAML tidak otomatis menghilangkan kebutuhan PDF pada alur document approval yang sekarang.
- Card dinamis mengikuti aturan refresh mandiri, loading, retry, dan tema dari `claude.md`.

## Pertanyaan terbuka

1. Terjawab 2026-10-07: XAML menjadi tampilan utama; dokumen dapat dibuka melalui tombol **Document preview** bila tersedia.
2. Apakah PDF tetap wajib sebagai arsip bertanda tangan, atau perlu alur document approval tanpa PDF? Pilihan kedua memerlukan pembahasan engine backend tersendiri.
3. Apakah panel harus membaca snapshot yang disimpan ketika submit, atau dokumen terkunci yang versinya diperiksa saat dibaca?
4. Card per hak pengguna tetap terlihat sepanjang request, atau juga dibatasi menurut langkah? Bagaimana penempatannya dalam mode compact?
5. Apakah progres cukup ditampilkan oleh engine, atau view modul juga perlu menerima rincian langkah melalui host panel?

## Pembaruan diskusi 2026-10-07 — preview sesuai state

- Saat layar dibuka, tampilkan view XAML modul dan state request dari engine. PDF dibuka melalui **Document preview**, bukan dimuat sebagai area utama secara otomatis.
- Bila preview tidak tersedia, tombol tidak ditampilkan. Ketersediaan preview tidak menentukan hak untuk mengambil keputusan approval.
- Saat preview diminta, render dokumen untuk versi request yang sedang dibuka, dengan tanda tangan dan status yang sudah tercatat pada request itu. Request selesai atau request lama tetap menampilkan versinya sendiri.
- Ketika state berubah sesudah keputusan atau refresh, tampilan utama ikut diperbarui. Preview yang dibuka ulang mengikuti state terbaru request tersebut. Cara memperbarui preview yang masih terbuka belum diputuskan.
- Pending menampilkan langkah aktif dan progres; selesai menampilkan hasil akhir; penolakan atau penarikan menampilkan status beserta informasi yang tersedia di riwayat. Tombol keputusan mengikuti hak pengguna, langkah, dan guard dari engine.
- Rincian visual preview untuk tiap state, misalnya label atau watermark penolakan/penarikan, masih berupa desain terbuka; belum menjadi keputusan pengguna.

## Pembaruan diskusi 2026-10-07 — interface dan registrasi builder

### Arah yang ditetapkan pengguna

- Engine menyediakan interface sebagai titik ekstensi tampilan approval.
- Developer modul membuat implementasi dan view XAML, termasuk rincian, card, indikator, atau informasi pendukung sesuai kebutuhan management.
- Implementasi didaftarkan ketika aplikasi dibangun melalui builder. Engine memilih implementasi terdaftar untuk request yang dibuka.
- Engine tetap memiliki state approval dan aturan keputusan; modul memiliki penyajian serta pengambilan data bisnisnya.

### Bentuk kontrak yang diusulkan, belum API final

- Kontrak tampilan menyediakan cara menerima context approval dan memuat atau memperbarui isi secara async. Pertimbangkan perluasan `IApprovalPanel`/`IApprovalPanelHost` yang sudah ada agar tidak membangun siklus panel kedua tanpa kebutuhan.
- Context menyajikan request id, jenis/key/versi dokumen, state, rincian langkah, langkah terpilih, dan kemampuan pengguna yang ditentukan engine. State diberikan untuk dibaca; perubahan approval tetap melalui action engine.
- Host menyediakan fasilitas refresh dan pembukaan preview jika tersedia. Penyusunan preview menggunakan jalur rendering dokumen yang sudah ada; kontrak UI tidak melakukan stamping sendiri.
- Registrasi mengaitkan jenis dokumen dengan tipe view dan implementasi/view model. Engine membuat instance melalui DI ketika layar dibuka, bukan membagikan satu instance tampilan ke seluruh request.
- Tampilan utama dapat berisi banyak bagian sesuai kebutuhan modul. Card tambahan dan panel input langkah tetap dapat memakai registrasi yang sudah ada, termasuk claim, langkah, dan urutannya.
- Pengambilan data modul tetap melalui service milik modul. Payload keputusan tetap diperiksa backend; validitas isian di UI tidak menggantikan validasi server.
- Siklus pemuatan awal, refresh setelah state berubah, pembatalan saat berpindah request, serta pelepasan resource saat ditutup perlu ditetapkan dalam kontrak agar implementasi modul mempunyai perilaku yang konsisten.

### Detail kontrak yang masih terbuka

- Memperluas interface panel yang ada atau menambah interface khusus tampilan utama.
- Nama interface dan method builder, tipe context, serta bentuk generik registrasi.
- Pembagian progres antara komponen bersama engine dan binding yang dapat digunakan view modul.
- Aturan fallback untuk jenis dokumen yang belum mendaftarkan tampilan utama, serta penempatan view dalam mode compact.

## Pembaruan diskusi 2026-10-07 — abstract class dan mobile

### Kondisi kode sekarang

- Interface panel dan registry berada di `Em.Ui.Core`, project bersama tanpa dependency WPF atau MAUI.
- WPF dan MAUI mempunyai `MvvmModelBase` masing-masing. Base WPF membawa Window, navigasi, dan command WPF; base MAUI membawa Page, navigasi, dan command MAUI.
- Registrasi panel dan layar Approval Manager saat ini tersedia di WPF. Keberadaan kontrak bersama belum berarti layar approval mobile sudah tersedia.

### Rekomendasi, belum keputusan pengguna

- Interface menjadi kontrak wajib yang diterima builder. Abstract class boleh disediakan sebagai kemudahan, tetapi implementasi modul tidak wajib mewarisinya.
- Kontrak context dan lifecycle approval diletakkan di `Em.Ui.Core` tanpa tipe kontrol, window, page, dispatcher, atau dependency framework UI.
- Perilaku bersama seperti pengelolaan sesi pemuatan, busy/error, cancellation, dan pergantian context dapat dikemas sebagai helper/session bersama yang digunakan melalui composition. Ini menghindari keharusan mengganti hierarki base view model modul yang sudah ada.
- Jika base class diperlukan untuk mengurangi pengulangan, sediakan base view model opsional per platform yang mengikuti `MvvmModelBase` platform tersebut dan menggunakan helper bersama. Jangan memaksakan base WPF untuk mobile, atau membuat base yang hanya meneruskan seluruh operasi tanpa manfaat.
- Modul dapat berbagi model presentasi dan service yang tidak bergantung platform. View XAML WPF dan MAUI dibuat terpisah karena kontrol dan layoutnya berbeda; abstract class tidak membuat XAML keduanya otomatis kompatibel.
- Builder WPF mendaftarkan view WPF dan implementasi kontraknya; builder MAUI mendaftarkan view MAUI dan implementasi kontrak yang sama. Policy pemilihan jenis dokumen, state, dan claim mengikuti aturan bersama.
- Preview dirender melalui backend yang sama untuk request dan versi yang sama. Host masing-masing platform menentukan cara membuka preview serta menyediakan aksi refresh dan navigasi melalui interface.

### Pertanyaan terbuka tambahan

- Apakah helper bersama sudah cukup, atau base class opsional perlu disediakan sejak implementasi pertama?
- Bagian view model mana yang dapat dipakai bersama setelah kebutuhan modul dan lifecycle disepakati?
- Apakah implementasi awal mencakup layar MAUI, atau kontraknya disiapkan sekarang dan layar mobile dibuat pada tahap berikutnya?

## Pembaruan diskusi 2026-10-07 — mockup layout Approval Editor

### Struktur yang terlihat pada mockup

- Toolbar horizontal di bagian atas, dengan contoh dua tombol tool.
- Area utama besar bertuliskan **Module Approval Control Rendered Here**.
- Panel kanan bertuliskan **User Chat Area**, membentang sampai bagian bawah area kerja.
- Deretan **Signee Card** di bawah control modul, pada kolom kiri.
- Contoh signee card di luar bingkai utama merupakan detail/mockup card: nama user, jabatan, status **Waiting for Action**, ikon approve, reject, dan **Message after action**.

### Pemetaan tanggung jawab yang diusulkan

- Engine menyediakan shell editor: toolbar, host control modul, area signee, serta panel komunikasi.
- Implementasi interface milik modul mengisi area utama melalui registrasi builder. Modul bebas menyusun isi sesuai kebutuhan management tanpa harus membangun ulang shell editor.
- **Document preview** dari keputusan sebelumnya ditempatkan pada toolbar jika tersedia. Makna tombol tool lain belum ditentukan oleh mockup.
- Signee card menampilkan identitas dan state dari langkah/request engine. Tombol approve/reject menjalankan jalur keputusan engine dan hanya aktif bagi pengguna yang berhak pada langkah yang sedang dapat diputuskan. Melihat card penanda tangan lain tidak memberikan hak bertindak sebagai orang itu.
- Hubungan card dengan langkah perlu dirumuskan: satu langkah dapat mempunyai beberapa calon penanda tangan dan satu level dapat mempunyai langkah paralel. Mockup belum menetapkan satu card mewakili user atau langkah.
- Panel komunikasi perlu terikat pada request yang sedang dibuka; pergantian request tidak boleh mencampur pesan antardokumen. Arti chat sudah dikonfirmasi sebagai komentar request; **Message after action** masih perlu dikonfirmasi.
- Pemisahan shell engine dan control modul berlaku juga untuk MAUI, dengan susunan layar yang disesuaikan ukuran mobile.

### Usulan adaptasi mobile, belum keputusan

- Gunakan area modul selebar layar, signee sebagai daftar/card yang mudah dijangkau, serta chat melalui tab atau panel terpisah.
- Pertahankan context, kontrak implementasi, dan aturan keputusan bersama; view dan hosting tetap dibuat sesuai platform.

### Pertanyaan dari mockup

1. Terjawab 2026-10-07: **User Chat Area** adalah komentar pengguna yang boleh membuka request approval itu. Mekanisme pembaruan komentar belum diputuskan.
2. Apakah **Message after action** berarti catatan keputusan yang tampil setelah approve/reject, atau aksi untuk mengirim pesan lanjutan?
3. Apakah signee card mewakili setiap langkah approval atau setiap user penanda tangan?
4. Apakah toolbar hanya berisi aksi engine atau juga menyediakan slot aksi tambahan dari modul?

## Pembaruan diskusi 2026-10-07 — komentar request

- Panel kanan menampilkan komentar yang terikat pada request approval yang sedang dibuka.
- Hak memberikan komentar mengikuti hak membuka request, bukan hak approve/reject atau daftar penanda tangan. Hak keputusan approval tetap terpisah.
- Engine sudah menyediakan `PostMeta_ApprovalComment`, yang memeriksa akses membaca request melalui backend. Layanan ini memerlukan user nyata dan sudah menerima komentar sesudah request selesai atau ditolak.
- Rekomendasi: gunakan penyimpanan dan action komentar engine yang ada, dengan tampilan daftar pesan berisi nama penulis, waktu, dan isi komentar serta input untuk menulis komentar.
- Komentar bebas tidak mengubah state approval. Catatan yang dikirim bersama approve/reject tetap mempunyai hubungan dengan keputusan di riwayat, walaupun desain tampilannya nanti dapat berkaitan dengan panel komentar.
- Mekanisme memuat ulang komentar pengguna lain, tampilan catatan keputusan di panel, dan arti ikon **Message after action** masih terbuka. Belum ada keputusan untuk menambah transport chat realtime.

## Pembaruan diskusi 2026-10-07 — usulan attachment pada mockup

- [Berkas draw.io](../diagram/Approval%20Edito%20Propose%20Layout.drawio) kini memuat dua halaman usulan tambahan: **Attachments proposal** dan **Attachment preview**. Halaman awal **Page-1** tetap dipertahankan. PNG awal masih menggambarkan halaman awal.
- Usulan utama: bagian attachment berada di atas komentar pada panel kanan. Panel diperlebar agar nama file, metadata, dan aksi mudah dibaca, sementara area control modul dan signee tetap tersedia.
- Header menampilkan jumlah attachment, refresh, serta tombol collapse. Toolbar mempunyai **Attachments (3)** untuk mengakses bagian tersebut dan **Document preview** untuk dokumen utama. Preview dokumen utama dan preview attachment adalah dua aksi terpisah.
- Setiap file menampilkan badge tipe, nama, ukuran, penulis/uploader, tanggal, dan aksi. PDF/gambar memakai **Preview** dan **Download**; tipe tanpa preview memakai **Open** atau **Download**.
- **Attachment preview** memperlihatkan jendela terpisah dengan daftar file di kiri, viewer besar di kanan, metadata, Download, paging PDF, zoom, dan Close. Menutup preview mengembalikan pengguna ke editor tanpa kehilangan state review.
- File asli attachment ditampilkan sebagaimana tersedia; aturan render/stamp dokumen approval utama tidak otomatis diterapkan pada file pendamping.
- File yang tidak dapat dipreview menampilkan fallback yang jelas, bukan viewer kosong. Cara Open mengikuti kemampuan client masing-masing.
- Daftar attachment dan komentar mempunyai scroll masing-masing; collapse memberi ruang tambahan untuk komentar. Untuk mobile, usulannya daftar attachment dibuka melalui sheet dengan kontrak data yang sama.
- Mockup memakai data contoh dan hanya mengusulkan tampilan untuk membaca attachment. Tidak ada keputusan menambahkan aksi upload, ganti, atau hapus pada request berjalan.

### Detail attachment yang masih terbuka

- Attachment terikat pada versi request/snapshot atau mengikuti file dokumen terkini; aturan ini perlu ditentukan agar bukti review tetap jelas.
- Sumber metadata/file attachment yang disediakan modul melalui kontrak engine dan aturan akses backend, termasuk bila sebuah file memiliki pembatasan tambahan.
- Hak serta waktu yang diizinkan untuk upload, mengganti, atau menghapus attachment.
- Banyak file: batas tinggi bagian, pencarian, paging, dan urutan tampil. Mockup pertama memakai tiga file agar alur dasar mudah dinilai.
- Preview format apa saja yang memang tersedia di WPF dan MAUI, serta cara memperbarui daftar setelah file berubah.

### Verifikasi mockup

- Struktur XML dan keunikan id cell kedua halaman diperiksa; halaman awal dibandingkan dengan salinan sebelum perubahan dan tetap sama.
- Kedua halaman tambahan diekspor memakai CLI draw.io dan diperiksa secara visual. Hasil render lokal berada di `..\.artefacts\em-system\scripts\approval-attachments-render\`.
- Ini perubahan diagram dan catatan ide, bukan implementasi engine atau pengujian interaksi aplikasi.

## Pembaruan diskusi 2026-10-07 — WebView sebagai comments viewer

- Pengguna menanyakan beban WebView jika dipakai untuk viewer komentar. Belum ada keputusan mengganti viewer native dengan WebView.
- WebView2 pada Windows membawa runtime browser dengan beberapa proses dan biaya inisialisasi/memori tambahan. Besarnya bergantung pada konten, jumlah instance, runtime, dan perangkat; belum dilakukan pengukuran di aplikasi ini.
- Rekomendasi untuk kebutuhan saat ini (nama penulis, waktu, teks, dan link): gunakan daftar native dengan template komentar dan virtualisasi/paging yang sesuai. WPF dan MAUI dapat memakai kontrak/model komentar bersama dengan view native masing-masing.
- WebView layak dipertimbangkan jika kebutuhan berkembang menjadi rich HTML, Markdown yang kompleks, atau ingin memakai satu renderer HTML/CSS untuk desktop dan mobile. Berbagi renderer web tidak menghilangkan perbedaan host, theme, navigation, dan lifecycle tiap platform.
- Jika WebView dipilih, gunakan satu viewer untuk seluruh panel komentar, bukan satu per komentar. Perbarui isi saat pindah request tanpa membuat ulang viewer setiap kali refresh, muat komentar bertahap, dan inisialisasi saat panel diperlukan. WebView2 dapat menggunakan environment bersama dengan parameter yang sesuai; browser process dapat dibagi, tetapi tambahan viewer tetap mempunyai biaya.
- Pisahkan kontrak komentar dari renderer agar viewer native/WebView dapat dipilih tanpa mengubah API approval. Ini rekomendasi arsitektur, bukan instruksi implementasi baru.
- MAUI memakai engine WebView platform: Android WebView, WKWebView pada iOS/Mac Catalyst, dan WebView2 pada Windows. Pengukuran Windows tidak otomatis mewakili perangkat mobile.
- Sebelum keputusan final, bila WebView tetap dipertimbangkan, bandingkan waktu buka pertama, total memori aplikasi beserta proses browser, scrolling daftar panjang, dan beberapa tab approval. Tidak ada angka benchmark yang sudah dibuktikan dalam diskusi ini.

Sumber teknis: [WebView2 performance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance), [WebView2 process model](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/process-model), [MAUI WebView](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/webview?view=net-maui-10.0).

## Pembaruan diskusi 2026-10-07 — foto dan snip dalam komentar native

### Kebutuhan pengguna

- Pengguna mengusulkan penambahan foto dan snip pada komentar approval, baik di mobile maupun WPF, dan menanyakan apakah kontrol native dapat digunakan.
- Foto/snip merupakan lampiran pesan komentar. Relasinya ke komentar/request perlu jelas dan tidak otomatis dianggap attachment dokumen utama atau mengubah versi dokumen approval.

### Rekomendasi desain native, belum implementasi

- Viewer memakai daftar native dengan template pesan yang memuat penulis, waktu, teks, dan thumbnail gambar. Klik thumbnail membuka viewer gambar native dengan zoom.
- Composer mempunyai input multiline, tombol Photo/Attach, kemampuan Paste image di Windows, dan Send. Gambar terpilih tampil sebagai thumbnail draft yang dapat dibatalkan sebelum dikirim; gambar tidak langsung dipublikasikan saat dipilih atau ditangkap.
- Mobile MAUI memakai media picker sistem untuk memilih foto atau mengambil foto kamera. Screenshot dari luar aplikasi dapat dipilih dari galeri; crop dapat ditambahkan pada preview draft.
- Screenshot/crop di dalam aplikasi mobile dapat disediakan melalui capture view/aplikasi dan UI crop. API Screenshot MAUI menangkap layar aplikasi yang sedang tampil, bukan snip bebas terhadap aplikasi lain. Snip seluruh perangkat tidak dianggap tersedia dari satu API bersama.
- WPF memakai pemilih file dan paste gambar melalui clipboard. Alur awal snip: pengguna memakai alat snip Windows, lalu paste hasil ke composer. Tombol Snip langsung di aplikasi memerlukan integrasi Windows atau overlay capture tersendiri, bukan kemampuan bawaan TextBox/Image.
- Shared core memegang model komentar, metadata attachment, draft, dan kontrak layanan; implementasi pemilihan/capture gambar berada pada platform masing-masing dan didaftarkan melalui builder/DI.
- Untuk kinerja, tampilkan thumbnail yang di-decode pada ukuran tampilan, muat file besar saat preview dibuka, dan gunakan paging/virtualisasi daftar. Tambahan memori terutama mengikuti ukuran gambar yang di-decode dan jumlah thumbnail yang ditahan, sehingga tetap perlu diukur.

### Pengaruh pada engine yang ada

- `ta_ApprovalRequestComment` dan `PostMeta_ApprovalComment(requestId, note)` saat ini menangani komentar plain text; belum ada kontrak attachment komentar pada action itu. Penambahan gambar memerlukan perluasan API/model/storage, bukan hanya mengganti kontrol viewer.
- File disimpan melalui fasilitas binary storage engine yang sesuai; metadata komentar menyimpan referensi file, bukan gambar Base64 di string komentar.
- Backend memeriksa hak terhadap request saat upload, menghubungkan file ke komentar, dan membaca file. UI tidak memberikan akses file hanya karena thumbnail terlihat.
- Pengiriman teks dan lampiran perlu memastikan pesan tidak tampil sukses jika lampiran gagal. Aturan draft upload, retry tanpa pesan ganda, serta pembersihan file yang tidak jadi dikirim masih harus diputuskan.
- Tidak ada perubahan aturan approve/reject hanya karena komentar mengandung gambar. Komentar tetap dapat dibuat oleh pengguna yang mempunyai akses membuka request, mengikuti aturan identitas engine.

### Pertanyaan terbuka

- Boleh mengirim gambar tanpa teks, berapa gambar per komentar, dan batas ukuran/dimensi/format.
- Snip awal cukup melalui clipboard Windows atau harus mempunyai tombol capture langsung; pada mobile cukup pilih screenshot/crop atau perlu capture layar aplikasi.
- Crop/annotasi mana yang dibutuhkan; belum ada keputusan menyediakan editor gambar lengkap.
- Apakah daftar attachment panel kanan juga mengindeks gambar komentar atau hanya attachment dokumen. Sumber dan konteksnya harus tetap ditampilkan bila digabung.

Sumber teknis: [MAUI media picker](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/device-media/picker?view=net-maui-10.0), [MAUI screenshot](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/device-media/screenshot?view=net-maui-10.0), [WPF Clipboard.GetImage](https://learn.microsoft.com/en-us/dotnet/api/system.windows.clipboard.getimage?view=windowsdesktop-10.0).

## Pembaruan diskusi 2026-10-07 — referensi attachment dalam komentar

### Klarifikasi pengguna

- Pengguna meminta desain mention attachment dan menjelaskan bahwa yang dimaksud adalah **referensi attachment dalam komentar**.
- Sintaks `#nama-file` dan tampilan chip/link adalah usulan desain; kebutuhan yang dikonfirmasi adalah mengacu pada file lampiran dari pesan.

### Usulan yang digambar

- Halaman **Comment attachments** ditambahkan pada [mockup draw.io](../diagram/Approval%20Edito%20Propose%20Layout.drawio). Tiga halaman sebelumnya dipertahankan.
- Viewer menampilkan komentar, referensi file yang dapat diklik, serta thumbnail foto/snip. Composer memisahkan teks, thumbnail draft, daftar referensi terpilih, dan tombol Send.
- Mengetik `#` atau menekan **Reference** membuka picker native yang dapat dicari. Memilih file menyisipkan referensi ke file yang sudah ada; tidak mengunggah ulang file tersebut.
- Picker memberi penanda sumber **Document attachment** atau komentar penulis. Pilihan mengindeks gambar dari komentar masih usulan; belum keputusan cakupan final.
- Referensi yang sudah dipilih tampil sebagai chip/link dan membuka preview file tersebut. Nama file hanya label; data referensi menggunakan identitas file stabil yang terkait dengan request/versi yang benar.
- Foto atau snip baru masuk sebagai lampiran draft yang dapat dibuang sebelum Send. **Remove** pada referensi hanya menghapus referensinya dari draft, bukan menghapus file sumber.
- **Photo**, **Paste**, dan **Snip** pada detail mockup menunjukkan kemampuan Windows yang diusulkan. Mobile memakai kamera/galeri serta pemilihan screenshot atau capture layar aplikasi sesuai batasan platform. Tombol ditampilkan mengikuti kemampuan host; mockup ini belum implementasi kemampuan capture.
- Pada mobile picker dibuka sebagai sheet dan gambar dapat dibuka pada viewer layar penuh. Kontrak pesan/file dibagi bersama, sedangkan view dan layanan capture tetap per platform.

### Kontrak dan perilaku yang perlu dirumuskan

- Simpan teks, lampiran pesan, dan referensi file sebagai data terstruktur; jangan hanya menyimpan string nama file yang dapat berubah atau tidak unik.
- Backend memvalidasi file referensi berada dalam konteks request yang diizinkan dan dapat dibaca pengguna. Membuat referensi tidak menambah hak membaca file.
- Tentukan perilaku ketika file sumber sudah tidak tersedia atau akses berubah: tampilkan status tidak tersedia tanpa mengarah diam-diam ke file lain bernama sama.
- Tetapkan hubungan referensi dengan versi file, sinkronisasi token teks dan chip, pemilihan beberapa file, serta dukungan komentar berisi gambar tanpa teks.
- TextBox/Editor native ditambah picker dan chip di luar editor cukup untuk usulan awal. Token berwarna di dalam editor merupakan kebutuhan tersendiri jika nanti diinginkan; belum menjadi persyaratan.
- Fitur ini memerlukan perluasan API komentar plain text saat ini. Viewer native tidak menyediakan penyimpanan/upload/referensi attachment secara otomatis.

### Verifikasi diagram

- Halaman baru diekspor dengan CLI draw.io dan diperiksa visual. Hasil lokal: `..\.artefacts\em-system\scripts\approval-attachments-render\comment-attachments.png`.
- Seluruh halaman sebelumnya dibandingkan dengan kondisi sebelum penambahan dan tetap sama; struktur XML dan keunikan id halaman baru diperiksa.

## Pembaruan diskusi 2026-10-07 — klik referensi membuka attachment window

- Pengguna menetapkan alur klik mention attachment: masuk ke **attachment window**, kemudian melihat preview dokumen bila tersedia atau mengunduh dokumen bila preview tidak tersedia.
- Attachment window membuka dan memilih file berdasarkan identitas referensi, bukan mencari file lain hanya berdasarkan namanya. Alur ini digunakan baik dari komentar maupun dari daftar attachment.
- Kondisi **preview tersedia** menampilkan viewer dan tetap menyediakan Download. Kondisi **preview tidak tersedia** menampilkan nama, tipe, ukuran file, status **No preview available**, dan tombol **Download**.
- Download pada fallback dimulai ketika tombol Download ditekan; membuka referensi tidak langsung mengunduh file. Ini rincian interaksi yang dipakai pada mockup.
- Alur ini memperjelas usulan sebelumnya yang menyebut Open/Download untuk tipe tanpa preview: pada desain saat ini fallback attachment window adalah **Download**. Cara membuka file setelah unduhan mengikuti perangkat pengguna.
- Menutup attachment window kembali ke editor/komentar dengan posisi dan draft tetap terjaga. Host mobile memakai halaman atau modal yang setara.
- Mockup **Comment attachments**, **Attachments proposal**, dan **Attachment preview** disesuaikan. Halaman baru **Attachment download** menunjukkan kondisi tanpa preview; **Page-1** tetap dipertahankan.
- Catatan desain tetap berstatus diskusi; belum ada perubahan kode engine.

## Pembaruan diskusi 2026-10-07 — bold dan italic pada komentar

- Pengguna menanyakan dukungan bold dan italic. Kebutuhan format masih dibahas; belum menjadi implementasi engine.
- Usulan awal: composer mempunyai tombol **B** dan **I** untuk membungkus teks terpilih dengan Markdown terbatas: `**teks tebal**` dan `*teks miring*`. WPF dapat menyediakan Ctrl+B/Ctrl+I melalui handler composer; mobile memakai tombol yang sama.
- Viewer memetakan hasil parser bersama ke elemen teks native. Untuk WPF dapat memakai TextBlock dengan Run/Bold/Italic; MAUI memakai Label dengan FormattedString/Span dan FontAttributes. Format ini tidak mengharuskan WebView.
- Composer TextBox/Editor tetap menampilkan penanda Markdown saat menulis. Preview draft dapat disediakan untuk melihat hasil sebelum Send. Ini berbeda dari editor WYSIWYG yang langsung menampilkan setiap bagian teks sesuai formatnya.
- Jika pengguna menginginkan WYSIWYG, WPF mempunyai RichTextBox dengan command ToggleBold/ToggleItalic. Editor standar MAUI tidak menyediakan pengeditan rich text per bagian; diperlukan kontrol atau handler native tambahan pada platform mobile. Keputusan WYSIWYG masih terbuka.
- Penyimpanan tidak menggunakan FlowDocument/XAML WPF sebagai format bersama. Usulan Markdown terbatas disertai penanda format/versi agar komentar plain text lama tetap dirender literal; detail kontrak dan migrasi belum diputuskan.
- Referensi attachment tetap menyimpan identitas file terstruktur. Format bold/italic hanya memengaruhi teks dan tidak mengganti referensi file dengan pencarian berdasarkan nama. Klik referensi tetap membuka attachment window dengan preview atau Download.
- Pertanyaan terbuka: cukup composer dengan penanda Markdown dan preview, atau perlu WYSIWYG di kedua platform? Belum ada perubahan mockup maupun kode pada pembahasan ini.

Sumber teknis: [WPF RichTextBox](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/richtextbox), [MAUI Label dan formatted text](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/label?view=net-maui-10.0), [MAUI Editor](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/editor?view=net-maui-10.0).

## Referensi

- [Panduan approval](../engine/engine-approval.md)
- [Registrasi panel WPF](../../src/shared/Em.Ui.Wpf.Core/Shared/EmAppBuilder.Approval.cs)
- [Kontrak host panel](../../src/shared/Em.Ui.Core/Ui.Core/shared/IApprovalPanelHost.cs)
- [Registry panel](../../src/shared/Em.Ui.Core/Ui.Core/shared/ApprovalPanelRegistry.cs)

