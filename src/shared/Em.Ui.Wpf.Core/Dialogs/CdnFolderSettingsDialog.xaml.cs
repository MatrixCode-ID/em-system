using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Edits the settings of one CDN folder: public listing and the hidden/shown name patterns. Saving
   /// replaces the settings as a whole; Export writes the canonical settings file to disk. Managed folders
   /// open read-only.
   /// </summary>
   public partial class CdnFolderSettingsDialog : EmWindow
   {
      /// <summary>Creates the dialog for the folder described by <paramref name="detail"/>.</summary>
      /// <param name="service">CDN service used to save and export.</param>
      /// <param name="detail">Current settings as read from the server.</param>
      public CdnFolderSettingsDialog(ICdnServices service, CdnFolderSettingsDetail detail) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Load(service, detail);
         Vm.RequestClose += result => DialogResult = result;
      }

      /// <summary>The view model of this dialog.</summary>
      public CdnFolderSettingsDialogVm Vm => (CdnFolderSettingsDialogVm)DataContext;
   }

   /// <summary>One editable name pattern in <see cref="CdnFolderSettingsDialog"/>.</summary>
   public class CdnPatternRow : NotifyPropertyBase
   {
      internal CdnPatternRow(ObservableCollection<CdnPatternRow> list, string text) {
         List = list;
         Text = text;
      }

      internal ObservableCollection<CdnPatternRow> List { get; }

      /// <summary>The pattern as typed.</summary>
      public string Text {
         get => Get<string>() ?? "";
         set => Set(value);
      }
   }

   /// <summary>View model for <see cref="CdnFolderSettingsDialog"/>.</summary>
   public class CdnFolderSettingsDialogVm : MvvmModelBase
   {
      private ICdnServices? _service;
      private string _path = "";
      private bool _canExport;

      /// <summary>Creates the view model and registers its commands.</summary>
      public CdnFolderSettingsDialogVm() {
         RegisterCommand(nameof(AddHiddenCommand), AddHiddenCommand, AddHiddenCommandAllowed);
         RegisterCommand(nameof(AddShownCommand), AddShownCommand, AddShownCommandAllowed);
         RegisterCommand<CdnPatternRow?>(nameof(RemovePatternCommand), RemovePatternCommand, RemovePatternCommandAllowed);
         RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
         RegisterCommand(nameof(ExportCommand), ExportCommand, ExportCommandAllowed);
      }

      /// <summary>Raised when the dialog should close; <c>true</c> after a successful save.</summary>
      public event Action<bool>? RequestClose;

      internal void Load(ICdnServices service, CdnFolderSettingsDetail detail) {
         _service = service;
         _path = detail.Path;
         _canExport = detail.Exists && detail.Warning is null;
         Caption = $"Settings of '{(detail.Path.Length == 0 ? "cdn" : detail.Path)}'. They apply to this folder only.";
         Warning = detail.Warning is null ? "" : $"The existing settings file is invalid and is ignored; saving replaces it.\n{detail.Warning}";
         IsReadOnly = detail.IsReadOnly;
         PublicListing = detail.Settings.PublicListing;
         HiddenFiles.Clear();
         foreach (var pattern in detail.Settings.HiddenFiles) HiddenFiles.Add(new CdnPatternRow(HiddenFiles, pattern));
         ShownFiles.Clear();
         foreach (var pattern in detail.Settings.ShownFiles) ShownFiles.Add(new CdnPatternRow(ShownFiles, pattern));
         RaiseCommandsChanged();
      }

      /// <summary>Which folder is edited.</summary>
      public string Caption { get => Get<string>() ?? ""; private set => Set(value); }

      /// <summary>Why the existing settings file was ignored; empty when it is valid or missing.</summary>
      public string Warning { get => Get<string>() ?? ""; private set => Set(value, _ => NotifyChanged(nameof(HasWarning))); }

      /// <summary><c>true</c> when <see cref="Warning"/> has text.</summary>
      public bool HasWarning => Warning.Length > 0;

      /// <summary><c>true</c> for a managed folder: everything is shown but nothing can be changed.</summary>
      public bool IsReadOnly { get => Get<bool>(); private set => Set(value, _ => NotifyChanged(nameof(IsEditable))); }

      /// <summary>The opposite of <see cref="IsReadOnly"/>, for enabling inputs.</summary>
      public bool IsEditable => !IsReadOnly;

      /// <summary><c>true</c> while saving or exporting.</summary>
      public bool IsWorking { get => Get<bool>(); private set => Set(value, _ => RaiseCommandsChanged()); }

      /// <summary>Public listing switch.</summary>
      public bool PublicListing { get => Get<bool>(); set => Set(value); }

      /// <summary>Hidden name patterns.</summary>
      public ObservableCollection<CdnPatternRow> HiddenFiles { get; } = [];

      /// <summary>Shown name patterns, which win over hidden ones.</summary>
      public ObservableCollection<CdnPatternRow> ShownFiles { get; } = [];

      /// <summary>The last save or export error; empty when none.</summary>
      public string ErrorText { get => Get<string>() ?? ""; private set => Set(value, _ => NotifyChanged(nameof(HasError))); }

      /// <summary><c>true</c> when <see cref="ErrorText"/> has text.</summary>
      public bool HasError => ErrorText.Length > 0;

      /// <summary>Adds an empty hidden pattern.</summary>
      public void AddHiddenCommand() => HiddenFiles.Add(new CdnPatternRow(HiddenFiles, ""));

      /// <summary>Only when the settings can be changed.</summary>
      public bool AddHiddenCommandAllowed() => IsEditable && !IsWorking;

      /// <summary>Adds an empty shown pattern.</summary>
      public void AddShownCommand() => ShownFiles.Add(new CdnPatternRow(ShownFiles, ""));

      /// <summary>Only when the settings can be changed.</summary>
      public bool AddShownCommandAllowed() => IsEditable && !IsWorking;

      /// <summary>Removes one pattern row from its list.</summary>
      public void RemovePatternCommand(CdnPatternRow? row) => row?.List.Remove(row);

      /// <summary>Only for a row, when the settings can be changed.</summary>
      public bool RemovePatternCommandAllowed(CdnPatternRow? row) => row is not null && IsEditable && !IsWorking;

      /// <summary>Validates, saves on the server and closes; errors stay in the dialog.</summary>
      public async Task SaveCommand() {
         var settings = new CdnFolderSettings {
            PublicListing = PublicListing,
            HiddenFiles = HiddenFiles.Select(r => r.Text.Trim()).ToArray(),
            ShownFiles = ShownFiles.Select(r => r.Text.Trim()).ToArray()
         };
         IsWorking = true;
         try {
            ErrorText = "";
            CdnFolderSettingsFormat.Validate(settings);
            await _service!.PostMeta_CdnFolderSettingsSave(new CdnFolderSettingsSave { Path = _path, Settings = settings });
            RequestClose?.Invoke(true);
         }
         catch (Exception ex) {
            ErrorText = ex.Message.Replace("Server Error: ", "");
         }
         finally {
            IsWorking = false;
         }
      }

      /// <summary>Only when the settings can be changed.</summary>
      public bool SaveCommandAllowed() => IsEditable && !IsWorking && _service is not null;

      /// <summary>Saves the canonical settings file of this folder to a file chosen by the user.</summary>
      public async Task ExportCommand() {
         var dialog = new SaveFileDialog {
            Title = "Export folder settings",
            FileName = CdnFolderSettingsFormat.FileName,
            Filter = "Folder settings|*.meta|All files|*.*"
         };
         if (dialog.ShowDialog(MainWindow) != true) return;
         IsWorking = true;
         try {
            ErrorText = "";
            var text = await _service!.GetMeta_CdnFolderSettingsExport(_path);
            await File.WriteAllTextAsync(dialog.FileName, text);
         }
         catch (Exception ex) {
            ErrorText = ex.Message.Replace("Server Error: ", "");
         }
         finally {
            IsWorking = false;
         }
      }

      /// <summary>Only when the folder has valid settings on the server.</summary>
      public bool ExportCommandAllowed() => _canExport && !IsWorking && _service is not null;

      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }
   }
}
