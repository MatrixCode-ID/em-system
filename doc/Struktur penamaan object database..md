Struktur penamaan object database. 



1. Semua table dimulai dari awalan ta\_ 
2. Penamaan tidak ada spasi. 
3. Untuk nama kolumn, harus diawali c dan nama table, baru diikuti nama dari kolumn tersebut. Contoh: cProductId merupakan kolumn Id dari table ta\_Product. 
4. Untuk tiap table harus ada column dengan nama dengan urutan Id as char(26), Name as varchar(255), RefNumber as datetima, Stage as int, Order as int, Revision as int, Date as date, Note as varchar(500), ustamp (Update Stamp), datestamp (Create Stamp), json\_objet (String store untuk data json jika dibutuhkan).
5. Penjelasan standar column

   * Id adalah ULID
   * Name digunakan jika isi data ada nama, dikosongkan jika tidak diperlukan
   * RefNumber adalah tampilan dari nomor data. Disesuaikan oleh aplikasi untuk user
   * Stage merupakan state dari data yang distandarkan -1=void, 0=draft, >0={nilai positif yang bisa diproses. Standardnya 1 adalah publish. bisa saja jadi 1 adalah submit, 2 approve, 3 close, dll}

     * Staging lebih kecil dari -1 bisa dipakai untuk negatif seperti dokumen di hapus.
     * Staging di atur dari aplikasi (Business Logic) jadi tidak ada master table.
   * Order merupakan urutan yang di set aplikasi jika dibutuhkan. Default -1.
   * Revision merupakan revisi data yang di set aplikasi jika diperlukan. Default 1.
   * Date adalah tanggal dari data. Bisa di input user
   * Note adalah keterangan dari data. Default null.
   * Jika dikemudian hari ada tambahan column, data akan di serialize ke json dan disimpan dalam column json\_object
6. Foreign Key menggunakan nama key parent nya. 
7. Jika menggunakan nama non-standard Foreign Key, harus menyertakan nama kolumn aslinya. Contoh cProductTranEmpRef\_cEmpId adalah kolumn EmpRef pada table cProductTran dan reference ke kolumn cEmpId. 
8. Penamaan table child harus inherit parent. contoh ta\_ProductTranData adalah keturunan dari table ta\_Product 
9. Untuk penamaan dimulai dari awalan vi\_
10. Untuk setiap tabel yang ada interaksi dengan ui harus ada vi\_ dengan penamaan yang sama dengan tabelnya
11. vi\_ tidak boleh kalkulasi berat
12. Di UI, vi\_ akan ada static Class Build yang dipakai untuk konversi dari ta\_ ke vi\_ dan sebaliknya
13. Isi data json\_object bisa di masukan ke vi\_
14. Untuk object lain seperti Fungsi scalar menggunakan awalan fs\_, Fungsi tabel dengan awalan ft\_, store procedure dengan awalan sp\_
15. vi\_ di prioritaskan ada tabel utamanya. Minimalis pembuatan vi\_ tanpa tabel. Jika butuh kalkulasi, gunakan sp\_ atau fs\_
16. Untuk fs\_, ft\_, sp\_ diusahakan terikat dengan prinsip tabelnya.
17. Berikan tugas fungsi/procedure pada namanya setelah nama tabel. contoh: fs\_Customer\_GetAllInRegion.

