using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Ui.Maui.Shared
{
   /// <summary>
   /// The contract of the MAUI-specific application object, adding what screens read - the brand
   /// settings and the password rules - on top of the base contract <see cref="IEmApp"/>. It exists so a
   /// screen can ask for both from the DI container without depending on the concrete application class.
   /// </summary>
   public interface IEmAppUi : IEmApp
   {
      /// <summary>
      /// The brand display settings in force, always filled - see <see cref="BrandingInfo"/> for its default
      /// values.
      /// </summary>
      BrandingInfo Branding { get; }

      /// <summary>
      /// The password rules in force, always filled - see <see cref="Shared.PasswordPolicy"/> for its default
      /// values.
      /// </summary>
      PasswordPolicy PasswordPolicy { get; }
   }
}
