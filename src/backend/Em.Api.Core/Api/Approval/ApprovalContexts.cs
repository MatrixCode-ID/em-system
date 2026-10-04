using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Keterangan yang diserahkan engine ke handler modul tentang sebuah request dokumen.
   /// </summary>
   /// <typeparam name="TServices">Service modul pemilik dokumen.</typeparam>
   /// <typeparam name="TKey">Record kunci dokumennya.</typeparam>
   internal class ApprovalContext<TServices, TKey> : IApprovalContext<TServices, TKey>
   {
      public required TServices Services { get; init; }
      public required TKey DocKey { get; init; }
      public required string DocVersion { get; init; }
      public required string ApprovalRequestId { get; init; }
      public required string RequesterId { get; init; }
      public required IApprovalUserLookup Users { get; init; }
   }

   /// <summary>
   /// Keterangan yang diserahkan engine ke handler modul tentang satu langkah sebuah request dokumen.
   /// </summary>
   /// <typeparam name="TServices">Service modul pemilik dokumen.</typeparam>
   /// <typeparam name="TKey">Record kunci dokumennya.</typeparam>
   internal class ApprovalStepContext<TServices, TKey> : ApprovalContext<TServices, TKey>,
      IApprovalStepContext<TServices, TKey>
   {
      public required string StepName { get; init; }
      public string? SignerId { get; init; }
   }

   /// <summary>
   /// Keterangan yang diserahkan engine ke handler modul dalam alur usulan perubahan data.
   /// </summary>
   /// <typeparam name="TServices">Service modul pemilik datanya.</typeparam>
   internal class ApprovalDataContext<TServices> : IApprovalDataContext<TServices>
      where TServices : ServicesBase, IServices
   {
      public required TServices Services { get; init; }
      public required string DocType { get; init; }
      public required string DocKey { get; init; }
      public required string ApprovalRequestId { get; init; }
      public required string RequesterId { get; init; }
      public required IApprovalUserLookup Users { get; init; }
      public required IReadOnlyList<ApprovalDataItem> Items { get; init; }

      /// <summary>Membangun keterangan untuk handler modul dari bahan yang dipegang engine.</summary>
      public static ApprovalDataContext<TServices> From(ApprovalRunScope scope) => new() {
         Services = ApprovalModuleService.Resolve<TServices>(scope.Engine, scope.Provider),
         DocType = scope.Request.cApprovalRequestDocType,
         DocKey = scope.Request.cApprovalRequestDocKey,
         ApprovalRequestId = scope.Request.cApprovalRequestId,
         RequesterId = scope.Request.cApprovalRequestRequesterId,
         Users = scope.Users,
         Items = scope.Items ?? []
      };
   }

   /// <summary>
   /// Penyedia service modul untuk engine approval: mengambilnya dari DI lalu mengisi keterangan request
   /// yang biasanya diisi gerbang.
   /// </summary>
   /// <remarks>
   /// Handler modul dipanggil dari dalam action engine, bukan dari action modul itu sendiri, jadi service
   /// yang baru diambil dari DI belum tahu siapa pemanggilnya maupun kapan pekerjaannya harus berhenti.
   /// Tanpa pengisian di sini, handler yang membaca <c>Request</c> akan melihat "tidak ada request" dan
   /// menolak setiap pemeriksaan haknya sendiri.
   /// </remarks>
   internal static class ApprovalModuleService
   {
      /// <summary>
      /// Mengambil service modul dan mengisi keterangan request-nya dari service engine yang memanggil.
      /// </summary>
      /// <typeparam name="TServices">Service modul yang diambil.</typeparam>
      /// <param name="engine">Service engine yang sedang berjalan - sumber keterangan request-nya.</param>
      /// <param name="provider">Penyedia service permintaan ini.</param>
      internal static TServices Resolve<TServices>(ServicesBase engine, IServiceProvider provider)
         where TServices : ServicesBase, IServices {
         var module = provider.GetRequiredService<TServices>();

         module.App = engine.App;
         module.HttpContext = engine.HttpContext;
         module.Request = engine.Request;
         module.AbortToken = engine.AbortToken;
         module.Logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(TServices).Name);

         return module;
      }
   }
}
