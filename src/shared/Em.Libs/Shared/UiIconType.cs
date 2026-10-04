namespace Em.Shared
{
   /// <summary>
   /// Daftar ikon yang bisa dipilih user untuk menandai sebuah baris data - role, user, modul,
   /// kategori, dan entitas lain memakai daftar yang sama supaya satu ikon berarti sama di mana pun
   /// dia muncul. Isinya berupa token abstrak, bukan nama ikon dari framework UI tertentu:
   /// yang tersimpan di data hanya makna ikonnya, sedangkan gambar mana yang dipakai untuk
   /// menampilkannya adalah urusan lapisan tampilan.
   /// <para>
   /// Daftar ini hanya boleh bertambah. Member yang sudah ada tidak boleh dihapus atau diganti
   /// namanya, karena namanyalah yang tersimpan di data - menghapus satu member membuat baris lama
   /// yang memakainya kehilangan ikonnya.
   /// </para>
   /// </summary>
   public enum UiIconType
   {
      /// <summary>Belum ditentukan - user belum memilih ikon apa pun. Sengaja bukan gambar, supaya
      /// baris yang belum dipilihkan ikon bisa jatuh ke ikon bawaan entitasnya.</summary>
      Unspecified = 0,

      #region Otoritas

      /// <summary>Pengawas, keamanan.</summary>
      Shield,

      /// <summary>Pemegang akses.</summary>
      Key,

      /// <summary>Pembatasan.</summary>
      Lock,

      /// <summary>Pimpinan tertinggi.</summary>
      Crown,

      /// <summary>Persetujuan, legal.</summary>
      Gavel,

      /// <summary>Approver.</summary>
      CheckCircle,

      #endregion

      #region Orang

      /// <summary>Perorangan.</summary>
      User,

      /// <summary>Tim, grup.</summary>
      Users,

      /// <summary>Manajer, eksekutif.</summary>
      UserTie,

      /// <summary>Kepegawaian.</summary>
      IdCard,

      /// <summary>Layanan pelanggan.</summary>
      Headset,

      /// <summary>Cabang, unit organisasi.</summary>
      Building,

      #endregion

      #region Operasi

      /// <summary>Penjualan, pembelian.</summary>
      Cart,

      /// <summary>Gudang, stok.</summary>
      Boxes,

      /// <summary>Pengiriman, logistik.</summary>
      Truck,

      /// <summary>Produksi.</summary>
      Factory,

      /// <summary>Teknik, perawatan.</summary>
      Wrench,

      /// <summary>QC, inspeksi.</summary>
      ClipboardCheck,

      #endregion

      #region Angka & sistem

      /// <summary>Analitik, target.</summary>
      ChartLine,

      /// <summary>Kas, keuangan.</summary>
      Coins,

      /// <summary>Tagihan, piutang.</summary>
      Invoice,

      /// <summary>Akuntansi.</summary>
      Calculator,

      /// <summary>IT, sistem.</summary>
      Database,

      /// <summary>Konfigurasi.</summary>
      Gear,

      #endregion
   }
}
