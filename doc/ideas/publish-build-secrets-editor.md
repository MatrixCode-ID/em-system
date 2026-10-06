# Editor Secrets di tab Build publish profile tidak bisa dipakai

- Tanggal: 2026-10-07
- Status: diskusi

## Latar

Temuan saat menambah help icon di tab Build (Mode Dockerfile) `PublishProfileDialog`. Satu entri Secrets
(`BuildSecret`) punya dua properti: `Id` (nama secret yang dibaca Dockerfile lewat
`RUN --mount=type=secret,id=<Id>`) dan `CredentialRef` (Id credential di tab Credentials yang secret-nya dikirim).
`Publisher` menyusunnya menjadi `docker build --secret id=<Id>,env=EM_PUBLISH_SECRET_n`.

## Masalah

- `Fields` di `PublishProfileDialog` melewati semua properti bernama `Id` (dimaksudkan untuk id internal), sehingga
  `BuildSecret.Id` tidak tampil dan tidak bisa diisi. Secret selalu dikirim dengan `id=` kosong.
- `CredentialRef` berupa TextBox bebas, padahal isinya harus `PublishCredential.Id` (GUID) yang juga disembunyikan di
  tab Credentials. Pengguna tidak punya cara mengetahui nilainya, jadi publish gagal dengan "Build credential missing."

## Usulan

- Tampilkan `BuildSecret.Id` sebagai "Secret id" (pengecualian khusus dari aturan lewati `Id`).
- Ganti `CredentialRef` dengan ComboBox berisi credential profile (tampilan: purpose · host · username), menyimpan Id-nya.
- Validasi saat Save: Secret id wajib diisi dan unik, credential harus ada.

## Pertanyaan terbuka

- Apakah credential untuk build secret perlu purpose sendiri (mis. `build-secret`) supaya tidak tercampur dengan
  credential push?
