namespace Em.Ui.Core.Shared
{
   public class MenuPath
   {
      private MenuPath() {
         
      }

      public static MenuPath Empty => new() {
         Path = string.Empty,
         IsEmptyPath = true
      }; 
      public static MenuPath Set(string path) {
         var menu = new MenuPath() {
            Path = path,
            IsEmptyPath = string.IsNullOrEmpty(path)
         };
         return menu.IsEmptyPath ? throw new ArgumentNullException(nameof(path)) : menu;
      }
      
      public required string Path { get; init; }
      public required bool IsEmptyPath { get; init; }
   }
}