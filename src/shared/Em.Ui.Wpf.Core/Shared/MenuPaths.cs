using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Shared
{
   // How a navigation's MenuPath is read into menu levels, shared by every menu built from it (the
   // home screen and the multi-tab Apps menu), so the two always group the same way.
   internal static class MenuPaths
   {
      // "SALES/Administration" -> ["SALES", "Administration"]. Empty segments are dropped so a
      // stray or trailing separator never produces a group without a caption.
      internal static string[] Split(MenuPath? path) {
         if (path == null || path.IsEmptyPath) return [];
         return path.Path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      }

      // Groups are matched this way, so "Sales" and "SALES" stay one group.
      internal const StringComparison SegmentComparison = StringComparison.OrdinalIgnoreCase;
   }
}
