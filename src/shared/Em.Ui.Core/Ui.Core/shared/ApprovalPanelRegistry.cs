using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Seluruh panel dan layar dokumen yang didaftarkan modul ke layar approval, dibekukan sejak
   /// aplikasi dibangun.
   /// </summary>
   /// <remarks>
   /// Layar approval membacanya untuk tahu apa yang perlu dipasang bagi jenis dokumen yang sedang
   /// dibuka. Selalu tersedia - juga saat tidak ada satu panel pun terdaftar - supaya layarnya tidak
   /// perlu tahu bedanya.
   /// </remarks>
   public class ApprovalPanelRegistry
   {
      private readonly List<ApprovalStepPanelRegistration> _stepPanels = [];
      private readonly List<ApprovalInfoPanelRegistration> _infoPanels = [];
      private readonly List<ApprovalDocumentOpenerRegistration> _documentOpeners = [];

      /// <summary>Panel isian per langkah.</summary>
      public IReadOnlyList<ApprovalStepPanelRegistration> StepPanels => _stepPanels;

      /// <summary>Kartu informasi, sudah terurut sesuai urutan yang diminta modul.</summary>
      public IReadOnlyList<ApprovalInfoPanelRegistration> InfoPanels => _infoPanels;

      /// <summary>Cara membuka layar dokumen sebuah jenis dokumen.</summary>
      public IReadOnlyList<ApprovalDocumentOpenerRegistration> DocumentOpeners => _documentOpeners;

      /// <summary>
      /// Panel isian sebuah langkah, atau kosong kalau langkah itu tidak punya panel.
      /// </summary>
      /// <param name="docType">Jenis dokumennya.</param>
      /// <param name="stepName">Nama langkahnya.</param>
      public ApprovalStepPanelRegistration? FindStepPanel(string docType, string stepName) =>
         _stepPanels.FirstOrDefault(r =>
            string.Equals(r.DocType, docType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.StepName, stepName, StringComparison.OrdinalIgnoreCase));

      /// <summary>
      /// Kartu informasi yang berlaku untuk sebuah langkah, terurut. Kartu yang tidak menyebut langkah
      /// apa pun berlaku untuk semua langkah jenis dokumen itu.
      /// </summary>
      /// <param name="docType">Jenis dokumennya.</param>
      /// <param name="stepName">
      /// Langkah yang sedang dibuka, atau kosong untuk mengambil kartu yang berlaku untuk semua langkah
      /// saja.
      /// </param>
      public IReadOnlyList<ApprovalInfoPanelRegistration> FindInfoPanels(string docType, string? stepName) =>
         [.. _infoPanels
            .Where(r => string.Equals(r.DocType, docType, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.Steps.Count == 0 ||
                        (stepName is not null && r.Steps.Contains(stepName, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(r => r.Order)];

      /// <summary>
      /// Cara membuka layar dokumen sebuah jenis dokumen, atau kosong kalau modulnya tidak
      /// mendaftarkannya.
      /// </summary>
      /// <param name="docType">Jenis dokumennya.</param>
      public ApprovalDocumentOpenerRegistration? FindDocumentOpener(string docType) =>
         _documentOpeners.FirstOrDefault(r =>
            string.Equals(r.DocType, docType, StringComparison.OrdinalIgnoreCase));

      /// <summary>
      /// Menambahkan panel isian satu langkah. Dipanggil saat aplikasi dibangun, lewat
      /// <c>EmAppBuilder.AddApprovalStepPanel</c>; sesudah itu katalognya tidak berubah lagi.
      /// </summary>
      /// <param name="registration">Panel yang didaftarkan.</param>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau langkah itu sudah punya panel isian.
      /// </exception>
      // Bukan "internal": pemanggilnya tinggal di assembly UI yang berbeda, dan repo ini sengaja
      // tidak memakai InternalsVisibleTo di mana pun. Batasnya karena itu konvensi, bukan penegakan
      // compiler - katalog ini hanya diisi selama aplikasi dibangun.
      public void Add(ApprovalStepPanelRegistration registration) {
         if (FindStepPanel(registration.DocType, registration.StepName) is not null) {
            throw new InvalidOperationException(
               $"Step '{registration.StepName}' of document type '{registration.DocType}' already has an input panel. " +
               "A step takes one panel; put several controls inside it instead.");
         }

         _stepPanels.Add(registration);
      }

      /// <summary>
      /// Menambahkan satu kartu informasi. Dipanggil saat aplikasi dibangun, lewat
      /// <c>EmAppBuilder.AddApprovalInfoPanel</c>.
      /// </summary>
      /// <param name="registration">Kartu yang didaftarkan.</param>
      public void Add(ApprovalInfoPanelRegistration registration) => _infoPanels.Add(registration);

      /// <summary>
      /// Menambahkan cara membuka layar dokumen sebuah jenis dokumen. Dipanggil saat aplikasi
      /// dibangun, lewat <c>EmAppBuilder.AddApprovalDocumentOpener</c>.
      /// </summary>
      /// <param name="registration">Cara membuka dokumen yang didaftarkan.</param>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau jenis dokumen itu sudah punya cara membuka dokumennya.
      /// </exception>
      public void Add(ApprovalDocumentOpenerRegistration registration) {
         if (FindDocumentOpener(registration.DocType) is not null) {
            throw new InvalidOperationException(
               $"Document type '{registration.DocType}' already has a way to open its document.");
         }

         _documentOpeners.Add(registration);
      }
   }

   /// <summary>Panel isian satu langkah, seperti yang didaftarkan modul.</summary>
   /// <param name="DocType">Jenis dokumennya.</param>
   /// <param name="StepName">Langkah yang panelnya dipasang.</param>
   /// <param name="ViewType">Tipe tampilan panelnya.</param>
   /// <param name="ViewModelType">
   /// Tipe view model panelnya, yang mengimplementasikan <see cref="IApprovalPanel"/>.
   /// </param>
   public record ApprovalStepPanelRegistration(string DocType, string StepName, Type ViewType, Type ViewModelType);

   /// <summary>Kartu informasi, seperti yang didaftarkan modul.</summary>
   /// <param name="DocType">Jenis dokumen tempat kartu ini tampil.</param>
   /// <param name="ViewType">Tipe tampilan kartunya.</param>
   /// <param name="Steps">
   /// Langkah-langkah yang kartunya tampil. Kosong berarti kartunya tampil di semua langkah jenis
   /// dokumen itu.
   /// </param>
   /// <param name="Input">
   /// Cara menyusun keterangan awal kartunya dari request yang sedang dibuka - misalnya mengambil
   /// identitas pihak yang datanya ditampilkan kartu itu. Kosong berarti kartunya tidak butuh apa-apa.
   /// </param>
   /// <param name="Claim">
   /// Claim yang harus dipegang user supaya kartunya tampil, atau kosong kalau kartunya terbuka bagi
   /// siapa pun yang boleh melihat request itu. Dipakai kartu yang menampilkan data milik modul lain.
   /// </param>
   /// <param name="Order">Urutan kartunya; yang lebih kecil tampil lebih dulu.</param>
   public record ApprovalInfoPanelRegistration(string DocType, Type ViewType, IReadOnlyList<string> Steps,
      Func<IApprovalPanelHost, object?>? Input, ClaimAction? Claim, int Order);

   /// <summary>Cara membuka layar dokumen sebuah jenis dokumen dari layar approval.</summary>
   /// <param name="DocType">Jenis dokumennya.</param>
   /// <param name="NavigationName">Nama navigasi layar dokumennya.</param>
   /// <param name="Parameter">
   /// Cara menyusun parameter navigasinya dari request yang sedang dibuka. Kosong berarti layarnya
   /// dibuka tanpa parameter.
   /// </param>
   /// <remarks>
   /// Layar dokumennya dibuka untuk dibaca, bukan untuk diubah: dokumen yang sedang menunggu keputusan
   /// terkunci.
   /// </remarks>
   public record ApprovalDocumentOpenerRegistration(string DocType, string NavigationName,
      Func<IApprovalPanelHost, object?>? Parameter);
}
