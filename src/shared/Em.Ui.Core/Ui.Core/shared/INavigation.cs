using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Definition of a screen that can be opened by its name: its default title, body type, place in the
   /// menu, and its access right binding. This definition holds no body or data - every time the screen is
   /// opened, what is created is an <see cref="INavigationEntry"/>, so a single definition can appear
   /// several times at once with different data.
   /// </summary>
   public interface INavigation
   {
      /// <summary>Unique name of the navigation, which is what <c>NavigateTo(string)</c> looks up.</summary>
      string Name { get; }

      /// <summary>
      /// Default title of the screen. Used as the entry title when the parameter it carries does not provide
      /// its own title (see <see cref="NavigationPayloadBase.Title"/>).
      /// </summary>
      string Title { get; set; }

      /// <summary>Short caption below the title.</summary>
      string Subtitle { get; set; }

      /// <summary>Long explanation, used by the menu card on the home screen.</summary>
      string Description { get; set; }

      /// <summary>Where this screen sits in the home menu tree, or <c>null</c> when it does not go through the menu.</summary>
      MenuPath? MenuPath { get; set; }

      /// <summary>The application router that owns this navigation.</summary>
      INavigationHost NavigationHost { get; }

      /// <summary>Type of the control that is built as the body every time this screen is opened.</summary>
      IBodyType BodyType { get; }

      /// <summary>Whether this screen refuses to be opened without a parameter.</summary>
      bool RequireParameter { get; }

      /// <summary>Order of this screen in the home menu; the smaller, the nearer the front.</summary>
      int OrderIndex { get; }

      /// <summary>
      /// Kind of this screen, <see cref="NavigationKind.Manager"/> or <see cref="NavigationKind.Editor"/>.
      /// Required when the navigation is registered.
      /// </summary>
      NavigationKind Kind { get; }

      /// <summary>
      /// Name of the module that owns this navigation, set through the <c>AddNavigation</c> overload that is
      /// bound to a module - or <c>null</c> when this navigation was registered without any binding and is
      /// therefore open to anyone who has signed in. Used by <see cref="NavigationAccess"/>, not set directly
      /// by modules.
      /// </summary>
      string? ModuleName { get; }

      /// <summary>
      /// The claim a user must hold to open this navigation, set through the <c>AddNavigation</c> overload
      /// that carries a claim - or <c>null</c> when this navigation is only bound to the module without a
      /// particular claim. Used by <see cref="NavigationAccess"/>, not set directly by modules.
      /// </summary>
      ClaimAction? RequiredClaim { get; }
   }
}
