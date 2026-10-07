using System.IO;
using System.Windows;
using FontAwesome6;
using Microsoft.Win32;
using Em.Ui.Wpf.Core.Release;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>Chooses the profile folder without moving the content of the old folder.</summary>
   public partial class ReleaseProfilesFolderDialog : EmWindow
   {
      /// <summary>Creates a new instance of <see cref="ReleaseProfilesFolderDialog"/>.</summary>
      public ReleaseProfilesFolderDialog(string folder) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Folder = folder;
         Vm.RequestClose += result => DialogResult = result;
      }
      /// <summary>The vm.</summary>
      public ReleaseProfilesFolderDialogVm Vm => (ReleaseProfilesFolderDialogVm)DataContext;
      /// <summary>The selected folder.</summary>
      public string SelectedFolder => Path.GetFullPath(Vm.Folder);
   }

   /// <summary>View model for <see cref="ReleaseProfilesFolderDialog"/>.</summary>
   public class ReleaseProfilesFolderDialogVm : MvvmModelBase
   {
      /// <summary>Creates a new instance of <see cref="ReleaseProfilesFolderDialogVm"/>.</summary>
      public ReleaseProfilesFolderDialogVm() {
         RegisterCommand(nameof(OkCommand), OkCommand, OkCommandAllowed);
         RegisterCommand(nameof(BrowseCommand), BrowseCommand);
         RegisterCommand(nameof(CopyCommand), CopyCommand);
         RegisterCommand(nameof(ResetCommand), ResetCommand);
      }
      /// <summary>Raised for request close.</summary>
      public event Action<bool>? RequestClose;
      /// <summary>The title.</summary>
      public string Title => "Release Profiles Folder";
      /// <summary>The caption.</summary>
      public string Caption => "Choose where release profiles are stored.";
      /// <summary>The ok caption.</summary>
      public string OkCaption => "Save";
      /// <summary>The icon.</summary>
      public EFontAwesomeIcon Icon => EFontAwesomeIcon.Solid_FolderOpen;
      /// <summary>The folder.</summary>
      public string Folder {
         get => Get<string>() ?? "";
         set => Set(value, _ => Commands[nameof(OkCommand)]?.RaiseCanExecuteChanged());
      }
      /// <summary>Runs the ok command.</summary>
      public void OkCommand() => RequestClose?.Invoke(true);
      /// <summary>Whether the ok command may run now.</summary>
      public bool OkCommandAllowed() {
         try {
            return !string.IsNullOrWhiteSpace(Folder) && Path.GetFullPath(Folder).Length > 0;
         }
         catch (Exception x) when (x is ArgumentException or NotSupportedException or IOException) { return false; }
      }
      /// <summary>Runs the browse command.</summary>
      public void BrowseCommand() {
         var picker = new OpenFolderDialog { Title = "Choose the profiles folder" };
         if (picker.ShowDialog(DialogOwner) == true) Folder = picker.FolderName;
      }
      /// <summary>Runs the copy command.</summary>
      public void CopyCommand() {
         try { Clipboard.SetText(Folder); }
         catch (Exception x) { DialogOwner?.ShowMboxError(x.Message); }
      }
      /// <summary>Runs the reset command.</summary>
      public void ResetCommand() => Folder = ReleaseManagerPreferences.DefaultProfilesFolder;
   }
}
