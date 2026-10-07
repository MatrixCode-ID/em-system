namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Kind of a navigation, which must be stated every time a navigation is registered. This kind
   /// classifies a screen by its role, not by its look.
   /// </summary>
   public enum NavigationKind
   {
      /// <summary>
      /// A manager screen: a list, a search, or a tool that is the starting point of work - e.g. the user
      /// list. Usually opened from the menu and carries no parameter.
      /// </summary>
      Manager,

      /// <summary>
      /// An editor screen for one document or one record - e.g. the editor of one user. Usually opened from a
      /// manager screen with a parameter that points to its document.
      /// </summary>
      Editor
   }
}
