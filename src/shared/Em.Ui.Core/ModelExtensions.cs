using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em
{
   /// <summary>Extension methods that reach the UI models from the application, a contact, or a user.</summary>
   public static class ModelExtensions
   {
      #region For Contact

      /// <summary>Gets one page of contacts.</summary>
      public static Task<Contact[]> GetContacts_InPageAsync(this IEmApp app, int page, int pageSize) =>
         Contact.GetContactsInPageAsync(app, page, pageSize);

      #endregion

      #region For Address

      /// <summary>Creates a new address for this contact.</summary>
      public static Task<ContactAddress> CreateAddressAsync(this Contact contact, string cAddressName,
         string cAddressLocation, string cAddressZip, AddressState cAddressState, object? jsonObject = null) =>
         ContactAddress.CreateAddress(contact, cAddressName, cAddressLocation, cAddressZip, cAddressState,
            jsonObject);

      /// <summary>Gets all addresses of this contact.</summary>
      public static Task<ContactAddress[]> GetAddressesAsync(this Contact contact) =>
         ContactAddress.GetAddresses_ByContact(contact);

      /// <summary>Gets one address of this contact by its id.</summary>
      public static async Task<ContactAddress> GetAddressAsync(this Contact contact, string cAddressId) {
         var dbData = await contact.Service.GetVi_Address_ById(cAddressId);
         return dbData == null
            ? throw new InvalidOperationException("No address found for " + cAddressId)
            : ContactAddress.Build(contact, dbData);
      }

      #endregion

      #region For User

      /// <summary>Gets the contact of this user.</summary>
      public static Task<Contact> GetContactAsync(this User user) =>
         Contact.GetContact_ByIdAsync(user.App, user.cContactId)!;

      /// <summary>Gets one user by its id.</summary>
      public static Task<User> GetUser_ById(this IEmApp app, string cUserId) =>
         User.GetUser_ByIdAsync(app, cUserId);

      /// <summary>Gets one page of users.</summary>
      public static Task<User[]> GetUsers_InPageAsync(this IEmApp app, int page, int pageSize) =>
         User.GetUsers_InPageAsync(app, page, pageSize);

      /// <summary>
      /// The roles a user currently holds, together with the validity period of each assignment. What is
      /// answered is the assignment rows, not the roles: an assignment may well point to a role that is
      /// turned off or whose validity period has not begun, and that must still be visible on its management
      /// screen.
      /// </summary>
      public static Task<ta_UserRole[]> GetRolesAsync(this User user) =>
         user.App.ServiceProvider.GetRequiredService<ICredentialServices>()
            .GetTa_UserRoles_ByUserId(user.cUserId);

      #endregion

      #region For Role

      /// <summary>Gets one role by its id.</summary>
      public static Task<Role> GetRole_ById(this IEmApp app, string cRoleId) =>
         Role.GetRole_ByIdAsync(app, cRoleId);

      /// <summary>Gets one page of roles.</summary>
      public static Task<Role[]> GetRoles_InPageAsync(this IEmApp app, int page, int pageSize) =>
         Role.GetRoles_InPageAsync(app, page, pageSize);

      #endregion

   }
}