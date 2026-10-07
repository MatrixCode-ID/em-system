using System.Windows;
using System.Windows.Interop;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// The application's message box: a small Material-style dialog on top of <see cref="EmWindow"/>,
   /// holding an icon matching the kind of message, a title, the message (selectable and copyable), and
   /// the answer buttons. Used through the <c>ShowMbox*</c> helpers in <c>Extensions</c>, not created
   /// directly.
   /// </summary>
   public partial class EmMessageBox : EmWindow
   {
      private EmMessageBox() {
         InitializeComponent();
         Vm.RequestClose += r => DialogResult = r;
      }

      /// <summary>The view model of this dialog.</summary>
      public EmMessageBoxVm Vm => (EmMessageBoxVm)DataContext;

      // The one way in. The owner is taken only once it has a window handle of its own: a window that
      // has never been shown cannot own another, and the box then simply centres on the screen.
      internal static MessageBoxResult Show(
         Window? owner,
         string title,
         string message,
         MessageBoxButton button,
         MessageBoxImage image,
         MessageBoxResult defaultButton) {

         var box = new EmMessageBox();
         box.Vm.Setup(title, message, button, image, defaultButton);

         if (owner is not null && new WindowInteropHelper(owner).Handle != IntPtr.Zero) {
            box.Owner = owner;
         }
         else {
            box.WindowStartupLocation = WindowStartupLocation.CenterScreen;
         }

         box.ShowDialog();
         return box.Vm.Result;
      }
   }

   /// <summary>
   /// One answer button on <see cref="EmMessageBox"/>.
   /// </summary>
   /// <param name="Caption">The button text.</param>
   /// <param name="Result">The answer that is returned when this button is chosen.</param>
   /// <param name="IsDefault">The button run by the Enter key while the focus is not on another button; this is the button that appears filled when the dialog opens.</param>
   /// <param name="IsCancel">The button run by the Esc key.</param>
   public sealed record EmMessageBoxButton(string Caption, MessageBoxResult Result, bool IsDefault, bool IsCancel)
   {
      /// <summary>
      /// <c>true</c> for an affirmative answer - OK or Yes. This button is given the danger color (red) so it
      /// always looks different from No and Cancel, which use the accent color.
      /// </summary>
      public bool IsConfirm => Result is MessageBoxResult.OK or MessageBoxResult.Yes;
   }

   /// <summary>
   /// View model for <see cref="EmMessageBox"/>: the message content, its answer buttons, and the answer
   /// chosen by the user.
   /// </summary>
   public class EmMessageBoxVm : MvvmModelBase
   {
      /// <summary>
      /// Creates an empty view model and registers the answer choosing command.
      /// </summary>
      public EmMessageBoxVm() {
         RegisterCommand<EmMessageBoxButton>(nameof(ChooseCommand), ChooseCommand);
      }

      /// <summary>
      /// Raised when the user chooses an answer, so its window closes itself.
      /// </summary>
      public event Action<bool>? RequestClose;

      /// <summary>The title of the message, shown in bold above the message content.</summary>
      public string Heading {
         get => Get<string>() ?? string.Empty;
         private set => Set(value);
      }

      /// <summary>The message content.</summary>
      public string Message {
         get => Get<string>() ?? string.Empty;
         private set => Set(value);
      }

      /// <summary>The kind of message, which decides its icon and color.</summary>
      public MessageBoxImage Image {
         get => Get<MessageBoxImage>();
         private set => Set(value);
      }

      /// <summary>The answer buttons, in order from left to right.</summary>
      public IReadOnlyList<EmMessageBoxButton> Buttons {
         get => Get<IReadOnlyList<EmMessageBoxButton>>() ?? [];
         private set => Set(value);
      }

      /// <summary>
      /// The user's answer. Before any button is chosen - e.g. the dialog is closed through the close button
      /// in the title row - it holds the answer of the Esc button.
      /// </summary>
      public MessageBoxResult Result { get; private set; }

      /// <summary>
      /// Fills in the message and arranges its buttons. The action button is placed at the far right and the
      /// button that cancels to its left, following the order of Material dialogs.
      /// </summary>
      /// <param name="title">The title of the message.</param>
      /// <param name="message">The message content.</param>
      /// <param name="button">The combination of buttons that is offered.</param>
      /// <param name="image">The kind of message.</param>
      /// <param name="defaultButton">
      /// The button for Enter. If it is not among the buttons offered, the action button (OK or Yes) is used.
      /// </param>
      public void Setup(string title, string message, MessageBoxButton button, MessageBoxImage image,
         MessageBoxResult defaultButton) {

         Heading = title;
         Message = message;
         Image = Normalize(image);

         MessageBoxResult[] results = button switch {
            MessageBoxButton.OKCancel => [MessageBoxResult.Cancel, MessageBoxResult.OK],
            MessageBoxButton.YesNo => [MessageBoxResult.No, MessageBoxResult.Yes],
            MessageBoxButton.YesNoCancel => [MessageBoxResult.Cancel, MessageBoxResult.No, MessageBoxResult.Yes],
            _ => [MessageBoxResult.OK]
         };

         var primary = results.Contains(defaultButton) ? defaultButton : results[^1];
         // Esc answers the way the system message box does: Cancel when offered, otherwise No,
         // otherwise the only button there is.
         var cancel = results.Contains(MessageBoxResult.Cancel) ? MessageBoxResult.Cancel
            : results.Contains(MessageBoxResult.No) ? MessageBoxResult.No
            : results[0];

         Result = cancel;
         Buttons = [.. results.Select(r => new EmMessageBoxButton(Caption(r), r, r == primary, r == cancel))];
      }

      /// <summary>
      /// Records the chosen answer, then asks its window to close.
      /// </summary>
      /// <param name="button">The button that was chosen.</param>
      public void ChooseCommand(EmMessageBoxButton button) {
         Result = button.Result;
         RequestClose?.Invoke(true);
      }

      // MessageBoxImage carries several names for each value (Hand/Stop/Error, ...); folding them onto
      // one lets the view tell them apart by a single name each.
      private static MessageBoxImage Normalize(MessageBoxImage image) => image switch {
         MessageBoxImage.Error => MessageBoxImage.Error,
         MessageBoxImage.Warning => MessageBoxImage.Warning,
         MessageBoxImage.Question => MessageBoxImage.Question,
         MessageBoxImage.Information => MessageBoxImage.Information,
         _ => MessageBoxImage.None
      };

      private static string Caption(MessageBoxResult result) => result switch {
         MessageBoxResult.Yes => "Yes",
         MessageBoxResult.No => "No",
         MessageBoxResult.Cancel => "Cancel",
         _ => "OK"
      };
   }
}
