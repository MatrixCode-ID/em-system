using System.Collections.ObjectModel;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// The collection of <see cref="UiCommandBase"/> belonging to one <see cref="MvvmModelBase"/>, with an
   /// additional lookup by command name.
   /// </summary>
   public class UiCommandBaseCollection : Collection<UiCommandBase>
   {
      /// <summary>
      /// Gets a command by its name.
      /// </summary>
      /// <param name="name">The name of the command being looked for.</param>
      /// <returns>The command with a matching name, or <c>null</c> when it is not found.</returns>
      public UiCommandBase? this[string name] => this.SingleOrDefault(command => command.Name == name);
   }
}
