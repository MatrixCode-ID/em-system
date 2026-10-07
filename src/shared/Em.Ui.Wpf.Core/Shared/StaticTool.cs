using System.Windows;
using System.Windows.Media;
using Em.Ui.Wpf.Core;

namespace Em.Ui.Wpf.Shared
{
   // One of the application's built-in tools, the same list whichever surface offers it: the home
   // screen of the single-page layout and the Tools menu of the multi-tab window. A tool either opens
   // a navigation or runs something of its own - a dialog, typically - against an owner window.
   internal sealed class StaticTool
   {
      public required string Name { get; init; }
      public required string Title { get; init; }
      public string Subtitle { get; init; } = string.Empty;
      public string Description { get; init; } = string.Empty;
      public required ImageSource Icon { get; init; }
      public Navigation? Navigation { get; init; }
      public Func<Window, Task>? Invoke { get; init; }

      // Whether a divider is drawn above this tool: set on the first tool after the connection and debug
      // group, so that group stands apart at the top of every surface.
      public bool StartsGroup { get; set; }
   }
}
