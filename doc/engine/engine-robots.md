# Robot identities dan User Manager

User Manager WPF memiliki dua tab berikon: **Users** untuk akun manusia dan
**Robots** untuk identitas login otomatis. Container Manager mengelola container;
pendaftaran robot, token dan pemberian hak ada di User Manager.

`IRobotServices` di module `Administrative Tools` memakai claim **User Manager
Access**. Claim **Container Manager Access** tidak lagi mengizinkan pengelolaan
identitas/token/hak robot. Robot tidak mendapat sesi login manusia atau hak admin.

Tabel identitas: `ta_Robot`. Hak Container per root: `ta_CtnRootRobot`.
Kolom identitas memakai `cRobot*`; hak Container memakai `cCtnRootRobotAccess`.
Referensi upload dan pengirim manifest juga memakai `cRobotId`. Hash, token, expiry, status, riwayat push dan foreign key tetap berlaku.
Database yang sudah di-rename tidak perlu menjalankan rename lagi.

Instalasi baru tanpa registry: jalankan `doc/sqlscript/mssql/sets/Robot.sql`.
Dengan registry: `doc/sqlscript/mssql/sets/Ctn.sql` juga membuat `ta_Robot` jika
belum ada. Instalasi lama: gunakan
`doc/sqlscript/mssql/updates/20261003-RobotUserManager.sql` sebelum server baru
dinyalakan. Jangan menjalankan DDL instalasi baru dahulu pada database yang belum
di-rename karena akan membuat tabel identitas terpisah.

API baru: `GetMeta_Robots`, `GetMeta_RobotManagers`,
`PostGetMeta_RobotCreate`, `PostGetMeta_RobotRegenerate`,
`PostMeta_RobotUpdate`, `PostMeta_RobotDelete`, `PostMeta_RobotAccessSet`.
API robot lama di CtnService dihapus. Set access menerima robotId, managerId,
resourceId dan access; access kosong mencabut grant secara idempotent.
Token lengkap hanya dikembalikan saat create/regenerate. Regenerate langsung
membatalkan token lama. Format token tetap `emc_` agar token yang ada kompatibel.

## Owner account

Dropdown **Owner account** memiliki kotak **Search user account** untuk mencari sebagian nama akun tanpa membedakan huruf besar/kecil. **No owner** selalu tersedia; mengetik pencarian tidak mengganti owner sampai user memilih hasil.

Saat **New Robot**, pilihan **Owner account** menyediakan akun user aktif (termasuk admin biasa, kecuali akun sistem Admin/Debugger),
serta **No owner** sebagai default. Backend memvalidasi pilihan yang dikirim.
`PostGetMeta_RobotCreate` menerima parameter opsional keempat `ownerUserId`;
`GetMeta_RobotOwners` menyediakan daftar pilihan dengan claim **User Manager Access**.
`RobotInfo.OwnerUserId` dan `OwnerAccount` dikembalikan saat create, list, dan regenerate.
Detail robot menampilkan akun owner; edit/regenerate mempertahankan link yang ada.

Link disimpan di `ta_Robot.cRobotOwner_cUserId`, dengan FK ke `ta_User.cUserId`.
Penghapusan user mengosongkan link; robot dan grant-nya tetap ada. Link hanya metadata
kepemilikan, tidak memberikan sesi user, pewarisan claim, atau hak pengelolaan kepada owner.
Robot tetap memakai token dan grant manager sendiri.

Database lama wajib menjalankan `doc/sqlscript/mssql/updates/20261003-RobotOwner.sql`
sebelum backend baru dinyalakan. Skrip idempotent dan mempertahankan robot yang sudah ada
sebagai tanpa owner. DDL instalasi `Robot.sql`/`Ctn.sql` memerlukan tabel `ta_User` terlebih dahulu.
Uji lokal sekaligus migrasi: `dotnet run --project ..\.artefacts\em-system\scripts\robot-smoke -- --migrate-owner`.

## Menambah manager

Implementasikan `IRobotAccessManager` di backend dan daftarkan sebagai scoped:

```csharp
builder.Services.AddScoped<IRobotAccessManager, MyRobotAccessManager>();
```

`Id` harus unik dan stabil. `DescribeAsync` memberi nama/deskripsi manager,
resource dan opsi hak (kode/label); `ReadAsync` memberi grant yang ada;
`SetAsync` memvalidasi resource dan kode hak, lalu menulis/mencabut grant.
Kode kosong dicadangkan untuk **No access**. Manager bebas menyediakan jenis
hak selain Read/Write. Satu robot dapat memperoleh grant dari beberapa manager
dan beberapa resource sekaligus; tidak ada kolom manager tunggal pada robot.

`PrepareDeleteAsync` membersihkan grant/referensi dalam transaksi RobotContext
yang diberikan, dan tidak melakukan commit sendiri. `AfterDeleteAsync` untuk
pembersihan berkas setelah commit. Provider yang tidak didaftarkan tidak tampil
di UI; foreign key yang masih merujuk robot akan menolak delete sehingga data
tidak terhapus sebagian. Daftarkan provider selama tabel grant-nya masih digunakan.

Manager baru memakai `RobotAuth.AuthenticateAsync` untuk memvalidasi Basic
credentials (nama + token), lalu **wajib memeriksa grant miliknya sendiri** di
setiap operasi. Validasi identitas saja tidak memberikan hak akses.
Container menyediakan provider `Container`, resource root, Read `R` (pull)
dan Write `W` (push + pull); runtime `/v2` tetap memeriksa grant per root.

UserManager merender opsi dari backend tanpa perubahan XAML untuk manager baru.
Pendaftaran robot tetap tersedia bila registry tidak dinyalakan.
