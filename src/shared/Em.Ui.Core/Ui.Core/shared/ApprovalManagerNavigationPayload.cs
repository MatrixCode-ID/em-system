namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Parameter untuk membuka layar approval: mode mana yang ditampilkan, dan disaring ke apa.
   /// </summary>
   /// <remarks>
   /// Layar yang sama dipakai tiga cara - dibuka dari menu alat tanpa saringan, dibuka dari daftar
   /// pekerjaan sudah tersaring ke satu jenis dokumen, dan ditanam di layar sebuah dokumen dengan
   /// saringan ke dokumen itu saja - jadi yang membedakannya hanya parameter ini.
   /// </remarks>
   public class ApprovalManagerNavigationPayload : NavigationPayloadBase
   {
      /// <summary>
      /// Nama navigasi layar approval bawaan aplikasi. Dibuka dengan parameter ini; membukanya tanpa
      /// parameter sama dengan membuka mode "perlu tindakan saya" tanpa saringan.
      /// </summary>
      public const string NavigationName = "em.approval.manager";

      /// <summary>Membuat parameter layar approval.</summary>
      public ApprovalManagerNavigationPayload() : base(null) { }

      /// <summary>
      /// <c>true</c> untuk hanya menampilkan request yang menunggu tindakan user aktif, <c>false</c>
      /// untuk seluruh request yang boleh ia lihat.
      /// </summary>
      public bool WaitingForMeOnly { get; set; } = true;
      /// <summary>Membuka daftar request yang dapat ditandatangani sebagai pengganti.</summary>
      public bool CanSignAsSubstituteOnly { get; set; }
      /// <summary>Pencarian yang dipertahankan ketika dokumen dibuka dalam tab sendiri.</summary>
      public string? Search { get; set; }
      /// <summary>Tahap yang dipertahankan ketika dokumen dibuka dalam tab sendiri.</summary>
      public Em.Api.Core.Models.ApprovalStage? Stage { get; set; }
      /// <summary>Kolom urutan daftar asal.</summary>
      public string? SortBy { get; set; }
      /// <summary>Arah urutan daftar asal.</summary>
      public bool SortDescending { get; set; }
      /// <summary>Halaman daftar asal.</summary>
      public int Page { get; set; } = 1;
      /// <summary>Ukuran halaman daftar asal.</summary>
      public int PageSize { get; set; } = 50;

      /// <summary>Jenis dokumen yang ditampilkan, atau kosong untuk semua jenis.</summary>
      public string? DocType { get; set; }

      /// <summary>
      /// Satu dokumen tertentu yang request-nya ditampilkan, dalam bentuk kunci bakunya. Dipakai saat
      /// layarnya ditanam di layar dokumen. Butuh <see cref="DocType"/> ikut disebut.
      /// </summary>
      public string? DocKey { get; set; }

      /// <summary>
      /// Versi dokumen yang request-nya ditampilkan, atau kosong untuk semua versi dokumen itu.
      /// </summary>
      public string? DocVersion { get; set; }

      /// <summary>
      /// Request yang langsung dibuka begitu layarnya tampil, atau kosong untuk membuka daftarnya saja.
      /// Dipakai saat layarnya dibuka dari pemberitahuan atau dari tautan ke satu request.
      /// </summary>
      public string? ApprovalRequestId { get; set; }

      /// <summary>
      /// Tampilan ringkas untuk layar approval yang ditanam di ruang sempit, mis. flyout di layar
      /// sebuah modul: satu kolom berisi usulan perubahan dan keputusan. Daftar request hanya muncul
      /// sebagai pemilih kalau ada lebih dari satu, tautan ke layar lain disembunyikan, dan riwayat
      /// tertutup sampai dibuka.
      /// </summary>
      public bool Compact { get; set; }

      /// <summary>
      /// Judul entri yang dibuka dengan parameter ini. Jenis dokumennya ikut disebut supaya daftar yang
      /// tersaring ke dua jenis dokumen berbeda menjadi dua entri, bukan satu entri yang isinya
      /// berganti-ganti.
      /// </summary>
      public override string? Title => DocType is null ? null : $"Approval - {DocType}";
   }
}
