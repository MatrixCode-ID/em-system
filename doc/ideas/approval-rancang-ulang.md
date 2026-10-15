# Rancang ulang engine approval: tiga jenis approval, engine tanpa konsep dokumen

- **Tanggal:** 2026-10-16
- **Status:** diskusi
- **Plan turunan:** belum ada
- **Catatan terkait:** [approval-display-modul.md](approval-display-modul.md),
  [approval-qr-verifikasi.md](approval-qr-verifikasi.md), [approval-user-lookup.md](approval-user-lookup.md),
  [get-parameter-per-field.md](get-parameter-per-field.md), [object-storage.md](object-storage.md);
  panduan engine yang berlaku sekarang: [doc/engine/engine-approval.md](../engine/engine-approval.md)

Isinya gagasan yang masih dibahas, bukan perintah kerja; agent tidak mengeksekusinya sebelum dijadikan plan di
`plan/unexecuted/`. Catatan ini hanya membahas engine generik. Kebutuhan dan pemakaian di repo produk turunan
dicatat di repo produk itu.

## Gagasan pengguna (2026-10-16)

Approval dibagi menjadi **tiga jenis**:

1. **Data Approval**: approval atas INSERT/UPDATE/DELETE data. Berbasis claim dan **mudah diimplementasikan**
   oleh modul.
2. **Document Approval**: approval atas **dokumen fisik** (docx, xlsx, pdf, dwg, dan sebagainya). Butuh flow
   dengan **step yang dinamis**. UI menyediakan **viewer** dokumen yang di-approve dan **comment**. Cocok untuk
   proyek engineering yang punya banyak dokumen untuk di-approve, dengan **progres dokumen**.
3. **System Approval**: approval yang dipakai aplikasi seperti ERP yang punya UI approval sendiri. UI bisa
   berbeda per entity, jadi **developer membangun UI** di samping flow dan step. UI harus memvalidasi apakah
   user yang login boleh approve atau view.

Prinsip: **engine tidak mengenal "dokumen"**. Engine saat ini dibangun di atas konsep dokumen (jenis dokumen,
kunci dokumen, versi dokumen), padahal yang tahu apa itu dokumen adalah **implementor**. Produk turunan yang
memakai engine yang tahu dokumen apa saja yang dimilikinya; engine tidak.

## Kondisi engine sekarang (dibaca dari kode 2026-10-16)

Ringkasan untuk pegangan diskusi. Rinciannya di [engine-approval.md](../engine/engine-approval.md) dan
`src/backend/Em.Api.Core/Api/Approval/` (sekitar 6.000 baris).

- Dua jenis (`ApprovalKind`): **Data** dan **Document**. Keduanya memakai tabel request, action `core.approval`,
  Approval Manager (WPF), dan hub MY TASKS yang sama.
- **Data approval** (`AddDataApproval<TServices>(docType, approveClaim, flow)`): usulan per entity berupa daftar
  kolom `string?` (old/new) yang disimpan di request. Ada satu keputusan oleh pemegang claim approve, lalu
  pembandingan tiga nilai (original/current/proposed) dengan konflik dan override. Pemegang claim approve
  langsung tersimpan sebagai request self-approved. Modul menyediakan handler `load`/`apply` per entity,
  termasuk kunci sementara untuk entity baru.
- **Document approval** (`AddDocumentApproval<TServices, TKey>(docType, flow)`): flow **dideklarasikan di kode
  saat startup**. Level berurutan, step dalam satu level paralel dan semuanya wajib selesai, satu claim per step
  (**nama step = nama claim**). Tersedia `signers:` (dibekukan saat submit), substitute *on behalf*, `strict`,
  `distinctFrom` (four-eyes), `when:`, `guard` + override claim, dan `input` per step. Dokumennya sudah ada di
  tabel modul; request hanya mengunci statusnya. PDF dasar dirender saat submit lalu diberi stempel lewat slot
  milimeter dan `PdfLayout`.
- Kunci entity berupa record `[KeyPart(n)]` dalam bentuk kanonis, sehingga tabel legacy dengan composite key
  bisa dipakai.
- Satu transaksi SQL lintas database tanpa MSDTC (`ApprovalTransaction`). Syaratnya satu server dan satu login.
- Akun sistem (admin bawaan, debugger) tidak bisa submit atau menandatangani, karena ada FK ke tabel user.

### Tempat konsep "dokumen" menempel di engine

- **Skema:** `020-approval.sql` membuat tabel jenis dokumen (`ta_Doc`: nama, deskripsi, singkatan `char(7)`).
  Kolom jenis dokumen pada request ber-FK ke tabel itu, jadi setiap jenis harus didaftarkan sebagai baris di
  database engine.
- **API:** `docType`, `docKey`, dan `docVersion` ada di `SubmitAsync`, `SubmitDataAsync`,
  `EnsureNotInApprovalAsync`, dan `GetMeta_ApprovalRequestsByDoc`.
- **Claim:** claim pembaca bernama `View {docType}` (`EmAppBuilder.ApprovalViewClaimName`).
- **Hub:** task dikelompokkan per jenis dokumen (`ApprovalHubTaskSource`).
- **Istilah:** `ApprovalKind.Document` berarti dokumen transaksi di tabel modul, bukan file fisik.

