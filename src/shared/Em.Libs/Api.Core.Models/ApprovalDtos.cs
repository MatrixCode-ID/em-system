namespace Em.Api.Core.Models
{
   /// <summary>
   /// Satu keputusan atas satu langkah. Dikirim dalam array supaya beberapa request bisa diputuskan
   /// sekaligus, yang penting karena hampir semua request pada akhirnya disetujui.
   /// </summary>
   public class ApprovalDecision
   {
      /// <summary>Request yang diputuskan.</summary>
      public string cApprovalRequestId { get; set; } = string.Empty;

      /// <summary>
      /// Langkah yang diputuskan. Wajib diisi, karena satu level bisa berisi beberapa langkah yang
      /// menunggu bersamaan.
      /// </summary>
      public string StepName { get; set; } = string.Empty;

      /// <summary><c>true</c> menyetujui, <c>false</c> menolak.</summary>
      public bool Approve { get; set; }

      /// <summary>
      /// Alasan keputusan. Boleh kosong saat menyetujui biasa; wajib saat menolak, saat menandatangani
      /// sebagai pengganti, dan saat menembus blokir.
      /// </summary>
      public string? Note { get; set; }

      /// <summary>
      /// Isian langkah ini dalam bentuk JSON milik modul, untuk langkah yang memang meminta isian. Modul
      /// yang memeriksa isinya; engine hanya meneruskan dan menggambarnya di PDF. Pada penolakan isian ikut
      /// digambar kalau bisa diterima, tetapi penolakannya tidak pernah bergantung pada isian itu.
      /// </summary>
      public string? Payload { get; set; }

      /// <summary>
      /// Tetap terapkan usulan walaupun nilainya sudah berubah di luar. Penimpaan sadar, tercatat pada
      /// entitas yang bersangkutan. Tidak berlaku untuk entitas yang sudah tidak ada.
      /// </summary>
      public bool Override { get; set; }

      /// <summary>
      /// Setujui walaupun syarat langkah ini belum terpenuhi. Hanya boleh dipakai pemegang claim penembus
      /// yang dideklarasikan modul, dan alasannya wajib.
      /// </summary>
      public bool GuardOverride { get; set; }
   }

   /// <summary>Hasil satu keputusan. Satu baris hasil untuk satu keputusan yang dikirim.</summary>
   /// <remarks>
   /// Setiap keputusan diproses dalam transaksinya sendiri, jadi satu keputusan yang gagal tidak
   /// menggagalkan keputusan lain dalam kiriman yang sama - itulah sebabnya hasilnya berupa daftar, bukan
   /// satu exception.
   /// </remarks>
   public class ApprovalDecisionResult
   {
      /// <summary>Request yang diputuskan.</summary>
      public string cApprovalRequestId { get; set; } = string.Empty;

      /// <summary>Langkah yang diputuskan.</summary>
      public string StepName { get; set; } = string.Empty;

      /// <summary>Keputusannya berhasil dicatat.</summary>
      public bool Success { get; set; }

      /// <summary>Kenapa gagal, kalau gagal.</summary>
      public string? ErrorMessage { get; set; }

      /// <summary>
      /// Kolom-kolom yang nilainya sudah berubah di luar sehingga usulan tidak bisa diterapkan apa adanya.
      /// Terisi hanya kalau kegagalannya memang karena itu; layar menampilkannya supaya approver bisa
      /// memilih tetap menerapkan atau menolak.
      /// </summary>
      public ApprovalConflictField[] Conflicts { get; set; } = [];

      /// <summary>
      /// <c>true</c> kalau konfliknya tidak bisa ditimpa - entitasnya sudah tidak ada, jadi tidak ada yang
      /// bisa diterapkan dan satu-satunya pilihan adalah menolak.
      /// </summary>
      public bool ConflictIsFinal { get; set; }

      /// <summary>Keadaan request sesudah keputusan ini, supaya layar tidak perlu memuat ulang.</summary>
      public ApprovalRequestInfo? Request { get; set; }
   }

   /// <summary>Satu kolom yang nilainya sudah berubah di luar sejak request diajukan.</summary>
   public class ApprovalConflictField
   {
      /// <summary>Entitas tempat kolom ini berada.</summary>
      public string Entity { get; set; } = string.Empty;

      /// <summary>Kunci entitasnya dalam bentuk yang bisa dibaca user.</summary>
      public string EntityKey { get; set; } = string.Empty;

      /// <summary>Nama kolomnya.</summary>
      public string FieldName { get; set; } = string.Empty;

      /// <summary>Nilainya saat request diajukan.</summary>
      public string? OldValue { get; set; }

      /// <summary>Nilai yang ditemukan sekarang.</summary>
      public string? CurrentValue { get; set; }

      /// <summary>Nilai yang diusulkan.</summary>
      public string? NewValue { get; set; }
   }

   /// <summary>
   /// Satu baris daftar request: kolom standar engine ditambah ringkasan milik modul, cukup untuk
   /// ditampilkan dan disaring tanpa memuat rinciannya.
   /// </summary>
   public class ApprovalRequestInfo
   {
      public string cApprovalRequestId { get; set; } = string.Empty;

      /// <summary>Jenis dokumennya.</summary>
      public string DocType { get; set; } = string.Empty;

      /// <summary>
      /// Kunci dokumennya dalam bentuk bakunya - bentuk yang sama yang dipakai saat meminta request
      /// sebuah dokumen, dan yang dipecah modul kalau ia perlu bagian-bagiannya.
      /// </summary>
      public string DocKey { get; set; } = string.Empty;

      /// <summary>Kunci dokumennya dalam bentuk yang bisa dibaca user.</summary>
      public string DocKeyDisplay { get; set; } = string.Empty;

      /// <summary>Versi dokumennya.</summary>
      public string DocVersion { get; set; } = string.Empty;

      /// <summary>Apakah data usulannya menumpang di request ini.</summary>
      public ApprovalKind Kind { get; set; }

      /// <summary>Tahap hidup request ini.</summary>
      public ApprovalStage Stage { get; set; }

      /// <summary>Nama pengajunya.</summary>
      public string RequesterName { get; set; } = string.Empty;

      /// <summary>Kapan diajukan.</summary>
      public DateTime RequestDate { get; set; }

      /// <summary>Kapan selesai, kalau sudah.</summary>
      public DateTime? CompletedDate { get; set; }

      /// <summary>Level yang sedang menunggu.</summary>
      public int Level { get; set; }

      /// <summary>Nama langkah yang sedang menunggu keputusan, dipisah koma kalau lebih dari satu.</summary>
      public string WaitingSteps { get; set; } = string.Empty;

      /// <summary>Jumlah langkah yang sudah disetujui, dari seluruh langkah yang berlaku.</summary>
      public int SignedStepCount { get; set; }

      /// <summary>Jumlah langkah yang berlaku pada request ini.</summary>
      public int TotalStepCount { get; set; }

      /// <summary>Request ini menunggu keputusan user yang meminta daftar ini.</summary>
      public bool WaitingForMe { get; set; }

      /// <summary>
      /// User yang meminta daftar ini boleh menandatangani langkah yang menunggu sebagai pengganti,
      /// walaupun bukan penanda tangan tercatatnya.
      /// </summary>
      public bool CanSignAsSubstitute { get; set; }

      /// <summary>Request ini punya PDF dokumen.</summary>
      public bool HasPdf { get; set; }

      /// <summary>Berapa kali dokumen ini sudah diajukan ulang untuk versi yang sama.</summary>
      public int ReinstateCount { get; set; }

      /// <summary>
      /// Keputusan jenis dokumen ini hanya boleh diambil setelah dokumennya dibuka, jadi layar approval
      /// tidak menawarkannya dari daftar. Dijaga di layar saja - server tidak mencatat dokumen sudah
      /// dibuka - karena yang dikejar adalah memastikan orangnya benar-benar melihat dokumennya.
      /// </summary>
      public bool RequireOpen { get; set; }

      /// <summary>
      /// Ringkasan milik modul, dipotret saat diajukan, berupa objek JSON berisi pasangan nama kolom dan
      /// nilainya. Nama kolomnya ditentukan modul, jadi layar tidak boleh menganggapnya tetap.
      /// </summary>
      public string? Summary { get; set; }

      /// <summary>
      /// Aksi terakhir pada request ini, salah satu dari <see cref="ApprovalTimelineAction"/>. Komentar
      /// bebas tidak dihitung - yang dimaksud keputusan atau pengajuan terakhir.
      /// </summary>
      public string LastAction { get; set; } = string.Empty;

      /// <summary>Nama pelaku aksi terakhir.</summary>
      public string LastActorName { get; set; } = string.Empty;

      /// <summary>Kapan aksi terakhir terjadi.</summary>
      public DateTime LastActionDate { get; set; }
   }

   /// <summary>
   /// Kata kunci aksi pada riwayat request, supaya layar bisa memetakannya ke ikon dan teks tanpa
   /// menebak isi kalimat.
   /// </summary>
   public static class ApprovalTimelineAction
   {
      /// <summary>Request diajukan.</summary>
      public const string Submitted = "Submitted";

      /// <summary>Langkah disetujui oleh penanda tangan yang memang ditetapkan.</summary>
      public const string Approved = "Approved";

      /// <summary>Langkah disetujui oleh pengganti, atas nama penanda tangan utama.</summary>
      public const string ApprovedAsSubstitute = "ApprovedAsSubstitute";

      /// <summary>Langkah disetujui dengan menembus blokirnya.</summary>
      public const string ApprovedWithOverride = "ApprovedWithOverride";

      /// <summary>Langkah ditolak dan request berhenti.</summary>
      public const string Rejected = "Rejected";

      /// <summary>Request ditarik kembali saat masih menunggu keputusan.</summary>
      public const string Cancelled = "Cancelled";

      /// <summary>Request ditarik kembali setelah seluruh langkahnya selesai.</summary>
      public const string ReinstatedAfterFinish = "ReinstatedAfterFinish";

      /// <summary>Komentar bebas.</summary>
      public const string Commented = "Commented";
   }

   /// <summary>Rincian satu request: langkah-langkahnya, usulan perubahannya, dan riwayatnya.</summary>
   public class ApprovalRequestDetail
   {
      /// <summary>Baris ringkas request ini.</summary>
      public ApprovalRequestInfo Info { get; set; } = new();

      /// <summary>Seluruh langkahnya, berurutan per level.</summary>
      public ApprovalStepInfo[] Steps { get; set; } = [];

      /// <summary>
      /// Usulan perubahan per kolom, untuk request yang data usulannya menumpang di sini. Kosong untuk
      /// request gerbang status.
      /// </summary>
      public ApprovalConflictField[] Changes { get; set; } = [];

      /// <summary>Riwayat gabungan aksi dan komentar, tersambung lintas pengajuan ulang.</summary>
      public ApprovalTimelineEntry[] Timeline { get; set; } = [];
   }

   /// <summary>Satu langkah seperti yang ditampilkan di layar.</summary>
   public class ApprovalStepInfo
   {
      /// <summary>Nama langkahnya.</summary>
      public string StepName { get; set; } = string.Empty;

      /// <summary>Level tempat langkah ini berada.</summary>
      public int Level { get; set; }

      /// <summary>Keadaan langkah ini.</summary>
      public ApprovalStepStatus Status { get; set; }

      /// <summary>Nama penanda tangannya, kalau sudah diputuskan.</summary>
      public string? SignerName { get; set; }

      /// <summary>Nama penanda tangan utama yang diwakili, kalau ditandatangani pengganti atau penembus.</summary>
      public string? OnBehalfName { get; set; }

      /// <summary>Atas dasar apa tanda tangannya sah.</summary>
      public ApprovalSignerRole SignerRole { get; set; }

      /// <summary>Kapan ditandatangani.</summary>
      public DateTime? SignedDate { get; set; }

      /// <summary>Alasan keputusannya.</summary>
      public string? Note { get; set; }

      /// <summary>Kode verifikasi tanda tangan ini.</summary>
      public string? VerificationCode { get; set; }

      /// <summary>Nama orang-orang yang ditetapkan sebagai penanda tangan langkah ini.</summary>
      public string[] AssignedSignerNames { get; set; } = [];

      /// <summary>Langkah ini sedang menunggu keputusan user yang meminta data ini.</summary>
      public bool WaitingForMe { get; set; }

      /// <summary>
      /// Langkah ini menunggu keputusan, user yang meminta data ini memegang claim-nya, tapi bukan
      /// penanda tangan yang tercatat - jadi ia hanya bisa menandatangani sebagai pengganti, dengan
      /// alasan wajib.
      /// </summary>
      public bool CanSignAsSubstitute { get; set; }

      /// <summary>Langkah ini meminta isian sebelum bisa diputuskan.</summary>
      public bool RequiresInput { get; set; }
   }

   /// <summary>Satu baris riwayat: sebuah aksi pada request, atau sebuah komentar.</summary>
   public class ApprovalTimelineEntry
   {
      /// <summary>Kapan terjadi.</summary>
      public DateTime Date { get; set; }

      /// <summary>Siapa pelakunya.</summary>
      public string ActorName { get; set; } = string.Empty;

      /// <summary>Apa yang terjadi, dalam kata kunci yang bisa dipetakan layar ke ikon dan teksnya.</summary>
      public string Action { get; set; } = string.Empty;

      /// <summary>Langkah yang bersangkutan, kalau aksinya menyangkut satu langkah.</summary>
      public string? StepName { get; set; }

      /// <summary>Isi komentar atau alasannya.</summary>
      public string? Note { get; set; }

      /// <summary>
      /// Request tempat baris ini berasal. Bisa berbeda dari request yang sedang dibuka, karena riwayat
      /// tersambung lintas pengajuan ulang.
      /// </summary>
      public string cApprovalRequestId { get; set; } = string.Empty;
   }

   /// <summary>Penyaring daftar request, dikirim apa adanya ke server supaya penyaringan tidak di client.</summary>
   public class ApprovalQuery
   {
      /// <summary>
      /// Hanya request yang menunggu keputusan user ini. <c>false</c> berarti seluruh request yang boleh
      /// ia lihat.
      /// </summary>
      public bool WaitingForMeOnly { get; set; }

      /// <summary>
      /// Hanya request yang bisa ditandatangani user ini sebagai pengganti: ia memegang claim langkah
      /// yang menunggu, tapi penanda tangan langkah itu ditetapkan per orang dan ia bukan salah satunya.
      /// Terpisah dari <see cref="WaitingForMeOnly"/> karena yang ini bukan pekerjaan miliknya.
      /// </summary>
      public bool CanSignAsSubstituteOnly { get; set; }

      /// <summary>Batasi ke satu jenis dokumen.</summary>
      public string? DocType { get; set; }

      /// <summary>Batasi ke satu dokumen tertentu, dipakai panel status di layar dokumennya.</summary>
      public string? DocKey { get; set; }

      /// <summary>Batasi ke satu versi dokumen itu.</summary>
      public string? DocVersion { get; set; }

      /// <summary>Batasi ke tahap-tahap tertentu. Kosong berarti semua tahap.</summary>
      public ApprovalStage[] Stages { get; set; } = [];

      /// <summary>Pencarian teks bebas atas kunci dokumen, nama pengaju, dan ringkasan modul.</summary>
      public string? Search { get; set; }

      /// <summary>Halaman yang diminta, dimulai dari satu.</summary>
      public int Page { get; set; } = 1;

      /// <summary>Jumlah baris per halaman.</summary>
      public int PageSize { get; set; } = 50;

      /// <summary>Kolom pengurut. Kosong berarti urutan bawaan, yaitu yang paling lama menunggu di atas.</summary>
      public string? SortBy { get; set; }

      /// <summary>Urutkan menurun.</summary>
      public bool SortDescending { get; set; }
   }

   /// <summary>Satu halaman hasil beserta jumlah seluruhnya, supaya pager tahu ada berapa halaman.</summary>
   /// <typeparam name="T">Jenis baris yang dihalamankan.</typeparam>
   public class PagedResult<T>
   {
      /// <summary>Baris pada halaman ini.</summary>
      public T[] Items { get; set; } = [];

      /// <summary>Jumlah seluruh baris yang cocok dengan penyaringnya, bukan hanya di halaman ini.</summary>
      public int TotalCount { get; set; }

      /// <summary>Halaman yang dikembalikan, dimulai dari satu.</summary>
      public int Page { get; set; }

      /// <summary>Jumlah baris per halaman yang dipakai.</summary>
      public int PageSize { get; set; }
   }

   /// <summary>
   /// Hasil pemeriksaan apakah sebuah langkah boleh diputuskan sekarang. Dievaluasi ulang setiap layar
   /// dibuka, sehingga blokir yang syaratnya sudah terpenuhi membuka sendiri.
   /// </summary>
   public class ApprovalGuardResult
   {
      /// <summary>Langkah ini boleh diputuskan.</summary>
      public bool Allowed { get; set; }

      /// <summary>Kenapa belum boleh, untuk ditampilkan di samping tombol yang mati.</summary>
      public string? Reason { get; set; }

      /// <summary>
      /// Blokirnya boleh ditembus pemegang claim tertentu. Dipakai layar untuk memunculkan tombol
      /// menyetujui dengan alasan wajib.
      /// </summary>
      public bool CanBeOverridden { get; set; }

      /// <summary>
      /// User yang meminta pemeriksaan ini memegang claim penembusnya. Layar hanya memunculkan tombol
      /// penembus kalau ini benar.
      /// </summary>
      public bool CallerCanOverride { get; set; }
   }

   /// <summary>
   /// Satu baris daftar pekerjaan user aktif, apa pun sumbernya: pekerjaan panjang yang sedang berjalan,
   /// dokumen yang menunggu tanda tangannya, atau usulan perubahan data yang menunggu keputusannya.
   /// </summary>
   /// <remarks>
   /// Bentuknya satu untuk semua sumber supaya daftar itu bisa menampung jenis pekerjaan baru tanpa
   /// berubah. Baris yang berarti "perlu tindakan" dihitung dari datanya, tidak disimpan, sehingga ia
   /// hilang sendiri dari daftar orang lain begitu satu orang memutuskannya.
   /// </remarks>
   public class HubTaskInfo
   {
      /// <summary>Sumber baris ini, dipakai layar untuk mengelompokkannya.</summary>
      public string Source { get; set; } = string.Empty;

      /// <summary>Penanda baris ini di dalam sumbernya.</summary>
      public string Id { get; set; } = string.Empty;

      /// <summary>Judul yang ditampilkan.</summary>
      public string Title { get; set; } = string.Empty;

      /// <summary>Keterangan singkat di bawah judulnya.</summary>
      public string? Description { get; set; }

      /// <summary>Jumlah pekerjaan yang diwakili baris ini, untuk baris yang merangkum beberapa.</summary>
      public int Count { get; set; }

      /// <summary>Umur pekerjaan tertua yang diwakili baris ini.</summary>
      public TimeSpan? OldestAge { get; set; }

      /// <summary>Kemajuan nol sampai seratus, untuk pekerjaan yang bisa diukur.</summary>
      public int? Progress { get; set; }

      /// <summary>Nama aksi yang ditawarkan baris ini, misalnya membuka layarnya.</summary>
      public string? ActionName { get; set; }

      /// <summary>Navigasi yang dibuka aksinya, beserta penyaring yang sudah terpasang.</summary>
      public string? NavigationTarget { get; set; }

      /// <summary>Parameter navigasinya.</summary>
      public string? NavigationParameter { get; set; }
   }
}
