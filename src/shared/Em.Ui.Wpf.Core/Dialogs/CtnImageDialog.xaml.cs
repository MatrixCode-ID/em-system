using Em.Api.Core.Models;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
using FontAwesome6;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// A dialog to create a container (name, description) in a root or to edit an existing container
   /// (description and active status; the name cannot be changed). The result is read from
   /// <see cref="Vm"/> after <c>ShowDialog()</c> returns <c>true</c>.
   /// </summary>
   public partial class CtnImageDialog : EmWindow
   {
      /// <summary>
      /// Creates the dialog.
      /// </summary>
      /// <param name="rootName">The name of the root the container belongs to, to show the pull name and check its length.</param>
      /// <param name="existing"><c>null</c> means a new container.</param>
      public CtnImageDialog(string rootName, CtnImageInfo? existing = null) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Initialize(rootName, existing);
         Vm.Refresh();
         Vm.RequestClose += result => DialogResult = result;
      }

      /// <summary>The view model of this dialog.</summary>
      public CtnImageDialogVm Vm => (CtnImageDialogVm)DataContext;
   }

   /// <summary>The view model for <see cref="CtnImageDialog"/>.</summary>
   public class CtnImageDialogVm : CtnFormVmBase
   {
      private string _rootName = "";

      internal void Initialize(string rootName, CtnImageInfo? existing) {
         _rootName = rootName;
         NameLabel = "Container name";
         NamePlaceholder = "api";
         NameHelp = $"Up to {CtnInput.MaxImageName} lowercase letters or digits, separated by '.', '_' or '-'. " +
                    "Together with the root it forms the pull name and cannot be changed later.";
         if (existing is null) {
            Title = "New Container";
            Caption = $"The container is created in '{rootName}'. It has to exist before an image can be pushed to it.";
            OkCaption = "Create";
            Icon = EFontAwesomeIcon.Solid_Box;
            IsNameEditable = true;
            return;
         }

         Title = "Edit Container";
         Caption = $"{existing.FullName} - change the description or switch it on or off. A disabled container refuses every push and pull.";
         OkCaption = "Save";
         Icon = EFontAwesomeIcon.Solid_PenToSquare;
         IsNameEditable = false;
         ShowActive = true;
         Name = existing.Name;
         Description = existing.Description ?? "";
         IsActive = existing.IsActive;
      }

      /// <summary>The pull name without the host for the name being typed: <c>root/name</c>.</summary>
      public string FullNamePreview => NameResult.Length == 0 ? $"{_rootName}/" : $"{_rootName}/{NameResult}";

      /// <inheritdoc />
      protected override string? ValidateName(string name) {
         if (!CtnInput.IsValidName(name, CtnInput.MaxImageName))
            return $"Use 1-{CtnInput.MaxImageName} lowercase letters or digits, separated by '.', '_' or '-'.";

         return _rootName.Length + 1 + name.Length > CtnInput.MaxFullName
            ? $"'{_rootName}/{name}' is longer than {CtnInput.MaxFullName} characters."
            : null;
      }

      /// <inheritdoc />
      protected override void OnInputChanged() {
         base.OnInputChanged();
         NotifyChanged(nameof(FullNamePreview));
      }
   }
}
