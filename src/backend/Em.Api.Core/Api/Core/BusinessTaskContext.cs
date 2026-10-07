using Microsoft.Extensions.Logging;

namespace Em.Api.Core
{
   /// <summary>
   /// Everything the work of a business task may use while it runs. That work runs outside the request
   /// that started it, so anything belonging to the request - <c>Request</c>, <c>AbortToken</c>,
   /// <c>GetService</c>, and the starting service itself - must not be used inside it. The replacements
   /// are here.
   /// </summary>
   public sealed class BusinessTaskContext
   {
      private readonly Action<double?, string> _report;

      internal BusinessTaskContext(CancellationToken cancellationToken, IServiceProvider services, ActionRequest starter,
         ILogger logger, Action<double?, string> report) {
         CancellationToken = cancellationToken;
         Services = services;
         Starter = starter;
         Logger = logger;
         _report = report;
      }

      /// <summary>
      /// Signals when this task is cancelled by the user or when the server is shut down - never because the
      /// starter's request finished. Pass it on to every call that may take long; stopping because of this
      /// token is recorded as cancelled, not failed.
      /// </summary>
      public CancellationToken CancellationToken { get; }

      /// <summary>
      /// The task's own service provider, alive while the task runs. Scoped services (DbContext and the
      /// like) are taken from here. The <see cref="ActionRequest"/> asked for through this provider is
      /// <see cref="Starter"/>.
      /// </summary>
      public IServiceProvider Services { get; }

      /// <summary>
      /// Identity of the user who started this task, as it was when they started it. Use this for rights
      /// checks and for write traces, not the identity of another request.
      /// </summary>
      public ActionRequest Starter { get; }

      /// <summary>Logger for this task.</summary>
      public ILogger Logger { get; }

      /// <summary>
      /// Reports progress to whoever is watching this task. Cheap to call as often as needed; the client only
      /// reads the latest value.
      /// </summary>
      /// <param name="percent">Progress 0-100, or <c>null</c> when it cannot be measured.</param>
      /// <param name="caption">Short note on the step being worked on.</param>
      public void Report(double? percent, string caption) => _report(percent, caption);
   }
}
