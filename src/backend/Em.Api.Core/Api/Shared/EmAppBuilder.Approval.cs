using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Em.Api.Core;
using Em.Api.Core.Approval;
using Em.Api.Core.Hub;
using Em.Api.Core.Storage;
using Em.Shared;

namespace Em.Api.Shared
{
   public partial class EmAppBuilder
   {
      internal List<ApprovalFlowDeclaration> ApprovalFlows { get; } = [];

      // null = no binary storage configured. Kept as written in the application's startup code;
      // turning it into an absolute path waits for BuildApp, where the content root is known.
      internal string? BinaryStorageRootPath { get; private set; }

      /// <summary>
      /// Nama claim yang diberikan setiap jenis dokumen kepada pembacanya - hak melihat request tanpa
      /// ikut memutuskannya. Nama jenis dokumennya ikut disebut, supaya satu modul yang memiliki
      /// beberapa jenis dokumen tetap punya satu claim pembaca per jenis.
      /// </summary>
      /// <param name="docType">Jenis dokumennya.</param>
      public static string ApprovalViewClaimName(string docType) => $"View {docType}";

      /// <summary>
      /// Mendaftarkan alur persetujuan sebuah jenis dokumen: dokumennya sudah ada, dan request menjadi
      /// gerbang bagi statusnya. Dipakai dokumen transaksi yang perlu ditandatangani beberapa pihak.
      /// </summary>
      /// <typeparam name="TServices">
      /// Service modul pemilik dokumennya - class implementasinya, yang bertanda <c>[Module]</c>, karena
      /// dari situlah nama modul claim-claim alurnya diambil.
      /// </typeparam>
      /// <typeparam name="TKey">
      /// Record kunci dokumennya, setiap bagiannya bertanda <c>KeyPart</c>. Handler modul menerima
      /// record ini apa adanya; engine yang menerjemahkannya ke bentuk tersimpan dan kembali.
      /// </typeparam>
      /// <param name="docType">
      /// Jenis dokumennya, sesuai daftar jenis dokumen aplikasi. Jenis yang tidak terdaftar di sana
      /// ditolak database saat request pertama dibuat.
      /// </param>
      /// <param name="flow">Callback yang menuliskan alurnya.</param>
      /// <remarks>
      /// Claim setiap langkah dan claim pembacanya didaftarkan otomatis di sini, pada modul
      /// <typeparamref name="TServices"/>, supaya namanya seragam dan tidak perlu didaftarkan dua kali.
      /// Tipe <typeparamref name="TServices"/> sendiri ikut didaftarkan ke DI, karena engine perlu
      /// menyerahkannya ke handler modul saat alur ini berjalan.
      /// </remarks>
      /// <exception cref="ArgumentException">
      /// Dilempar kalau <paramref name="docType"/> kosong atau sudah punya alur.
      /// </exception>
      public void AddDocumentApproval<TServices, TKey>(string docType,
         Action<ApprovalFlowBuilder<TServices, TKey>> flow)
         where TServices : ServicesBase, IServices
         where TKey : notnull {
         ArgumentNullException.ThrowIfNull(flow);
         EnsureDocTypeIsFree(docType);

         var declaration = new ApprovalDocumentFlowDeclaration<TServices, TKey> {
            DocType = docType,
            ModuleName = ModuleAttribute.ResolveName(typeof(TServices)),
            ViewClaim = ClaimAction.Create<TServices>(ApprovalViewClaimName(docType))
         };

         flow(new ApprovalFlowBuilder<TServices, TKey>(declaration, ClaimAction.Create<TServices>));

         if (declaration.Levels.Count == 0) {
            throw new ArgumentException(
               $"Approval flow for '{docType}' declares no level, so nothing could ever be decided.",
               nameof(flow));
         }

         // A step without a place to print its signature on a document that has a PDF would mean a
         // signature nobody can see, and that is a mistake of the declaration - so it is refused here,
         // while the application is being built, rather than at the first submit.
         if (declaration.Pdf is not null) {
            var slotless = declaration.Steps.FirstOrDefault(r => r.Slot is null);
            if (slotless is not null) {
               throw new ArgumentException(
                  $"Step '{slotless.Name}' of the approval flow for '{docType}' has no slot, but the flow has a PDF. " +
                  "Give the step a slot, or turn the approval sheet on so its signature has a place to go.",
                  nameof(flow));
            }
         }

         // The requester signs the first step automatically, and that signature has no payload to carry
         // input, so a step that asks for input cannot be the one that comes first.
         if (declaration.Steps.FirstOrDefault() is { Input: not null } inputFirst) {
            throw new ArgumentException(
               $"Step '{inputFirst.Name}' of the approval flow for '{docType}' comes first and is signed by the requester at submission, so it cannot ask for input.",
               nameof(flow));
         }

         // Both key translation and the flow's own validity are checked now, so a key type that
         // declares no part fails while the application is being built.
         ApprovalKey.GetPartNames(typeof(TKey));
         EnsureDistinctFromNamesExist(declaration);

         Register<TServices>(declaration);
      }

