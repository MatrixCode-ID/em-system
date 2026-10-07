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

## Referensi

- [Panduan approval](../engine/engine-approval.md)
- [Registrasi panel WPF](../../src/shared/Em.Ui.Wpf.Core/Shared/EmAppBuilder.Approval.cs)
- [Kontrak host panel](../../src/shared/Em.Ui.Core/Ui.Core/shared/IApprovalPanelHost.cs)
- [Registry panel](../../src/shared/Em.Ui.Core/Ui.Core/shared/ApprovalPanelRegistry.cs)

