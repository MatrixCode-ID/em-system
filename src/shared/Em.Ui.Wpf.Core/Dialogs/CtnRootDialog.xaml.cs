using Em.Api.Core.Models;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
using FontAwesome6;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Dialog untuk membuat root container registry (nama, deskripsi) atau mengedit root yang ada
   /// (deskripsi dan status aktif; nama tidak bisa diganti). Hasilnya dibaca dari <see cref="Vm"/>
   /// setelah <c>ShowDialog()</c> mengembalikan <c>true</c>.
   /// </summary>
   public partial class CtnRootDialog : EmWindow
   {
      /// <summary>Membuat dialog; <paramref name="existing"/> <c>null</c> berarti root baru.</summary>
      public CtnRootDialog(CtnRootInfo? existing = null) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Initialize(existing);
         Vm.Refresh();
         Vm.RequestClose += result => DialogResult = result;
      }

      /// <summary>ViewModel dialog ini.</summary>
      public CtnRootDialogVm Vm => (CtnRootDialogVm)DataContext;
   }

   /// <summary>ViewModel untuk <see cref="CtnRootDialog"/>.</summary>
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
