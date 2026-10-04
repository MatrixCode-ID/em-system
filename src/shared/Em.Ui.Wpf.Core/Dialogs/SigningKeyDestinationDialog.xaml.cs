using FontAwesome6;
using Em.Ui.Wpf.Core.Release;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>Memilih tujuan key untuk operasi Create atau Import saat ini.</summary>
   public partial class SigningKeyDestinationDialog : EmWindow
   {
      public SigningKeyDestinationDialog(string title, ReleaseSigningSource initial) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Title = title;
         Vm.IsProfileFile = initial == ReleaseSigningSource.ProfileFile;
         Vm.IsStore = initial == ReleaseSigningSource.Store;
         Vm.RequestClose += result => DialogResult = result;
      }
      public SigningKeyDestinationDialogVm Vm => (SigningKeyDestinationDialogVm)DataContext;
      public ReleaseSigningSource Destination => Vm.IsProfileFile ? ReleaseSigningSource.ProfileFile : ReleaseSigningSource.Store;
   }

   public class SigningKeyDestinationDialogVm : MvvmModelBase
   {
      public SigningKeyDestinationDialogVm() => RegisterCommand(nameof(OkCommand), OkCommand);
      public event Action<bool>? RequestClose;
      public string Title {
         get => Get<string>() ?? "Create Signing Key";
         set => Set(value);
      }
      public string Caption => "Where should the signing key be saved?";
      public string OkCaption => "Continue";
      public EFontAwesomeIcon Icon => EFontAwesomeIcon.Solid_Key;
      public bool IsProfileFile {
         get => Get<bool>();
         set => Set(value);
      }
      public bool IsStore {
         get => Get<bool>();
         set => Set(value);
      }
      public void OkCommand() => RequestClose?.Invoke(true);
   }
}
