using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Alur persetujuan satu jenis dokumen, seperti yang dideklarasikan modul pemiliknya: langkah apa
   /// saja, siapa yang boleh memutuskan, dan apa yang terjadi di sepanjang alurnya.
   /// </summary>
   /// <remarks>
   /// Deklarasi dibuat sekali saat aplikasi dibangun, lalu tidak berubah selama aplikasi hidup. Engine
   /// membacanya untuk hal-hal yang sama bagi semua jenis dokumen; bagian yang hanya dipahami modul -
   /// memuat dokumennya, menentukan penanda tangan, menerapkan perubahan - tinggal sebagai delegate di
   /// turunan bertipe deklarasi ini.
   /// </remarks>
   public abstract class ApprovalFlowDeclaration
   {
      /// <summary>Jenis dokumen yang alurnya dideklarasikan di sini.</summary>
      public required string DocType { get; init; }

      /// <summary>Nama modul pemilik dokumennya, sekaligus modul pemilik claim-claim alurnya.</summary>
      public required string ModuleName { get; init; }

      /// <summary>
      /// Claim yang memberi hak melihat request jenis dokumen ini tanpa ikut memutuskannya - untuk
      /// pembaca yang perlu memantau tanpa menandatangani.
      /// </summary>
      public required ClaimAction ViewClaim { get; init; }

      /// <summary>Apakah data usulannya menumpang di request, atau sudah ada di dokumennya.</summary>
      public abstract ApprovalKind Kind { get; }

      /// <summary>Tipe service modul pemilik dokumennya.</summary>
      public abstract Type ServicesType { get; }

      private IReadOnlyList<Type>? _moduleDbContextTypes;

      /// <summary>
      /// Context database yang diminta service modul pemilik dokumennya lewat constructor - tempat handler
      /// modul menulis. Context-context inilah yang diikutsertakan engine ke transaksi keputusan, jadi modul
      /// tidak perlu menyebutkannya lagi. Context inti tidak dihitung: ia sudah pasti ikut.
      /// </summary>
      internal IReadOnlyList<Type> ModuleDbContextTypes => _moduleDbContextTypes ??=
         [.. ServicesType.GetConstructors()
            .OrderByDescending(r => r.GetParameters().Length)
            .FirstOrDefault()?.GetParameters()
            .Select(r => r.ParameterType)
            .Where(r => typeof(Microsoft.EntityFrameworkCore.DbContext).IsAssignableFrom(r) && r != typeof(ApiCoreContext))
            .Distinct() ?? []];

      /// <summary>Tipe record kunci dokumennya, atau kosong kalau jenis ini tidak memakai kunci bertipe.</summary>
      public virtual Type? KeyType => null;

      /// <summary>
      /// Seluruh claim yang dipakai alur ini, termasuk <see cref="ViewClaim"/>. Dipakai engine saat
      /// memeriksa siapa yang boleh melihat sebuah request.
      /// </summary>
      public abstract IReadOnlyList<ClaimAction> Claims { get; }

      /// <summary>
      /// <c>true</c> kalau keputusan jenis dokumen ini hanya boleh diambil setelah dokumennya dibuka,
      /// sehingga tidak bisa disetujui berbondong-bondong dari daftar. Hanya bermakna untuk alur
      /// dokumen; alur usulan perubahan data selalu <c>false</c>.
      /// </summary>
      public bool RequireOpen { get; set; }

      /// <summary>
      /// <c>true</c> kalau PDF-nya diberi halaman tambahan berisi tabel seluruh langkah beserta tanda
      /// tangannya.
      /// </summary>
      public bool ApprovalSheet { get; set; }

      /// <summary>Apakah sebuah langkah meminta isian sebelum bisa diputuskan.</summary>
      /// <param name="stepName">Nama langkahnya.</param>
      public virtual bool StepRequiresInput(string stepName) => false;

      // The member below is how the engine reaches the delegates of a flow without knowing the
      // module's service type or key type: the typed subclass builds the typed context and calls the
      // delegate, and the engine only sees the result. Internal because only the engine has a scope to
      // hand in.

      /// <summary>
      /// Menjalankan pemeriksaan blokir sebuah langkah di modul pemiliknya.
      /// </summary>
      /// <returns>Hasil pemeriksaannya, atau <c>null</c> kalau langkah itu tidak punya pemeriksaan.</returns>
      internal virtual Task<ApprovalGuard?> EvaluateGuardAsync(ApprovalRunScope scope, string stepName,
         string? signerId) => Task.FromResult<ApprovalGuard?>(null);

      // Submitting, advancing and finishing only make sense for a document flow, so the base class
      // refuses them; the document flow overrides every one. Data flows get their own bridge when
      // their submission is written.

      /// <summary>Apakah jenis dokumen ini punya PDF yang dibekukan saat pengajuan.</summary>
      internal virtual bool HasPdf => false;

      /// <summary>
      /// Menyusun rencana langkah untuk sebuah pengajuan: setiap langkah beserta apakah ia berlaku untuk
      /// dokumen ini. Penanda tangannya belum ditentukan di sini.
      /// </summary>
      internal virtual Task<IReadOnlyList<ApprovalPlannedStep>> PlanStepsAsync(ApprovalRunScope scope) =>
         throw NotADocumentFlow();

      /// <summary>
      /// Menentukan penanda tangan sebuah langkah dari isi dokumennya.
      /// </summary>
      /// <returns>Daftar id user, atau <c>null</c> kalau langkah itu tidak menetapkan penanda tangan.</returns>
      internal virtual Task<IReadOnlyList<string>?> ResolveSignersAsync(ApprovalRunScope scope, string stepName) =>
         throw NotADocumentFlow();

      /// <summary>Menghitung kolom ringkasan modul untuk sebuah pengajuan.</summary>
      internal virtual Task<IReadOnlyDictionary<string, string?>?> SummaryAsync(ApprovalRunScope scope) =>
         throw NotADocumentFlow();

      /// <summary>Mengambil PDF dokumennya, atau <c>null</c> kalau jenis ini tidak punya PDF.</summary>
      internal virtual Task<Stream?> OpenPdfAsync(ApprovalRunScope scope) => throw NotADocumentFlow();

      /// <summary>
      /// Menggeser posisi kotak-kotak rencana ke tempat mereka sebenarnya di PDF dasar dokumen ini, kalau
      /// modul menyatakan tata letaknya berubah menurut isi dokumen. Tanpa pernyataan itu posisinya
      /// dibiarkan seperti yang dideklarasikan.
      /// </summary>
      internal virtual Task LocateSlotsAsync(ApprovalRunScope scope, IReadOnlyList<ApprovalPlannedStep> plan, Stream pdf) =>
         throw NotADocumentFlow();

      /// <summary>Menjalankan hook setelah pengajuan tersimpan.</summary>
      internal virtual Task RunSubmittedAsync(ApprovalRunScope scope) => throw NotADocumentFlow();

      /// <summary>Menjalankan hook penyelesaian sebuah level, di dalam transaksi.</summary>
      internal virtual Task RunLevelCompletedAsync(ApprovalRunScope scope, int level) => throw NotADocumentFlow();

      /// <summary>Menjalankan hook penyelesaian request, di dalam transaksi.</summary>
      internal virtual Task RunFinishingAsync(ApprovalRunScope scope) => throw NotADocumentFlow();

      /// <summary>Menjalankan hook penyelesaian request, sesudah transaksi tersimpan.</summary>
      internal virtual Task RunFinishedAsync(ApprovalRunScope scope) => throw NotADocumentFlow();

      /// <summary>
      /// Aturan setiap langkah yang dipakai saat langkah itu ditandatangani: claim-nya, apakah hanya
      /// penanda tangannya sendiri yang boleh, dan langkah mana yang penanda tangannya harus berbeda.
      /// </summary>
      internal virtual IReadOnlyList<ApprovalStepRule> StepRules => throw NotADocumentFlow();

      /// <summary>
      /// Memeriksa isian sebuah langkah di modul pemiliknya, lalu mengambil nilai kotak-kotaknya.
      /// </summary>
      /// <returns>Nilai setiap kotak isian, atau <c>null</c> kalau langkah itu tidak meminta isian.</returns>
      internal virtual Task<IReadOnlyList<ApprovalInputValue>?> ValidateInputAsync(ApprovalRunScope scope,
         string stepName, string? signerId, string? payloadJson) => throw NotADocumentFlow();

      /// <summary>Menuliskan akibat isian sebuah langkah ke dokumennya, di dalam transaksi.</summary>
      internal virtual Task RunInputSignedAsync(ApprovalRunScope scope, string stepName, string? signerId,
         string? payloadJson) => throw NotADocumentFlow();

      /// <summary>Menjalankan hook pemeriksaan keputusan, sebelum keputusannya ditulis.</summary>
      internal virtual Task RunSigningAsync(ApprovalRunScope scope, string stepName, string? signerId,
         ApprovalDecision decision) => throw NotADocumentFlow();

      /// <summary>Menjalankan hook akibat keputusan, di dalam transaksi yang sama dengan keputusannya.</summary>
      internal virtual Task RunSignedAsync(ApprovalRunScope scope, string stepName, string? signerId,
         ApprovalDecision decision) => throw NotADocumentFlow();

      /// <summary>Menjalankan hook penolakan request, di dalam transaksi.</summary>
      internal virtual Task RunRejectingAsync(ApprovalRunScope scope) => throw NotADocumentFlow();

      /// <summary>Menjalankan hook penolakan request, sesudah transaksi tersimpan.</summary>
      internal virtual Task RunRejectedAsync(ApprovalRunScope scope) => throw NotADocumentFlow();

      /// <summary>
      /// Menjalankan hook penarikan kembali request, di dalam transaksi. <paramref name="stage"/> adalah
      /// tahap request sebelum ditarik: masih menunggu, atau sudah selesai seluruhnya.
      /// </summary>
      internal virtual Task RunReinstatingAsync(ApprovalRunScope scope, ApprovalStage stage) =>
         throw NotADocumentFlow();

      private InvalidOperationException NotADocumentFlow() =>
         new($"Approval flow for '{DocType}' is not a document flow, so it has no steps to run.");
   }

   /// <summary>
   /// Yang dibutuhkan engine untuk menyerahkan sebuah request ke handler modulnya: siapa yang sedang
   /// menjalankan engine (sumber keterangan request-nya), penyedia service permintaan ini, dan request
   /// yang dikerjakan.
   /// </summary>
   /// <param name="Engine">Service engine yang sedang berjalan.</param>
   /// <param name="Provider">Penyedia service permintaan ini.</param>
   /// <param name="Request">Baris request yang dikerjakan.</param>
   /// <param name="Items">
   /// Usulan perubahan request yang dikerjakan, untuk alur usulan perubahan data; kosong untuk alur dokumen.
   /// Diisi engine sebelum menyerahkan giliran ke modul, supaya hook modul bisa melihat apa yang diusulkan.
   /// </param>
   internal sealed record ApprovalRunScope(ServicesBase Engine, IServiceProvider Provider,
      ta_ApprovalRequest Request, IReadOnlyList<ApprovalDataItem>? Items = null);

   /// <summary>
   /// Aturan sebuah langkah yang diperiksa engine saat langkah itu ditandatangani.
   /// </summary>
   /// <param name="Name">Nama langkahnya.</param>
   /// <param name="Claim">Claim yang harus dipegang untuk memutuskannya.</param>
   /// <param name="Strict">Apakah pengganti ditolak.</param>
   /// <param name="DistinctFrom">Langkah yang penanda tangannya harus berbeda, atau <c>null</c>.</param>
   internal sealed record ApprovalStepRule(string Name, ClaimAction Claim, bool Strict, string? DistinctFrom);

   /// <summary>
   /// Alur dokumen: beberapa level yang dijalani berurutan, masing-masing berisi satu langkah atau
   /// beberapa langkah yang berjalan bersamaan.
   /// </summary>
   /// <typeparam name="TServices">Service modul pemilik dokumen.</typeparam>
   /// <typeparam name="TKey">Record kunci dokumennya.</typeparam>
   public partial class ApprovalDocumentFlowDeclaration<TServices, TKey> : ApprovalFlowDeclaration
      where TServices : ServicesBase, IServices
      where TKey : notnull
   {
      /// <inheritdoc />
      public override ApprovalKind Kind => ApprovalKind.Document;

      /// <inheritdoc />
      public override Type ServicesType => typeof(TServices);

      /// <inheritdoc />
      public override Type KeyType => typeof(TKey);

      /// <summary>Level-level alurnya, berurutan.</summary>
      public List<ApprovalLevelDeclaration<TServices, TKey>> Levels { get; } = [];

      /// <summary>Seluruh langkah alurnya, berurutan per level lalu per urutan di dalam level.</summary>
      public IEnumerable<ApprovalStepDeclaration<TServices, TKey>> Steps =>
         Levels.OrderBy(r => r.Level).SelectMany(r => r.Steps.OrderBy(q => q.Order));

      /// <summary>
      /// Cara mengambil PDF dokumennya, atau kosong kalau jenis dokumen ini tidak punya PDF. Dipanggil
      /// saat pengajuan, dan hasilnya dibekukan sebagai PDF dasar request itu.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Task<Stream>>? Pdf { get; set; }

      /// <summary>
      /// Cara menemukan posisi kotak-kotak di PDF dokumen yang tata letaknya berubah menurut isinya.
      /// Dipanggil saat pengajuan dengan PDF dasar yang baru dibuat, sebelum posisi kotak dibekukan.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Stream, Task<Func<ApprovalSlot, ApprovalSlot?>>>? PdfLayout { get; set; }

      /// <summary>
      /// Cara menghitung kolom ringkasan milik modul, dipotret saat pengajuan. Dipakai daftar request
      /// untuk menyaring, mengurutkan, dan menampilkan keterangan yang hanya modul pahami.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Task<IReadOnlyDictionary<string, string?>>>? Summary { get; set; }

      /// <summary>Dijalankan setelah pengajuan tersimpan. Kegagalannya tidak membatalkan pengajuan.</summary>
      public Func<IApprovalContext<TServices, TKey>, Task>? OnSubmitted { get; set; }

      /// <summary>
      /// Memeriksa sebuah keputusan sebelum ditulis, di dalam transaksinya. Di sinilah isian per langkah
      /// divalidasi ulang di server. Kegagalannya menggagalkan keputusan itu.
      /// </summary>
      public Func<IApprovalStepContext<TServices, TKey>, ApprovalDecision, Task>? OnSigning { get; set; }

      /// <summary>
      /// Menulis akibat sebuah tanda tangan ke dokumennya, di dalam transaksi yang sama dengan tanda
      /// tangan itu. Kegagalannya membatalkan tanda tangannya juga.
      /// </summary>
      public Func<IApprovalStepContext<TServices, TKey>, ApprovalDecision, Task>? OnSigned { get; set; }

      /// <summary>
      /// Dijalankan di dalam transaksi, saat langkah terakhir disetujui dan request akan selesai.
      /// Kegagalannya mengembalikan segalanya seperti sebelum keputusan itu diambil.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Task>? OnFinishing { get; set; }

      /// <summary>Dijalankan setelah request selesai tersimpan. Kegagalannya tidak merusak data.</summary>
      public Func<IApprovalContext<TServices, TKey>, Task>? OnFinished { get; set; }

      /// <summary>
      /// Dijalankan di dalam transaksi, saat sebuah langkah ditolak dan request akan berhenti.
      /// Kegagalannya mengembalikan segalanya seperti sebelum penolakan itu.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Task>? OnRejecting { get; set; }

      /// <summary>Dijalankan setelah penolakan tersimpan. Kegagalannya tidak merusak data.</summary>
      public Func<IApprovalContext<TServices, TKey>, Task>? OnRejected { get; set; }

      /// <summary>
      /// Dijalankan di dalam transaksi saat sebuah request ditarik kembali. Di sinilah modul mencabut
      /// status yang sudah ditulis karena request itu, dan di sinilah ia boleh menolak penarikan -
      /// dengan melempar - kalau dokumennya sudah diproses lebih lanjut.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, ApprovalStage, Task>? OnReinstating { get; set; }

      /// <inheritdoc />
      public override IReadOnlyList<ClaimAction> Claims =>
         [ViewClaim, .. Steps.Select(r => r.Claim)];

      /// <inheritdoc />
      public override bool StepRequiresInput(string stepName) =>
         Steps.Any(r => string.Equals(r.Name, stepName, StringComparison.OrdinalIgnoreCase) && r.Input is not null);

      internal override async Task<ApprovalGuard?> EvaluateGuardAsync(ApprovalRunScope scope, string stepName,
         string? signerId) {
         var step = Steps.FirstOrDefault(r => string.Equals(r.Name, stepName, StringComparison.OrdinalIgnoreCase));
         if (step?.Guard is null) return null;

         var context = new ApprovalStepContext<TServices, TKey> {
            Services = ApprovalModuleService.Resolve<TServices>(scope.Engine, scope.Provider),
            DocKey = ApprovalKey.FromCanonical<TKey>(scope.Request.cApprovalRequestDocKey),
            DocVersion = scope.Request.cApprovalRequestDocVersion,
            ApprovalRequestId = scope.Request.cApprovalRequestId,
            RequesterId = scope.Request.cApprovalRequestRequesterId,
            StepName = step.Name,
            SignerId = signerId
         };

         return await step.Guard(context);
      }
   }

   /// <summary>
   /// Satu level alur dokumen: langkah-langkah yang berjalan bersamaan, dan apa yang terjadi begitu
   /// semuanya disetujui.
   /// </summary>
   /// <typeparam name="TServices">Service modul pemilik dokumen.</typeparam>
   /// <typeparam name="TKey">Record kunci dokumennya.</typeparam>
   public class ApprovalLevelDeclaration<TServices, TKey> where TKey : notnull
   {
      /// <summary>Nomor levelnya. Level dijalani dari yang terkecil.</summary>
      public required int Level { get; init; }

      /// <summary>Langkah-langkah di level ini. Semuanya harus disetujui sebelum level berikutnya mulai.</summary>
      public List<ApprovalStepDeclaration<TServices, TKey>> Steps { get; } = [];

      /// <summary>
      /// Dijalankan di dalam transaksi begitu seluruh langkah level ini disetujui. Dipakai untuk tonggak
      /// di tengah alur - status dokumen yang berubah sebelum langkah terakhir. Kegagalannya
      /// mengembalikan keputusan yang memicunya.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Task>? OnCompleted { get; set; }
   }

   /// <summary>Satu langkah alur dokumen, seperti yang dideklarasikan modul.</summary>
   /// <typeparam name="TServices">Service modul pemilik dokumen.</typeparam>
   /// <typeparam name="TKey">Record kunci dokumennya.</typeparam>
   public class ApprovalStepDeclaration<TServices, TKey> where TKey : notnull
   {
      /// <summary>Nama langkahnya. Dipakai di layar, di PDF, dan sebagai nama claim-nya.</summary>
      public required string Name { get; init; }

      /// <summary>Claim yang harus dipegang untuk memutuskan langkah ini.</summary>
      public required ClaimAction Claim { get; init; }

      /// <summary>Level tempat langkah ini berada.</summary>
      public required int Level { get; init; }

      /// <summary>Urutan langkah ini di dalam levelnya, untuk tampilan yang stabil.</summary>
      public required int Order { get; init; }

      /// <summary>
      /// Di mana tanda tangannya digambar pada PDF, atau kosong untuk jenis dokumen yang tidak punya
      /// PDF.
      /// </summary>
      public ApprovalSlot? Slot { get; init; }

      /// <summary>
      /// <c>true</c> kalau langkah ini hanya boleh ditandatangani penanda tangannya sendiri, sehingga
      /// pengganti ditolak.
      /// </summary>
      public bool Strict { get; init; }

      /// <summary>
      /// Nama langkah yang penanda tangannya tidak boleh sama dengan penanda tangan langkah ini, atau
      /// kosong kalau tidak ada syarat seperti itu.
      /// </summary>
      public string? DistinctFrom { get; init; }

      /// <summary>
      /// Cara menentukan siapa saja penanda tangan langkah ini, dibaca dari isi dokumennya saat
      /// pengajuan. Kosong berarti langkah ini terbuka bagi semua pemegang claim-nya.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Task<IReadOnlyList<string>>>? Signers { get; init; }

      /// <summary>
      /// Cara menentukan langkah ini berlaku atau dilewati, dievaluasi saat pengajuan. Kosong berarti
      /// langkah ini selalu berlaku.
      /// </summary>
      public Func<IApprovalContext<TServices, TKey>, Task<bool>>? When { get; init; }

      /// <summary>
      /// Cara menentukan langkah ini sudah boleh diputuskan sekarang. Dievaluasi ulang setiap kali layar
      /// dibuka dan sekali lagi di server sebelum keputusannya ditulis, jadi ia membaca keadaan saat itu
      /// - bukan keadaan saat pengajuan.
      /// </summary>
      public Func<IApprovalStepContext<TServices, TKey>, Task<ApprovalGuard>>? Guard { get; init; }

      /// <summary>Isian yang diminta langkah ini sebelum bisa diputuskan, atau kosong kalau tidak ada.</summary>
      public IApprovalStepInput<TServices, TKey>? Input { get; init; }
   }

   /// <summary>
   /// Alur usulan perubahan data: satu claim persetujuan, dan daftar entitas yang boleh diusulkan
   /// berubah beserta cara memuat dan menerapkannya.
   /// </summary>
   /// <typeparam name="TServices">Service modul pemilik datanya.</typeparam>
   /// <remarks>
   /// Jenis ini tidak punya level dan tidak punya PDF: request disetujui atau ditolak utuh oleh pemegang
   /// satu claim, dan yang dilihat approver adalah daftar perubahan lama ke baru.
   /// </remarks>
   public partial class ApprovalDataFlowDeclaration<TServices> : ApprovalFlowDeclaration, IApprovalDataFlow
      where TServices : ServicesBase, IServices
   {
      /// <inheritdoc />
      public override ApprovalKind Kind => ApprovalKind.Data;

      /// <inheritdoc />
      public override Type ServicesType => typeof(TServices);

      /// <summary>
      /// Claim yang memberi hak menyetujui usulan perubahan jenis dokumen ini. Pemegangnya juga
      /// menyimpan perubahannya sendiri tanpa menunggu persetujuan siapa pun.
      /// </summary>
      public required ClaimAction ApproveClaim { get; init; }

      /// <summary>Entitas yang boleh diusulkan berubah lewat alur ini.</summary>
      public List<ApprovalEntityDeclaration> Entities { get; } = [];

      /// <summary>
      /// Cara menghitung kolom ringkasan milik modul, dipotret saat pengajuan.
      /// </summary>
      public Func<IApprovalDataContext<TServices>, Task<IReadOnlyDictionary<string, string?>>>? Summary { get; set; }

      /// <summary>Dijalankan setelah pengajuan tersimpan. Kegagalannya tidak membatalkan pengajuan.</summary>
      public Func<IApprovalDataContext<TServices>, Task>? OnSubmitted { get; set; }

      /// <summary>
      /// Dijalankan di dalam transaksi, setelah seluruh usulan diterapkan dan sebelum keputusannya
      /// tersimpan. Kegagalannya mengembalikan segalanya seperti sebelum keputusan itu.
      /// </summary>
      public Func<IApprovalDataContext<TServices>, Task>? OnFinishing { get; set; }

      /// <summary>Dijalankan setelah keputusan tersimpan. Kegagalannya tidak merusak data.</summary>
      public Func<IApprovalDataContext<TServices>, Task>? OnFinished { get; set; }

      /// <summary>Dijalankan di dalam transaksi saat usulan ditolak.</summary>
      public Func<IApprovalDataContext<TServices>, Task>? OnRejecting { get; set; }

      /// <summary>Dijalankan setelah penolakan tersimpan.</summary>
      public Func<IApprovalDataContext<TServices>, Task>? OnRejected { get; set; }

      /// <inheritdoc />
      public override IReadOnlyList<ClaimAction> Claims => [ViewClaim, ApproveClaim];
   }

   /// <summary>Satu entitas yang boleh diusulkan berubah, dilihat tanpa tipe kuncinya.</summary>
   public abstract class ApprovalEntityDeclaration
   {
      /// <summary>
      /// Nama entitasnya, ditulis dengan awalan nama modulnya supaya tidak bertabrakan antar modul.
      /// </summary>
      public required string Name { get; init; }

      /// <summary>Urutan penerapan, untuk entitas yang harus diterapkan setelah entitas lain.</summary>
      public int Order { get; init; }

      /// <summary>Tipe record kunci entitas ini.</summary>
      public abstract Type KeyType { get; }

      // The two members below are how the engine reaches the handlers of an entity without knowing the
      // service type or the key type of the module: the typed subclass translates the key and builds the
      // typed context, and the engine only sees plain values. Internal because only the engine has a
      // scope to hand in.

      /// <summary>Memuat nilai kolom entitas ini saat ini, atau <c>null</c> kalau entitasnya sudah tidak ada.</summary>
      internal abstract Task<IReadOnlyDictionary<string, string?>?> LoadAsync(ApprovalRunScope scope, string canonicalKey);

      /// <summary>Menerapkan usulan atas entitas ini, lalu mengembalikan kunci kanoniknya setelah diterapkan.</summary>
      internal abstract Task<string> ApplyAsync(ApprovalRunScope scope, string canonicalKey,
         ApprovalItemOperation operation, IReadOnlyList<ApprovalDataField> fields);
   }

   /// <summary>Satu entitas yang boleh diusulkan berubah, beserta cara memuat dan menerapkannya.</summary>
   /// <typeparam name="TServices">Service modul pemilik datanya.</typeparam>
   /// <typeparam name="TKey">Record kunci entitas ini.</typeparam>
   /// <remarks>
   /// Nilai kolom diserahkan sebagai teks, bukan sebagai baris bertipe, dengan sengaja: engine hanya
   /// membandingkan nilai lama, nilai sekarang, dan nilai usulan, dan itu bisa ia lakukan tanpa tahu
   /// bentuk tabelnya sama sekali - termasuk tabel warisan yang kuncinya gabungan beberapa kolom.
   /// Penerjemahan ke tipe aslinya tinggal di handler modul.
   /// </remarks>
   public class ApprovalEntityDeclaration<TServices, TKey> : ApprovalEntityDeclaration
      where TServices : ServicesBase, IServices
      where TKey : notnull
   {
      /// <inheritdoc />
      public override Type KeyType => typeof(TKey);

      /// <summary>
      /// Memuat nilai kolom entitas ini apa adanya saat ini, atau kosong kalau entitasnya sudah tidak
      /// ada. Dipanggil saat keputusan diambil, untuk dibandingkan dengan nilai yang dicatat waktu
      /// pengajuan.
      /// </summary>
      public Func<IApprovalDataContext<TServices>, TKey, Task<IReadOnlyDictionary<string, string?>?>>? Load { get; init; }

      /// <summary>
      /// Menerapkan usulan ke entitas ini, di dalam transaksi keputusan. Nilai balikannya adalah kunci
      /// entitas setelah diterapkan - sama dengan yang diminta untuk perubahan, dan kunci yang baru
      /// terbentuk untuk entitas baru.
      /// </summary>
      public Func<IApprovalDataContext<TServices>, ApprovalApplyRequest<TKey>, Task<TKey>>? Apply { get; init; }

      internal override async Task<IReadOnlyDictionary<string, string?>?> LoadAsync(ApprovalRunScope scope,
         string canonicalKey) {
         if (Load is null) {
            throw new InvalidOperationException($"Entity '{Name}' declares no way to load its current values.");
         }

         return await Load(ApprovalDataContext<TServices>.From(scope), ApprovalKey.FromCanonical<TKey>(canonicalKey));
      }

      internal override async Task<string> ApplyAsync(ApprovalRunScope scope, string canonicalKey,
         ApprovalItemOperation operation, IReadOnlyList<ApprovalDataField> fields) {
         if (Apply is null) {
            throw new InvalidOperationException($"Entity '{Name}' declares no way to apply a proposal.");
         }

         var applied = await Apply(ApprovalDataContext<TServices>.From(scope),
            new ApprovalApplyRequest<TKey>(ApprovalKey.FromCanonical<TKey>(canonicalKey), operation, fields));
         return ApprovalKey.ToCanonical(applied);
      }
   }

   /// <summary>Permintaan menerapkan usulan atas satu entitas.</summary>
   /// <typeparam name="TKey">Record kunci entitasnya.</typeparam>
   /// <param name="Key">Kunci entitas yang diterapkan.</param>
   /// <param name="Operation">Apa yang diusulkan atas entitas itu.</param>
   /// <param name="Fields">
   /// Kolom yang diusulkan berubah beserta nilainya. Kosong untuk penghapusan dan pengaktifan ulang.
   /// </param>
   public record ApprovalApplyRequest<TKey>(TKey Key, ApprovalItemOperation Operation,
      IReadOnlyList<ApprovalDataField> Fields) where TKey : notnull;

   /// <summary>
   /// Keterangan yang tersedia saat engine menyerahkan giliran ke modul dalam alur usulan perubahan
   /// data.
   /// </summary>
   /// <typeparam name="TServices">Service modul pemilik datanya.</typeparam>
   public interface IApprovalDataContext<out TServices>
   {
      /// <summary>Service modul pemilik datanya, sudah siap dipakai.</summary>
      TServices Services { get; }

      /// <summary>Jenis dokumen yang sedang diproses.</summary>
      string DocType { get; }

      /// <summary>Kunci dokumen yang sedang diproses, dalam bentuk bakunya.</summary>
      string DocKey { get; }

      /// <summary>Id request yang sedang diproses.</summary>
      string ApprovalRequestId { get; }

      /// <summary>Pengaju request ini.</summary>
      string RequesterId { get; }


      /// <summary>
      /// Usulan perubahan yang dibawa request ini, per entitas. Setelah usulannya diterapkan, kunci entitas
      /// baru sudah berisi kunci yang sebenarnya.
      /// </summary>
      IReadOnlyList<ApprovalDataItem> Items { get; }
   }

   /// <summary>
   /// Seluruh alur persetujuan yang terdaftar di aplikasi, dibekukan sejak aplikasi dibangun.
   /// </summary>
   /// <remarks>
   /// Satu-satunya tempat engine mencari tahu apa yang dideklarasikan sebuah jenis dokumen. Jenis
   /// dokumen yang tidak ada di sini tidak punya alur, dan request untuknya ditolak.
   /// </remarks>
   public class ApprovalRegistry
   {
      private readonly Dictionary<string, ApprovalFlowDeclaration> _byDocType;

      internal ApprovalRegistry(IEnumerable<ApprovalFlowDeclaration> flows) {
         _byDocType = flows.ToDictionary(r => r.DocType, StringComparer.OrdinalIgnoreCase);
      }

      /// <summary>Seluruh alur yang terdaftar.</summary>
      public IReadOnlyCollection<ApprovalFlowDeclaration> Flows => _byDocType.Values;

      /// <summary>Jenis dokumen yang punya alur persetujuan.</summary>
      public IReadOnlyCollection<string> DocTypes => _byDocType.Keys;

      /// <summary>Alur sebuah jenis dokumen, atau kosong kalau jenis itu tidak punya alur.</summary>
      /// <param name="docType">Jenis dokumen yang dicari.</param>
      public ApprovalFlowDeclaration? Find(string docType) =>
         _byDocType.GetValueOrDefault(docType);

      /// <summary>Alur sebuah jenis dokumen.</summary>
      /// <param name="docType">Jenis dokumen yang dicari.</param>
      /// <exception cref="ActionException">
      /// Dilempar kalau jenis dokumen itu tidak punya alur persetujuan - biasanya karena modulnya tidak
      /// terpasang, atau jenis dokumennya salah tulis.
      /// </exception>
      public ApprovalFlowDeclaration Get(string docType) =>
         Find(docType) ?? throw new ActionException(
            $"Document type '{docType}' has no approval flow registered.", 404);
   }
}
