using Em.Shared;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// One value a list can be filtered on, as offered by a chip, a check box or a segment: its caption, the
   /// value it stands for, and whether it is picked. Bound two way, so a click arrives as
   /// <see cref="IsSelected"/> changing.
   /// </summary>
   /// <typeparam name="T">Type of the value the option stands for.</typeparam>
   public sealed class FilterOption<T> : NotifyPropertyBase
   {
      /// <summary>Creates an option.</summary>
      /// <param name="caption">Text shown for the option.</param>
      /// <param name="value">The value the option stands for.</param>
      public FilterOption(string caption, T value) {
         Caption = caption;
         Value = value;
      }

      /// <summary>Text shown for the option.</summary>
      public string Caption { get; }

      /// <summary>The value the option stands for.</summary>
      public T Value { get; }

      /// <summary>Whether the option is picked.</summary>
      public bool IsSelected {
         get => Get<bool>();
         set => Set(value);
      }
   }
}
