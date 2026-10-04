using Em.Api.Core.Models;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Shared
{
   public class UserEditorNavigationPayload(User? data = null) : NavigationPayloadBase(data)
   {
      private Action<User>? _whenUserCreated;
      public static UserEditorNavigationPayload Create(Action<User> WhenUserCreated) {
         var result = new UserEditorNavigationPayload();
         result.DataState = DataState.NewData;
         result._whenUserCreated = WhenUserCreated;
         return result;
      }
      public static UserEditorNavigationPayload Create(User? data = null) {
         return new UserEditorNavigationPayload(data) {
            DataState = data == null ? DataState.NewData : DataState.EditData
         };
      }
      public new User? Data => (User?)base.Data;

      /// <summary>
      /// Judul layar penyunting untuk parameter ini: "Create New User" selama pengguna barunya belum
      /// tersimpan, lalu "Edit User: " diikuti nama akunnya - yang unik, jadi dua pengguna berbeda
      /// tidak pernah berbagi satu judul.
      /// </summary>
      public override string? Title =>
         DataState == DataState.NewData || Data is null ? "Create New User" : $"Edit User: {Data.cUserAccount}";

      public void SetNewUser(User user) {
         if (DataState != DataState.NewData)
            throw new InvalidOperationException("Cannot reset user data");
         _data = user;
         DataState = DataState.EditData;
         _whenUserCreated!.Invoke(user);
      }
   }
}
