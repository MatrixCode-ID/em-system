using Em.Api.Core.Models;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>Navigation parameter of the user editor.</summary>
   public class UserEditorNavigationPayload(User? data = null) : NavigationPayloadBase(data)
   {
      private Action<User>? _whenUserCreated;
      /// <summary>Creates a parameter for a new user, calling back when it is created.</summary>
      public static UserEditorNavigationPayload Create(Action<User> WhenUserCreated) {
         var result = new UserEditorNavigationPayload();
         result.DataState = DataState.NewData;
         result._whenUserCreated = WhenUserCreated;
         return result;
      }
      /// <summary>Creates a parameter for editing a user, or for a new user when none is given.</summary>
      public static UserEditorNavigationPayload Create(User? data = null) {
         return new UserEditorNavigationPayload(data) {
            DataState = data == null ? DataState.NewData : DataState.EditData
         };
      }
      /// <summary>The data.</summary>
      public new User? Data => (User?)base.Data;

      /// <summary>
      /// The title of the editor screen for this parameter: "Create New User" while the new user has not been
      /// saved, then "Edit User: " followed by its account name - which is unique, so two different users
      /// never share one title.
      /// </summary>
      public override string? Title =>
         DataState == DataState.NewData || Data is null ? "Create New User" : $"Edit User: {Data.cUserAccount}";

      /// <summary>Sets the user being created.</summary>
      public void SetNewUser(User user) {
         if (DataState != DataState.NewData)
            throw new InvalidOperationException("Cannot reset user data");
         _data = user;
         DataState = DataState.EditData;
         _whenUserCreated!.Invoke(user);
      }
   }
}
