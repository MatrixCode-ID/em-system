using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;

namespace Em.Api.Core
{
   /// <summary>
   /// Registration of one credential type in the application. Each credential type provides one
   /// implementation and registers it in the DI container, so the login flow and the account settings
   /// page can offer every existing credential type without naming their classes one by one - adding a
   /// new type only takes adding one registration.
   /// </summary>
   public interface ICredentialProviderFactory
   {
      /// <summary>
      /// Name of the credential type that is created, the same as <see cref="CredentialProviderBase.Name"/>
      /// of the result of <see cref="CreateAsync"/>.
      /// </summary>
      string Name { get; }

      /// <summary>
      /// Creates a credential of this type for <paramref name="user"/>, complete with the data already
      /// stored for them.
      /// </summary>
      /// <param name="user">The user whose credential is to be used.</param>
      /// <param name="services">The credential data service of the running request.</param>
      /// <returns>A credential ready to be checked or enrolled.</returns>
      Task<CredentialProviderBase> CreateAsync(ta_User user, CredentialServices services);
   }

   /// <summary>
   /// A compact way to get a user's credentials through the credential types registered in the
   /// application.
   /// </summary>
   public static class CredentialProviderExtensions
   {
      /// <summary>
      /// Gets one credential type of <paramref name="user"/> by its name.
      /// </summary>
      /// <param name="services">The credential data service of the running request.</param>
      /// <param name="user">The user whose credential is to be used.</param>
      /// <param name="name">Name of the credential type, e.g. <c>"PASSWORD"</c>.</param>
      /// <returns>The requested credential.</returns>
      /// <exception cref="InvalidOperationException">No credential type with that name is registered.</exception>
      public static Task<CredentialProviderBase> GetCredentialAsync(this CredentialServices services, ta_User user,
         string name) {
         var factory = services.App.ServiceProvider.GetServices<ICredentialProviderFactory>()
            .FirstOrDefault(f => f.Name == name)
            ?? throw new InvalidOperationException($"No credential provider named '{name}' is registered.");
         return factory.CreateAsync(user, services);
      }

      /// <summary>
      /// Gets every credential type of <paramref name="user"/>. What is returned is every type registered in
      /// the application, including those the user has not enrolled - use
      /// <see cref="CredentialProviderBase.IsEnrolled"/> to tell which are ready for signing in and which
      /// can only be offered for enrollment.
      /// </summary>
      /// <param name="services">The credential data service of the running request.</param>
      /// <param name="user">The user whose credential is to be used.</param>
      /// <returns>All credentials of that user.</returns>
      public static async Task<CredentialProviderBase[]> GetCredentialsAsync(this CredentialServices services,
         ta_User user) {
         var factories = services.App.ServiceProvider.GetServices<ICredentialProviderFactory>();

         // Created one after another rather than all at once: each one reads and may write through
         // the same service, and there is nothing to gain from overlapping a handful of small reads
         // at the price of a failure that no longer says which credential caused it.
         var result = new List<CredentialProviderBase>();
         foreach (var factory in factories) result.Add(await factory.CreateAsync(user, services));
         return [.. result];
      }
   }
}
