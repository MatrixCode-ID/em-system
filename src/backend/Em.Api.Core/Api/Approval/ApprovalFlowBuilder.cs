using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Tempat modul menulis alur persetujuan sebuah jenis dokumen: level-levelnya, PDF-nya, kolom
   /// ringkasannya, dan hal-hal yang terjadi di sepanjang alurnya.
   /// </summary>
   /// <typeparam name="TServices">Service modul pemilik dokumen.</typeparam>
   /// <typeparam name="TKey">Record kunci dokumennya, setiap bagiannya bertanda <c>KeyPart</c>.</typeparam>
   /// <remarks>
   /// Setiap method mengembalikan builder-nya sendiri, jadi deklarasinya boleh ditulis bersambung
   /// maupun baris per baris.
   /// </remarks>
   /// <example>
   /// <code>
   /// builder.AddDocumentApproval&lt;MyServices, MyKey&gt;(MyApproval.DocType, flow => {
   ///    flow.Level(1, l => l.Step(MyApproval.PreparedBy, ApprovalSlot.At(15, 245),
   ///       signers: ctx => Task.FromResult&lt;IReadOnlyList&lt;string&gt;&gt;([ctx.RequesterId])));
   ///    flow.Level(2, l => l.Step(MyApproval.CheckedBy, ApprovalSlot.At(52, 245),
   ///       distinctFrom: MyApproval.PreparedBy));
   ///    flow.Pdf(ctx => ctx.Services.GetReport_MyDoc(ctx.DocKey.Number, ctx.DocVersion));
   /// });
   /// </code>
   /// </example>
   public class ApprovalFlowBuilder<TServices, TKey>
      where TServices : ServicesBase, IServices
      where TKey : notnull
   {
      private readonly ApprovalDocumentFlowDeclaration<TServices, TKey> _declaration;
      private readonly Func<string, ClaimAction> _claimFactory;

      internal ApprovalFlowBuilder(ApprovalDocumentFlowDeclaration<TServices, TKey> declaration,
         Func<string, ClaimAction> claimFactory) {
         _declaration = declaration;
         _claimFactory = claimFactory;
      }

      /// <summary>
      /// Menambahkan satu level ke alurnya. Level dijalani dari nomor terkecil, dan level berikutnya
      /// baru mulai setelah seluruh langkah level ini disetujui.
      /// </summary>
      /// <param name="level">Nomor levelnya. Harus belum dipakai level lain.</param>
      /// <param name="steps">Callback yang menuliskan langkah-langkah level ini.</param>
      /// <param name="onCompleted">
      /// Dijalankan di dalam transaksi begitu seluruh langkah level ini disetujui, untuk tonggak di
      /// tengah alur - status dokumen yang berubah sebelum langkah terakhir. Kegagalannya mengembalikan
      /// keputusan yang memicunya.
      /// </param>
      /// <exception cref="ArgumentException">Dilempar kalau nomor levelnya sudah dipakai.</exception>
      public ApprovalFlowBuilder<TServices, TKey> Level(int level,
         Action<ApprovalLevelBuilder<TServices, TKey>> steps,
         Func<IApprovalContext<TServices, TKey>, Task>? onCompleted = null) {
         ArgumentNullException.ThrowIfNull(steps);

         if (_declaration.Levels.Any(r => r.Level == level)) {
            throw new ArgumentException(
               $"Approval flow for '{_declaration.DocType}' already declares level {level}. " +
               "Several steps running at once belong in one Level call, not in two.", nameof(level));
         }

         var declaration = new ApprovalLevelDeclaration<TServices, TKey> {
            Level = level,
            OnCompleted = onCompleted
         };

         steps(new ApprovalLevelBuilder<TServices, TKey>(declaration, _declaration.DocType, _claimFactory));
         _declaration.Levels.Add(declaration);
         return this;
      }

      /// <summary>
      /// Menyatakan bahwa jenis dokumen ini punya PDF, beserta cara mengambilnya. PDF-nya diambil sekali
      /// saat pengajuan lalu dibekukan, sehingga penanda tangan selalu melihat isi yang sama.
      /// </summary>
      /// <param name="render">
      /// Cara mengambil isi PDF dokumennya. Biasanya action laporan milik modul itu sendiri.
      /// </param>
      public ApprovalFlowBuilder<TServices, TKey> Pdf(Func<IApprovalContext<TServices, TKey>, Task<Stream>> render) {
         ArgumentNullException.ThrowIfNull(render);
         _declaration.Pdf = render;
         return this;
      }

      /// <summary>
      /// Menyatakan bahwa tata letak PDF dokumen ini berubah menurut isinya - jumlah baris, bagian yang
      /// tampil, halaman yang terpecah - sehingga posisi kotak yang dideklarasikan hanya berlaku untuk
      /// satu bentuk dokumen dan harus dicari ulang pada PDF dasar tiap request.
      /// </summary>
      /// <param name="locate">
      /// Menerima salinan PDF dasar yang baru dibuat, mengembalikan pemetaan dari posisi yang
      /// dideklarasikan ke posisi sebenarnya pada PDF itu. Pemetaan yang mengembalikan <c>null</c> berarti
      /// kotak itu tidak ada pada dokumen ini dan tidak digambar. Salinannya boleh dibaca sampai habis.
      /// </param>
      public ApprovalFlowBuilder<TServices, TKey> PdfLayout(
         Func<IApprovalContext<TServices, TKey>, Stream, Task<Func<ApprovalSlot, ApprovalSlot?>>> locate) {
         ArgumentNullException.ThrowIfNull(locate);
         _declaration.PdfLayout = locate;
         return this;
      }

      /// <summary>
      /// Menyatakan kolom ringkasan milik modul yang tampil di daftar request, dan cara menghitungnya.
      /// Dihitung saat pengajuan lalu ikut tersimpan, jadi daftar request bisa menyaring dan
      /// mengurutkannya tanpa memuat dokumennya.
      /// </summary>
      /// <param name="summary">
      /// Cara menghitung ringkasannya, berupa pasangan nama kolom dan nilainya.
      /// </param>
      public ApprovalFlowBuilder<TServices, TKey> Summary(
         Func<IApprovalContext<TServices, TKey>, Task<IReadOnlyDictionary<string, string?>>> summary) {
         ArgumentNullException.ThrowIfNull(summary);
         _declaration.Summary = summary;
         return this;
      }

      /// <summary>
      /// Menyatakan bahwa keputusan jenis dokumen ini hanya boleh diambil setelah dokumennya dibuka,
      /// sehingga ia tidak bisa disetujui berbondong-bondong dari daftar. Dipakai dokumen yang memang
      /// harus dibaca dulu sebelum ditandatangani.
      /// </summary>
      public ApprovalFlowBuilder<TServices, TKey> RequireOpen() {
         _declaration.RequireOpen = true;
         return this;
      }

      /// <summary>
      /// Menyalakan halaman tambahan pada PDF-nya, berisi tabel seluruh langkah beserta tanda
      /// tangannya. Dipakai dokumen yang kotak tanda tangannya tidak cukup untuk seluruh alurnya.
      /// </summary>
      public ApprovalFlowBuilder<TServices, TKey> ApprovalSheet() {
         _declaration.ApprovalSheet = true;
         return this;
      }

      /// <summary>Menyetel apa yang dijalankan setelah pengajuan tersimpan.</summary>
      /// <param name="hook">Yang dijalankan. Kegagalannya tidak membatalkan pengajuan.</param>
      public ApprovalFlowBuilder<TServices, TKey> OnSubmitted(Func<IApprovalContext<TServices, TKey>, Task> hook) {
         _declaration.OnSubmitted = hook;
         return this;
      }

      /// <summary>
      /// Menyetel pemeriksaan sebuah keputusan sebelum ditulis, di dalam transaksinya. Di sinilah isian
      /// per langkah divalidasi ulang di server.
      /// </summary>
      /// <param name="hook">Yang dijalankan. Melempar berarti keputusan itu gagal.</param>
      public ApprovalFlowBuilder<TServices, TKey> OnSigning(
         Func<IApprovalStepContext<TServices, TKey>, ApprovalDecision, Task> hook) {
         _declaration.OnSigning = hook;
         return this;
      }

      /// <summary>
      /// Menyetel penulisan akibat sebuah tanda tangan ke dokumennya, di dalam transaksi yang sama.
      /// </summary>
      /// <param name="hook">Yang dijalankan. Melempar berarti tanda tangannya ikut batal.</param>
      public ApprovalFlowBuilder<TServices, TKey> OnSigned(
         Func<IApprovalStepContext<TServices, TKey>, ApprovalDecision, Task> hook) {
         _declaration.OnSigned = hook;
         return this;
      }

      /// <summary>Menyetel apa yang dijalankan di dalam transaksi saat request akan selesai.</summary>
      /// <param name="hook">Yang dijalankan. Melempar berarti keputusan terakhirnya dibatalkan.</param>
      public ApprovalFlowBuilder<TServices, TKey> OnFinishing(Func<IApprovalContext<TServices, TKey>, Task> hook) {
         _declaration.OnFinishing = hook;
         return this;
      }

      /// <summary>Menyetel apa yang dijalankan setelah request selesai tersimpan.</summary>
      /// <param name="hook">Yang dijalankan. Kegagalannya tidak merusak data.</param>
      public ApprovalFlowBuilder<TServices, TKey> OnFinished(Func<IApprovalContext<TServices, TKey>, Task> hook) {
         _declaration.OnFinished = hook;
         return this;
      }

      /// <summary>Menyetel apa yang dijalankan di dalam transaksi saat sebuah langkah ditolak.</summary>
      /// <param name="hook">Yang dijalankan. Melempar berarti penolakannya dibatalkan.</param>
      public ApprovalFlowBuilder<TServices, TKey> OnRejecting(Func<IApprovalContext<TServices, TKey>, Task> hook) {
         _declaration.OnRejecting = hook;
         return this;
      }

      /// <summary>Menyetel apa yang dijalankan setelah penolakan tersimpan.</summary>
      /// <param name="hook">Yang dijalankan. Kegagalannya tidak merusak data.</param>
      public ApprovalFlowBuilder<TServices, TKey> OnRejected(Func<IApprovalContext<TServices, TKey>, Task> hook) {
         _declaration.OnRejected = hook;
         return this;
      }

      /// <summary>
      /// Menyetel apa yang dijalankan di dalam transaksi saat sebuah request ditarik kembali - termasuk
      /// request yang sudah selesai seluruhnya. Di sinilah modul mencabut status yang sudah ditulis, dan
      /// di sinilah ia boleh menolak penarikan dengan melempar kalau dokumennya sudah diproses lebih
      /// lanjut.
      /// </summary>
      /// <param name="hook">
      /// Yang dijalankan, menerima tahap request itu sebelum ditarik - dari situ modul tahu apakah
      /// status dokumennya memang pernah berubah.
      /// </param>
      public ApprovalFlowBuilder<TServices, TKey> OnReinstating(
         Func<IApprovalContext<TServices, TKey>, ApprovalStage, Task> hook) {
         _declaration.OnReinstating = hook;
         return this;
      }
   }

   /// <summary>
   /// Tempat modul menulis langkah-langkah satu level. Beberapa langkah di level yang sama berjalan
   /// bersamaan, dan semuanya harus disetujui sebelum level berikutnya mulai.
   /// </summary>
   /// <typeparam name="TServices">Service modul pemilik dokumen.</typeparam>
   /// <typeparam name="TKey">Record kunci dokumennya.</typeparam>
   public class ApprovalLevelBuilder<TServices, TKey> where TKey : notnull
   {
      private readonly ApprovalLevelDeclaration<TServices, TKey> _declaration;
      private readonly string _docType;
      private readonly Func<string, ClaimAction> _claimFactory;

      internal ApprovalLevelBuilder(ApprovalLevelDeclaration<TServices, TKey> declaration, string docType,
         Func<string, ClaimAction> claimFactory) {
         _declaration = declaration;
         _docType = docType;
         _claimFactory = claimFactory;
      }

      /// <summary>
      /// Menambahkan satu langkah ke level ini.
      /// </summary>
      /// <param name="name">
      /// Nama langkahnya, sekaligus nama claim yang dipersyaratkan untuk memutuskannya dan nama yang
      /// tercetak di PDF. Pakai konstanta, bukan teks langsung: namanya ikut tersimpan di setiap
      /// request.
      /// </param>
      /// <param name="slot">
      /// Di mana tanda tangannya digambar pada PDF. Wajib untuk jenis dokumen yang punya PDF.
      /// </param>
      /// <param name="signers">
      /// Cara menentukan siapa saja penanda tangannya, dibaca dari isi dokumen saat pengajuan. Kosong
      /// berarti langkah ini terbuka bagi semua pemegang claim-nya.
      /// </param>
      /// <param name="strict">
      /// <c>true</c> kalau hanya penanda tangannya sendiri yang boleh menandatangani, sehingga pemegang
      /// claim lain tidak bisa menggantikannya.
      /// </param>
      /// <param name="distinctFrom">
      /// Nama langkah yang penanda tangannya tidak boleh sama dengan penanda tangan langkah ini -
      /// pemisahan peran yang diperiksa saat pengajuan dan sekali lagi saat menandatangani.
      /// </param>
      /// <param name="when">
      /// Cara menentukan langkah ini berlaku atau dilewati, dievaluasi saat pengajuan. Kosong berarti
      /// selalu berlaku.
      /// </param>
      /// <param name="guard">
      /// Cara menentukan langkah ini sudah boleh diputuskan sekarang. Dievaluasi ulang setiap kali layar
      /// dibuka, jadi blokir yang syaratnya sudah terpenuhi terbuka sendiri.
      /// </param>
      /// <param name="input">Isian yang diminta sebelum langkah ini bisa diputuskan.</param>
      /// <exception cref="ArgumentException">
      /// Dilempar kalau nama langkahnya kosong atau sudah dipakai langkah lain di level yang sama.
      /// </exception>
      public ApprovalLevelBuilder<TServices, TKey> Step(string name, ApprovalSlot? slot = null,
         Func<IApprovalContext<TServices, TKey>, Task<IReadOnlyList<string>>>? signers = null,
         bool strict = false,
         string? distinctFrom = null,
         Func<IApprovalContext<TServices, TKey>, Task<bool>>? when = null,
         Func<IApprovalStepContext<TServices, TKey>, Task<ApprovalGuard>>? guard = null,
         IApprovalStepInput<TServices, TKey>? input = null) {
         if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException(
               $"A step of the approval flow for '{_docType}' has an empty name.", nameof(name));
         }

         if (_declaration.Steps.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))) {
            throw new ArgumentException(
               $"Approval flow for '{_docType}' already declares a step named '{name}' at level {_declaration.Level}.",
               nameof(name));
         }

         _declaration.Steps.Add(new ApprovalStepDeclaration<TServices, TKey> {
            Name = name,
            Claim = _claimFactory(name),
            Level = _declaration.Level,
            Order = _declaration.Steps.Count + 1,
            Slot = slot,
            Strict = strict,
            DistinctFrom = distinctFrom,
            Signers = signers,
            When = when,
            Guard = guard,
            Input = input
         });

         return this;
      }
   }

   /// <summary>
   /// Tempat modul menulis alur usulan perubahan data: claim persetujuannya, entitas yang boleh
   /// diusulkan berubah, dan hal-hal yang terjadi saat usulan diputuskan.
   /// </summary>
   /// <typeparam name="TServices">Service modul pemilik datanya.</typeparam>
   public class ApprovalDataFlowBuilder<TServices>
      where TServices : ServicesBase, IServices
   {
      private readonly ApprovalDataFlowDeclaration<TServices> _declaration;

      internal ApprovalDataFlowBuilder(ApprovalDataFlowDeclaration<TServices> declaration) {
         _declaration = declaration;
      }

      /// <summary>
      /// Mendaftarkan satu entitas yang boleh diusulkan berubah, beserta cara memuat nilainya saat ini
      /// dan cara menerapkan usulannya.
      /// </summary>
      /// <typeparam name="TKey">Record kunci entitas ini, setiap bagiannya bertanda <c>KeyPart</c>.</typeparam>
      /// <param name="name">
      /// Nama entitasnya, ditulis dengan awalan nama modulnya supaya tidak bertabrakan antar modul.
      /// Namanya ikut tersimpan di setiap usulan, jadi pakai konstanta.
      /// </param>
      /// <param name="load">
      /// Memuat nilai kolom entitas ini apa adanya saat ini, atau mengembalikan kosong kalau entitasnya
      /// sudah tidak ada. Inilah yang dibandingkan engine dengan nilai yang dicatat waktu pengajuan.
      /// </param>
      /// <param name="apply">
      /// Menerapkan usulannya, di dalam transaksi keputusan. Nilai balikannya kunci entitas setelah
      /// diterapkan - kunci yang baru terbentuk, untuk entitas baru.
      /// </param>
      /// <param name="order">
      /// Urutan penerapan, untuk entitas yang harus diterapkan setelah entitas lain. Yang lebih kecil
      /// diterapkan lebih dulu.
      /// </param>
      /// <exception cref="ArgumentException">
      /// Dilempar kalau nama entitasnya kosong atau sudah dipakai entitas lain di alur ini.
      /// </exception>
      public ApprovalDataFlowBuilder<TServices> Entity<TKey>(string name,
         Func<IApprovalDataContext<TServices>, TKey, Task<IReadOnlyDictionary<string, string?>?>> load,
         Func<IApprovalDataContext<TServices>, ApprovalApplyRequest<TKey>, Task<TKey>> apply,
         int order = 0) where TKey : notnull {
         if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException(
               $"An entity of the data approval flow for '{_declaration.DocType}' has an empty name.", nameof(name));
         }

         ArgumentNullException.ThrowIfNull(load);
         ArgumentNullException.ThrowIfNull(apply);

         if (_declaration.Entities.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))) {
            throw new ArgumentException(
               $"Data approval flow for '{_declaration.DocType}' already declares an entity named '{name}'.",
               nameof(name));
         }

         _declaration.Entities.Add(new ApprovalEntityDeclaration<TServices, TKey> {
            Name = name,
            Order = order,
            Load = load,
            Apply = apply
         });

         return this;
      }

      /// <summary>
      /// Menyatakan kolom ringkasan milik modul yang tampil di daftar request, dan cara menghitungnya.
      /// </summary>
      /// <param name="summary">Cara menghitungnya, berupa pasangan nama kolom dan nilainya.</param>
      public ApprovalDataFlowBuilder<TServices> Summary(
         Func<IApprovalDataContext<TServices>, Task<IReadOnlyDictionary<string, string?>>> summary) {
         ArgumentNullException.ThrowIfNull(summary);
         _declaration.Summary = summary;
         return this;
      }

      /// <summary>Menyetel apa yang dijalankan setelah pengajuan tersimpan.</summary>
      /// <param name="hook">Yang dijalankan. Kegagalannya tidak membatalkan pengajuan.</param>
      public ApprovalDataFlowBuilder<TServices> OnSubmitted(Func<IApprovalDataContext<TServices>, Task> hook) {
         _declaration.OnSubmitted = hook;
         return this;
      }

      /// <summary>
      /// Menyetel apa yang dijalankan di dalam transaksi, setelah seluruh usulan diterapkan dan sebelum
      /// keputusannya tersimpan.
      /// </summary>
      /// <param name="hook">Yang dijalankan. Melempar berarti seluruh penerapannya dibatalkan.</param>
      public ApprovalDataFlowBuilder<TServices> OnFinishing(Func<IApprovalDataContext<TServices>, Task> hook) {
         _declaration.OnFinishing = hook;
         return this;
      }

      /// <summary>Menyetel apa yang dijalankan setelah keputusan tersimpan.</summary>
      /// <param name="hook">Yang dijalankan. Kegagalannya tidak merusak data.</param>
      public ApprovalDataFlowBuilder<TServices> OnFinished(Func<IApprovalDataContext<TServices>, Task> hook) {
         _declaration.OnFinished = hook;
         return this;
      }

      /// <summary>Menyetel apa yang dijalankan di dalam transaksi saat usulan ditolak.</summary>
      /// <param name="hook">Yang dijalankan. Melempar berarti penolakannya dibatalkan.</param>
      public ApprovalDataFlowBuilder<TServices> OnRejecting(Func<IApprovalDataContext<TServices>, Task> hook) {
         _declaration.OnRejecting = hook;
         return this;
      }

      /// <summary>Menyetel apa yang dijalankan setelah penolakan tersimpan.</summary>
      /// <param name="hook">Yang dijalankan. Kegagalannya tidak merusak data.</param>
      public ApprovalDataFlowBuilder<TServices> OnRejected(Func<IApprovalDataContext<TServices>, Task> hook) {
         _declaration.OnRejected = hook;
         return this;
      }
   }
}
