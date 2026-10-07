using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// A marker base class for module services on the UI side, analogous to <c>ServicesBase</c> in the
   /// backend but for the UI hierarchy (not the HTTP dispatcher). A frontend module service class that
   /// implements <see cref="IServices"/> is advised to derive from this class.
   /// </summary>
   public abstract class ServiceUiBase : IServices
   {
      /// <summary>Creates the service for an application.</summary>
      public ServiceUiBase(IEmApp app) {
         App = app;
         // GetType() returns the concrete derived type (e.g. SampleServices), not ServiceUiBase, and the
         // constructor is already enough for that - there is no reason to delay reading it, and that delay is
         // what once made claims get asked while ModuleName was still empty (module services were asked through
         // XxxCommandAllowed before the first request had ever run).
         // Not required: a UI service without [Module] has always worked that way and falls back to the class
         // name.
         ModuleName = ModuleAttribute.ResolveName(GetType(), required: false);
      }

      /// <summary>Name of the module this service belongs to.</summary>
      public string ModuleName { get; }
      /// <summary>The API client used by this service.</summary>
      public ApiClient? ApiClient { get; protected set; }

      /// <summary>The application this service belongs to.</summary>
      public virtual IEmApp App { get; }

      /// <summary>Calls a GET action of this module and reads its result.</summary>
      protected Task<T> GetAsync<T>(string action, params object[] args) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.GetAsync<T>(ModuleName, action, args);
      }

      /// <summary>
      /// Calls a GET action of this module that returns file content. The caller must close the resulting
      /// stream; see <see cref="Shared.ApiClient.GetStreamAsync(string, string, object[])"/> for its full
      /// rules.
      /// </summary>
      /// <param name="action">Name of the action, usually <c>nameof</c> of the method being implemented.</param>
      /// <param name="args">Arguments of the action.</param>
      protected Task<Stream> GetStreamAsync(string action, params object[] args) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.GetStreamAsync(ModuleName, action, args);
      }

     /// <summary>Calls a POST action of this module and reads its result.</summary>
     protected Task<T> PostAsync<T>(string action, params object[] args) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.PostAsync<T>(ModuleName, action, args);
      }

      /// <summary>Calls a POST action of this module that returns no value.</summary>
      protected Task PostAsync(string action, params object[] args) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.PostAsync(ModuleName, action, args);
      }

      /// <summary>
      /// Calls a POST action of this module with its own time limit; see
      /// <see cref="Shared.ApiClient.PostAsync{T}(TimeSpan, string, string, object[])"/>.
      /// </summary>
      protected Task<T> PostAsync<T>(TimeSpan timeout, string action, params object[] args) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.PostAsync<T>(timeout, ModuleName, action, args);
      }

      /// <inheritdoc cref="PostAsync{T}(TimeSpan, string, object[])"/>
      protected Task PostAsync(TimeSpan timeout, string action, params object[] args) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.PostAsync(timeout, ModuleName, action, args);
      }

      /// <summary>
      /// Calls a streaming action of this module: the content of <paramref name="content"/> is sent raw, and
      /// <paramref name="payload"/> - the object received by the action's other parameter - goes in the
      /// header. No time limit; see
      /// <see cref="Shared.ApiClient.PostStreamAsync(string, string, Stream, object?)"/> for its full rules.
      /// </summary>
      /// <param name="action">Name of the action, usually <c>nameof</c> of the method being implemented.</param>
      /// <param name="content">The stream to send; not closed by this method.</param>
      /// <param name="payload">Object for the parameter other than the stream, or <c>null</c> when there is none.</param>
      protected Task PostStreamAsync(string action, Stream content, object? payload = null) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.PostStreamAsync(ModuleName, action, content, payload);
      }

      /// <summary>
      /// Same as <see cref="PostStreamAsync(string, Stream, object?)"/>, except that the server's answer is
      /// read into <typeparamref name="T"/>.
      /// </summary>
      /// <inheritdoc cref="PostStreamAsync(string, Stream, object?)"/>
      protected Task<T> PostStreamAsync<T>(string action, Stream content, object? payload = null) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.PostStreamAsync<T>(ModuleName, action, content, payload);
      }
   }
}