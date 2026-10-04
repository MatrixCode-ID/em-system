using System.IO;
using System.Windows;
using FontAwesome6;
using Microsoft.Win32;
using Em.Ui.Wpf.Core.Release;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>Memilih folder profile tanpa memindahkan isi folder lama.</summary>
   public partial class ReleaseProfilesFolderDialog : EmWindow
   {
      public ReleaseProfilesFolderDialog(string folder) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Folder = folder;
         Vm.RequestClose += result => DialogResult = result;
      }
      public ReleaseProfilesFolderDialogVm Vm => (ReleaseProfilesFolderDialogVm)DataContext;
      public string SelectedFolder => Path.GetFullPath(Vm.Folder);
   }

   public class ReleaseProfilesFolderDialogVm : MvvmModelBase
   {
      public ReleaseProfilesFolderDialogVm() {
         RegisterCommand(nameof(OkCommand), OkCommand, OkCommandAllowed);
         RegisterCommand(nameof(BrowseCommand), BrowseCommand);
         RegisterCommand(nameof(CopyCommand), CopyCommand);
         RegisterCommand(nameof(ResetCommand), ResetCommand);
      }
      public event Action<bool>? RequestClose;
      public string Title => "Release Profiles Folder";
      public string Caption => "Choose where release profiles are stored.";
      public string OkCaption => "Save";
      public EFontAwesomeIcon Icon => EFontAwesomeIcon.Solid_FolderOpen;
      public string Folder {
         get => Get<string>() ?? "";
         set => Set(value, _ => Commands[nameof(OkCommand)]?.RaiseCanExecuteChanged());
      }
      public void OkCommand() => RequestClose?.Invoke(true);
      public bool OkCommandAllowed() {
         try {
            return !string.IsNullOrWhiteSpace(Folder) && Path.GetFullPath(Folder).Length > 0;
         }
         catch (Exception x) when (x is ArgumentException or NotSupportedException or IOException) { return false; }
      }
      public void BrowseCommand() {
         var picker = new OpenFolderDialog { Title = "Choose the profiles folder" };
         if (picker.ShowDialog(DialogOwner) == true) Folder = picker.FolderName;
      }
      public void CopyCommand() {
         try { Clipboard.SetText(Folder); }
         catch (Exception x) { DialogOwner?.ShowMboxError(x.Message); }
      }
      public void ResetCommand() => Folder = ReleaseManagerPreferences.DefaultProfilesFolder;
   }
}