### Kelemahan lain yang tercatat

- Mengubah flow atau penanda tangan berarti deploy ulang. Mengganti label step berarti mengganti claim.
- Bentuk flow terbatas: belum ada any-of/kuorum, percabangan selain `when:`, eskalasi/batas waktu,
  delegasi/cuti, dan notifikasi.
- Kunci dokumen bersifat opt-in: modul harus memanggil `EnsureNotInApprovalAsync` sendiri. Aplikasi lain yang
  menulis langsung ke tabel tidak tertahan.
- Approval dan stempel PDF menyatu. Penentuan posisi stempel lewat anchor teks rapuh.
- Data approval berbasis string per kolom. Handler modul berat. Konflik dibandingkan lalu ditulis tanpa kunci
  baris (sudah disebut di "Known limitations").
- `GetMeta_ApprovalRequests(query)` dan `GetMeta_ApprovalRequestsByDoc` masih membawa JSON di query string,
  melanggar aturan GET per field.
- Hub hanya per jenis, belum per item. UI approval dan hub belum ada di MAUI. Layar cek kode verifikasi belum
  dibangun.

## Pemetaan tiga jenis terhadap kode sekarang (analisa agent, belum keputusan)

| Jenis (gagasan) | Objek | Flow | UI | Padanan sekarang |
| --- | --- | --- | --- | --- |
| Data Approval | Operasi INSERT/UPDATE/DELETE | Berbasis claim, sederhana | Generik dari engine | Data approval sekarang. Konsepnya dekat, implementasinya masih berat |
| Document Approval | File fisik | Step dinamis (runtime) | Disediakan engine: viewer, comment, progres | **Belum ada.** Bukan `ApprovalKind.Document` |
| System Approval | Entity aplikasi (transaksi ERP) | Flow + step | Dibangun developer per entity; engine memvalidasi hak | `ApprovalKind.Document` sekarang (flow di kode, input per step, stempel PDF) |

Bacaan agent: Document Approval versi gagasan menjadikan *file* sebagai objek utama. Itu bisa dibangun
**di atas** inti engine sebagai salah satu implementor (bersama object storage), bukan konsep yang tertanam di
inti. Inti engine cukup mengenal: subjek approval (jenis yang didaftarkan implementor + kunci opaque), flow,
step, penanda tangan, keputusan, komentar, timeline, dan task. Ketiga jenis menjadi pintu masuk ke inti yang
sama.

## Keputusan yang sudah jelas

- (2026-10-16) Ada tiga jenis approval: Data, Document, System, dengan arti seperti di "Gagasan pengguna".
- (2026-10-16) Engine tidak boleh memiliki konsep dokumen. Daftar dan arti dokumen adalah milik implementor.

## Pertanyaan terbuka

1. **Inti bersama.** Apakah ketiga jenis berbagi satu runtime flow (step, signer, keputusan, audit, comment,
   hub), atau masing-masing berdiri sendiri?
2. **Flow Data Approval.** Selalu satu keputusan berbasis claim, atau boleh multi-step?
3. **"Mudah diimplementasi" untuk Data Approval.** Untuk tabel berkolom standar, engine bisa generik lewat
   metadata ORM, sedangkan tabel tanpa kolom standar atau dengan composite key tetap butuh handler dari modul.
   Apakah dua jalur ini diterima? Apakah nilai tetap string per kolom?
4. **"Dinamis" di Document Approval.** Siapa yang menyusun step: admin lewat template per proyek atau kategori,
   atau pengaju per dokumen? Apakah step bisa diubah saat request berjalan?
5. **Progres dokumen.** Apakah berupa revisi beserta status/kode review, persentase step, atau keduanya?
   Bagaimana hubungan revisi file dengan request approval (satu request per revisi)?
6. **Viewer.** PDF mudah. docx/xlsx biasanya dikonversi ke PDF. dwg butuh converter atau viewer khusus yang
   mungkin berlisensi, sehingga harus mengikuti aturan lib berlisensi. Format mana yang wajib di tahap awal?
   Apakah comment bisa berupa markup di halaman, atau teks saja?
7. **System Approval.** Flow tetap dideklarasikan di kode oleh developer, atau juga bisa dikonfigurasi saat
   runtime? Berapa banyak UI yang disediakan engine (komponen keputusan, timeline) dibanding UI developer?
8. **Stempel PDF.** Masih dibutuhkan? Kalau ya, apakah menjadi lapisan opsional dan dipakai oleh jenis yang
   mana?
9. **Istilah netral** pengganti `Doc` (`docType`/`docKey`/`docVersion`) di skema dan API. Tanyakan ke pengguna
   sebelum memilih.
10. **Kunci subjek.** Apakah engine tetap menyediakan penguncian subjek saat approval berjalan, atau sepenuhnya
    urusan implementor?
11. **Migrasi.** Apa yang dipertahankan dari tabel dan perilaku sekarang (reinstate `-2`/`-3`, verifikasi kode,
    FK ke user, transaksi lintas database), dan bagaimana data request yang sudah ada dipindahkan?
12. **Hal lama yang ikut dibahas.** Notifikasi, delegasi/cuti, eskalasi, any-of/kuorum, hub per item, UI MAUI,
    dan GET per field untuk action approval.