      /// <summary>
      /// Mendaftarkan alur usulan perubahan data: data usulannya tinggal di request dan baru diterapkan
      /// setelah disetujui. Dipakai perubahan data induk, tempat tabel aslinya belum boleh berubah
      /// selama usulan masih menunggu.
      /// </summary>
      /// <typeparam name="TServices">
      /// Service modul pemilik datanya - class implementasinya, yang bertanda <c>[Module]</c>.
      /// </typeparam>
      /// <param name="docType">Jenis dokumennya, sesuai daftar jenis dokumen aplikasi.</param>
      /// <param name="approveClaim">
      /// Nama claim yang memberi hak menyetujui usulan jenis ini, ditulis tanpa nama modulnya.
      /// Pemegangnya juga menyimpan perubahannya sendiri tanpa menunggu persetujuan siapa pun.
      /// </param>
      /// <param name="flow">Callback yang menuliskan entitas-entitasnya dan hook-nya.</param>
      /// <exception cref="ArgumentException">
      /// Dilempar kalau <paramref name="docType"/> kosong atau sudah punya alur, atau kalau alurnya
      /// tidak mendaftarkan satu entitas pun.
      /// </exception>
      public void AddDataApproval<TServices>(string docType, string approveClaim,
         Action<ApprovalDataFlowBuilder<TServices>> flow)
         where TServices : ServicesBase, IServices {
         ArgumentNullException.ThrowIfNull(flow);
         EnsureDocTypeIsFree(docType);

         var declaration = new ApprovalDataFlowDeclaration<TServices> {
            DocType = docType,
            ModuleName = ModuleAttribute.ResolveName(typeof(TServices)),
            ViewClaim = ClaimAction.Create<TServices>(ApprovalViewClaimName(docType)),
            ApproveClaim = ClaimAction.Create<TServices>(approveClaim)
         };

         flow(new ApprovalDataFlowBuilder<TServices>(declaration));

         if (declaration.Entities.Count == 0) {
            throw new ArgumentException(
               $"Data approval flow for '{docType}' declares no entity, so no change could ever be proposed.",
               nameof(flow));
         }

         Register<TServices>(declaration);
      }

      /// <summary>
      /// Menyalakan penyimpanan isi berkas di folder pada mesin server. Tidak ada alamat publik ke
      /// isinya: yang mengambilnya adalah action pemakainya, yang lebih dulu memeriksa hak pemanggil.
      /// </summary>
      /// <param name="rootPath">
      /// Folder tempat isinya disimpan. Path absolut dipakai apa adanya; path relatif (termasuk
      /// <c>./...</c>) dihitung dari folder konten aplikasi. Foldernya dibuat sendiri saat startup kalau
      /// belum ada.
      /// </param>
      /// <exception cref="ArgumentException">Dilempar kalau <paramref name="rootPath"/> kosong.</exception>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau penyimpanan berkas sudah dinyalakan sebelumnya - satu aplikasi hanya punya satu.
      /// </exception>
      public void AddLocalBinaryStorage(string rootPath) {
         if (string.IsNullOrWhiteSpace(rootPath)) {
            throw new ArgumentException("The binary storage root path must not be empty.", nameof(rootPath));
         }

         if (BinaryStorageRootPath is not null) {
            throw new InvalidOperationException(
               $"Binary storage is already enabled for '{BinaryStorageRootPath}'; " +
               $"'{nameof(AddLocalBinaryStorage)}' may only be called once.");
         }

         BinaryStorageRootPath = rootPath;
      }

      /// <summary>
      /// Mendaftarkan satu sumber daftar pekerjaan user aktif. Daftar pekerjaan di baris judul aplikasi
      /// adalah gabungan semua sumber yang terdaftar, jadi jenis pekerjaan baru ditambahkan di sini -
      /// bukan dengan mengubah daftarnya.
      /// </summary>
      /// <typeparam name="T">Sumbernya.</typeparam>
      /// <remarks>
      /// Aman dipanggil berulang kali untuk tipe yang sama: yang terdaftar tetap satu.
      /// </remarks>
      public void AddHubTaskSource<T>() where T : class, IHubTaskSource {
         Services.TryAddEnumerable(ServiceDescriptor.Scoped<IHubTaskSource, T>());
      }

      private void Register<TServices>(ApprovalFlowDeclaration declaration)
         where TServices : ServicesBase, IServices {
         // The engine hands a module its own service when the flow runs, so the implementation type
         // has to be resolvable - the interface registration that AddService makes is not enough,
         // because the declaration names the implementation.
         Services.TryAddScoped<TServices>();

         foreach (var claim in declaration.Claims) {
            AddClaimIfMissing(claim);
         }

         ApprovalFlows.Add(declaration);
      }

      private void EnsureDocTypeIsFree(string docType) {
         if (string.IsNullOrWhiteSpace(docType)) {
            throw new ArgumentException("A document type must not be empty.", nameof(docType));
         }

         if (ApprovalFlows.Any(r => string.Equals(r.DocType, docType, StringComparison.OrdinalIgnoreCase))) {
            throw new ArgumentException(
               $"Document type '{docType}' already has an approval flow. One document type has one flow.",
               nameof(docType));
         }
      }

      private static void EnsureDistinctFromNamesExist<TServices, TKey>(
         ApprovalDocumentFlowDeclaration<TServices, TKey> declaration)
         where TServices : ServicesBase, IServices
         where TKey : notnull {
         var names = declaration.Steps.Select(r => r.Name).ToArray();

         foreach (var step in declaration.Steps) {
            if (step.DistinctFrom is null) continue;

            if (!names.Contains(step.DistinctFrom, StringComparer.OrdinalIgnoreCase)) {
               throw new ArgumentException(
                  $"Step '{step.Name}' of the approval flow for '{declaration.DocType}' must differ from " +
                  $"'{step.DistinctFrom}', but that flow has no step by that name.");
            }
         }
      }

      // Registered claims of an approval flow come from the declaration, not from a module author
      // typing them twice, so a name that is already there is the same claim and not a mistake -
      // which is exactly the case when one module owns two document types that share a step name.
      private void AddClaimIfMissing(ClaimAction claim) {
         if (ClaimActions.Any(r => string.Equals(r.Key, claim.Key, StringComparison.OrdinalIgnoreCase))) {
            return;
         }

         ClaimActions.Add(claim);
      }
   }
}
