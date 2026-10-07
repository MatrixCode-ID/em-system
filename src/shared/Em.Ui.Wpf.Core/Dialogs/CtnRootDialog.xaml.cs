using Em.Api.Core.Models;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
using FontAwesome6;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// A dialog to create a container registry root (name, description) or to edit an existing root
   /// (description and active status; the name cannot be changed). The result is read from
   /// <see cref="Vm"/> after <c>ShowDialog()</c> returns <c>true</c>.
   /// </summary>
   public partial class CtnRootDialog : EmWindow
   {
      /// <summary>Creates the dialog; <paramref name="existing"/> <c>null</c> means a new root.</summary>
      public CtnRootDialog(CtnRootInfo? existing = null) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Initialize(existing);
         Vm.Refresh();
         Vm.RequestClose += result => DialogResult = result;
      }

      /// <summary>The view model of this dialog.</summary>
      public CtnRootDialogVm Vm => (CtnRootDialogVm)DataContext;
   }

   /// <summary>The view model for <see cref="CtnRootDialog"/>.</summary>
   public class CtnRootDialogVm : CtnFormVmBase
   {
      internal void Initialize(CtnRootInfo? existing) {
         NameLabel = "Root name";
         NamePlaceholder = "acme";
         NameHelp = $"Up to {CtnInput.MaxRootName} lowercase letters or digits, separated by '.', '_' or '-'. " +
                    "It is the first part of every pull name and cannot be changed later.";
         if (existing is null) {
            Title = "New Root";
            Caption = "A root groups the containers of one product or team, like acme in host/acme/api.";
            OkCaption = "Create";
            Icon = EFontAwesomeIcon.Solid_Cubes;
            IsNameEditable = true;
            return;
         }

         Title = "Edit Root";
         Caption = "Change the description or switch the root on or off. A disabled root refuses every push and pull.";
         OkCaption = "Save";
         Icon = EFontAwesomeIcon.Solid_PenToSquare;
         IsNameEditable = false;
         ShowActive = true;
         Name = existing.Name;
         Description = existing.Description ?? "";
         IsActive = existing.IsActive;
      }

      /// <inheritdoc />
      protected override string? ValidateName(string name) =>
         CtnInput.IsValidName(name, CtnInput.MaxRootName)
            ? null
            : $"Use 1-{CtnInput.MaxRootName} lowercase letters or digits, separated by '.', '_' or '-'.";
   }
}
