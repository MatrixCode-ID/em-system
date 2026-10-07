using FontAwesome6;
using Em.Ui.Wpf.Core.Release;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>Chooses the key destination for the current Create or Import operation.</summary>
   public partial class SigningKeyDestinationDialog : EmWindow
   {
      /// <summary>Creates a new instance of <see cref="SigningKeyDestinationDialog"/>.</summary>
      public SigningKeyDestinationDialog(string title, ReleaseSigningSource initial) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Title = title;
         Vm.IsProfileFile = initial == ReleaseSigningSource.ProfileFile;
         Vm.IsStore = initial == ReleaseSigningSource.Store;
         Vm.RequestClose += result => DialogResult = result;
      }
      /// <summary>The vm.</summary>
      public SigningKeyDestinationDialogVm Vm => (SigningKeyDestinationDialogVm)DataContext;
      /// <summary>The destination.</summary>
      public ReleaseSigningSource Destination => Vm.IsProfileFile ? ReleaseSigningSource.ProfileFile : ReleaseSigningSource.Store;
   }

   /// <summary>View model for <see cref="SigningKeyDestinationDialog"/>.</summary>
   public class SigningKeyDestinationDialogVm : MvvmModelBase
   {
      /// <summary>Creates a new instance of <see cref="SigningKeyDestinationDialogVm"/>.</summary>
      public SigningKeyDestinationDialogVm() => RegisterCommand(nameof(OkCommand), OkCommand);
      /// <summary>Raised for request close.</summary>
      public event Action<bool>? RequestClose;
      /// <summary>The title.</summary>
      public string Title {
         get => Get<string>() ?? "Create Signing Key";
         set => Set(value);
      }
      /// <summary>The caption.</summary>
      public string Caption => "Where should the signing key be saved?";
      /// <summary>The ok caption.</summary>
      public string OkCaption => "Continue";
      /// <summary>The icon.</summary>
      public EFontAwesomeIcon Icon => EFontAwesomeIcon.Solid_Key;
      /// <summary>Indicates profile file.</summary>
      public bool IsProfileFile {
         get => Get<bool>();
         set => Set(value);
      }
      /// <summary>Indicates store.</summary>
      public bool IsStore {
         get => Get<bool>();
         set => Set(value);
      }
      /// <summary>Runs the ok command.</summary>
      public void OkCommand() => RequestClose?.Invoke(true);
   }
}
