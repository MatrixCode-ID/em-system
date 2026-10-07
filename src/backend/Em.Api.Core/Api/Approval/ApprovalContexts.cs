using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Info the engine hands to the module's handler about a document request.
   /// </summary>
   /// <typeparam name="TServices">Service of the module that owns the document.</typeparam>
   /// <typeparam name="TKey">Key record of the document.</typeparam>
   internal class ApprovalContext<TServices, TKey> : IApprovalContext<TServices, TKey>
   {
      public required TServices Services { get; init; }
      public required TKey DocKey { get; init; }
      public required string DocVersion { get; init; }
      public required string ApprovalRequestId { get; init; }
      public required string RequesterId { get; init; }
   }

   /// <summary>
   /// Info the engine hands to the module's handler about one step of a document request.
   /// </summary>
   /// <typeparam name="TServices">Service of the module that owns the document.</typeparam>
   /// <typeparam name="TKey">Key record of the document.</typeparam>
   internal class ApprovalStepContext<TServices, TKey> : ApprovalContext<TServices, TKey>,
      IApprovalStepContext<TServices, TKey>
   {
      public required string StepName { get; init; }
      public string? SignerId { get; init; }
   }

   /// <summary>
   /// Info the engine hands to the module's handler in a data change proposal flow.
   /// </summary>
   /// <typeparam name="TServices">Service of the module that owns the data.</typeparam>
   internal class ApprovalDataContext<TServices> : IApprovalDataContext<TServices>
      where TServices : ServicesBase, IServices
   {
      public required TServices Services { get; init; }
      public required string DocType { get; init; }
      public required string DocKey { get; init; }
      public required string ApprovalRequestId { get; init; }
      public required string RequesterId { get; init; }
      public required IReadOnlyList<ApprovalDataItem> Items { get; init; }

      /// <summary>Builds the info for the module's handler from the material the engine holds.</summary>
      public static ApprovalDataContext<TServices> From(ApprovalRunScope scope) => new() {
         Services = ApprovalModuleService.Resolve<TServices>(scope.Engine, scope.Provider),
         DocType = scope.Request.cApprovalRequestDocType,
         DocKey = scope.Request.cApprovalRequestDocKey,
         ApprovalRequestId = scope.Request.cApprovalRequestId,
         RequesterId = scope.Request.cApprovalRequestRequesterId,
         Items = scope.Items ?? []
      };
   }

   /// <summary>
   /// Provider of module services for the approval engine: it gets them from DI and then fills the request
   /// info that is normally filled by the gate.
   /// </summary>
   /// <remarks>
   /// A module handler is called from inside an engine action, not from a module action itself, so a
   /// service freshly taken from DI does not yet know who the caller is or when its work must stop.
   /// Without the filling done here, a handler that reads <c>Request</c> would see "no request" and refuse
   /// every one of its own rights checks.
   /// </remarks>
   internal static class ApprovalModuleService
   {
      /// <summary>
      /// Gets a module service and fills its request info from the calling engine service.
      /// </summary>
      /// <typeparam name="TServices">The module service to get.</typeparam>
      /// <param name="engine">The running engine service - the source of its request info.</param>
      /// <param name="provider">The service provider of this call.</param>
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
