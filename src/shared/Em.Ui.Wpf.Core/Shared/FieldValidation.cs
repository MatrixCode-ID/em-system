using System.Windows;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// A "this input is not right yet" marker that can be attached to any input control through an attached
   /// property. It exists so a screen need not know how a field is drawn: the view model only says its
   /// input is wrong, and the field style takes care of the display (e.g. the border turns red).
   /// </summary>
   /// <remarks>
   /// Deliberately does not use WPF's built-in <c>Validation.HasError</c>: that built-in rule holds back a
   /// wrong value so it does not reach the model, while here the value may still enter and it is the view
   /// model that judges whether it is right - the same place that decides whether the save button may be
   /// pressed.
   /// </remarks>
   public static class FieldValidation
   {
      /// <summary>
      /// The attached property that marks a problematic input. Attach it to an input control and bind it to
      /// the view model property that judges its content, e.g.
      /// <c>shared:FieldValidation.HasError="{Binding HasEmailError}"</c>.
      /// </summary>
      public static readonly DependencyProperty HasErrorProperty =
         DependencyProperty.RegisterAttached(
            "HasError",
            typeof(bool),
            typeof(FieldValidation),
            new FrameworkPropertyMetadata(false));

      /// <summary>Reads the problematic input marker of a control.</summary>
      /// <param name="element">The input control to read.</param>
      /// <returns><c>true</c> when that control's input is currently considered wrong.</returns>
      public static bool GetHasError(DependencyObject element) =>
         (bool)element.GetValue(HasErrorProperty);

      /// <summary>Sets the problematic input marker on a control.</summary>
      /// <param name="element">The input control to mark.</param>
      /// <param name="value"><c>true</c> when the input is wrong, <c>false</c> when it is right.</param>
      public static void SetHasError(DependencyObject element, bool value) =>
         element.SetValue(HasErrorProperty, value);
   }
}
