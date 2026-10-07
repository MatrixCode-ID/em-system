namespace Em.Shared
{
   /// <summary>
   /// Minimal contract of the application object (<c>EmApp</c>) shared with modules through
   /// <c>Em.Models</c>, without modules referencing the <c>Em.Api.Core</c> implementation directly.
   /// </summary>
   public interface IEmApp
   {
      /// <summary>
      /// Entry point for getting services from the DI container. It is read-only: every service is
      /// registered through the <c>EmAppBuilder</c> callbacks during <c>BuildApp</c>, and once the
      /// application runs the container is locked - no more services may be added.
      /// </summary>
      /// <remarks>
      /// What is returned is the provider in effect when this property is read, not always the root
      /// provider. On the API side, while a request is being processed, the request's own provider is
      /// used, so scoped services (e.g. a database connection/context) follow that request's lifetime and
      /// are disposed as soon as it ends. Outside a request - and on the UI side - the root provider is
      /// used.
      /// <para>
      /// So do not keep resolved services beyond the request: the objects are disposed when the request
      /// ends. For background work running outside a request, create your own scope with
      /// <c>ServiceProvider.CreateScope()</c>.
      /// </para>
      /// </remarks>
      IServiceProvider ServiceProvider { get; }

      /// <summary>Current server date and time: the local clock on the API side, the API server time on the client side.</summary>
      Task<DateTime> GetDateStampAsync();
   }
}
