using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em
{
   public static class ModelExtensions
   {
      #region For Contact

      public static Task<Contact[]> GetContacts_InPageAsync(this IEmApp app, int page, int pageSize) =>
         Contact.GetContactsInPageAsync(app, page, pageSize);

      #endregion

      #region For Address

      public static Task<ContactAddress> CreateAddressAsync(this Contact contact, string cAddressName,
         string cAddressLocation, string cAddressZip, AddressState cAddressState, object? jsonObject = null) =>
         ContactAddress.CreateAddress(contact, cAddressName, cAddressLocation, cAddressZip, cAddressState,
            jsonObject);

      public static Task<ContactAddress[]> GetAddressesAsync(this Contact contact) =>
         ContactAddress.GetAddresses_ByContact(contact);

      public static async Task<ContactAddress> GetAddressAsync(this Contact contact, string cAddressId) {
         var dbData = await contact.Service.GetVi_Address_ById(cAddressId);
         return dbData == null
            ? throw new InvalidOperationException("No address found for " + cAddressId)
            : ContactAddress.Build(contact, dbData);
      }

      #endregion

      #region For User

      public static Task<Contact> GetContactAsync(this User user) =>
         Contact.GetContact_ByIdAsync(user.App, user.cContactId)!;

      public static Task<User> GetUser_ById(this IEmApp app, string cUserId) =>
         User.GetUser_ByIdAsync(app, cUserId);

      public static Task<User[]> GetUsers_InPageAsync(this IEmApp app, int page, int pageSize) =>
         User.GetUsers_InPageAsync(app, page, pageSize);

      /// <summary>
      /// Role yang sedang dipegang seorang user, berikut masa berlaku tiap penugasannya. Yang
      /// dijawab adalah baris penugasannya, bukan role-nya: sebuah penugasan bisa saja menunjuk
      /// role yang sedang dimatikan atau yang masa berlakunya belum mulai, dan itu tetap harus
      /// kelihatan di layar pengelolanya.
      /// </summary>
      public static Task<ta_UserRole[]> GetRolesAsync(this User user) =>
         user.App.ServiceProvider.GetRequiredService<ICredentialServices>()
            .GetTa_UserRoles_ByUserId(user.cUserId);

      #endregion

      #region For Role

      public static Task<Role> GetRole_ById(this IEmApp app, string cRoleId) =>
         Role.GetRole_ByIdAsync(app, cRoleId);

      public static Task<Role[]> GetRoles_InPageAsync(this IEmApp app, int page, int pageSize) =>
         Role.GetRoles_InPageAsync(app, page, pageSize);

      #endregion

   }
}