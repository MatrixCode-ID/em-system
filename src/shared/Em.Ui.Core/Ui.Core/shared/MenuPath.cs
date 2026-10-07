namespace Em.Ui.Core.Shared
{
   /// <summary>Position of a navigation in the home menu tree.</summary>
   public class MenuPath
   {
      private MenuPath() {
         
      }

      /// <summary>A path that places the navigation outside the menu.</summary>
      public static MenuPath Empty => new() {
         Path = string.Empty,
         IsEmptyPath = true
      }; 
      /// <summary>Creates a path from its text form.</summary>
      public static MenuPath Set(string path) {
         var menu = new MenuPath() {
            Path = path,
            IsEmptyPath = string.IsNullOrEmpty(path)
         };
         return menu.IsEmptyPath ? throw new ArgumentNullException(nameof(path)) : menu;
      }
      
      /// <summary>The path text.</summary>
      public required string Path { get; init; }
      /// <summary>Whether this path is empty.</summary>
      public required bool IsEmptyPath { get; init; }
   }
}