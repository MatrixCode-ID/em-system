namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// One condition currently narrowing a list, shown as a removable chip above it. The chip knows how to take
   /// its own condition back, so the screen needs a single remove command whatever the condition is.
   /// </summary>
   public sealed class ActiveFilterChip
   {
      private readonly Action _remove;

      /// <summary>Creates a chip.</summary>
      /// <param name="caption">Text on the chip, e.g. "Role: Finance".</param>
      /// <param name="remove">Takes the condition back; the list is reloaded by the caller afterwards.</param>
      public ActiveFilterChip(string caption, Action remove) {
         Caption = caption;
         _remove = remove;
      }

      /// <summary>Text on the chip.</summary>
      public string Caption { get; }

      /// <summary>Takes the condition this chip stands for back.</summary>
      public void Remove() => _remove();
   }
}
