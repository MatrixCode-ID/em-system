using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Em.Api.Core.Models;
using Em.Ui.Wpf.Core;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Win32;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations;

/// <summary>
/// Object Storage Manager: buckets with an object browser and version history, volumes, policies and
/// maintenance (garbage collection, retention, repair).
/// </summary>
public partial class ObjectStorageManager : UserControl, INavigationBody
{
   /// <summary>Creates the Object Storage Manager.</summary>
   public ObjectStorageManager(EmApp app) {
      InitializeComponent();
      AttachDialogs(Vm, this, app);
      serviceToggle.Attach(app, "ObjectStorage");
   }

   /// <summary>Gives the view model its confirm, open-file and save-file dialogs, owned by the window of <paramref name="view"/>.</summary>
   internal static void AttachDialogs(ObjectStorageManagerVm vm, FrameworkElement view, EmApp app) {
      vm.EmApp = app;
      Window Owner() => Window.GetWindow(view) ?? app.MainWindow;
      vm.Confirm = (message, title) => Owner().ShowMboxDecideWarning(message, title) == MessageBoxResult.Yes;
      vm.PickUploadFiles = () => {
         var dialog = new OpenFileDialog { Title = "Upload to object storage", Multiselect = true, CheckFileExists = true };
         return dialog.ShowDialog(Owner()) == true ? dialog.FileNames : null;
      };
      vm.PickSavePath = name => {
         var dialog = new SaveFileDialog { Title = "Download from object storage", FileName = name, Filter = "All files|*.*" };
         return dialog.ShowDialog(Owner()) == true ? dialog.FileName : null;
      };
   }

   /// <summary>Screen view model.</summary>
   public ObjectStorageManagerVm Vm => (ObjectStorageManagerVm)DataContext;

   private void OnRowDoubleClick(object sender, MouseButtonEventArgs args) {
      if (args.ChangedButton != MouseButton.Left || !Vm.CanBrowse || sender is not ListBoxItem item) return;
      args.Handled = true;
      switch (item.DataContext) {
         case StoreBucketInfo bucket:
            // A bucket opens in its own bucket editor tab; the in-place browser stays behind Browse.
            Vm.SelectedBucket = bucket;
            _ = Vm.NavigationEntry is null ? Vm.OpenBucketCommand() : Vm.OpenBucketPageCommand();
            break;
         case StoreBrowserItem entry:
            Vm.SelectedItem = entry;
            _ = Vm.OpenItemCommand();
            break;
         case StoreVolumeInfo volume:
            Vm.SelectedVolume = volume;
            Vm.EditVolumeCommand();
            break;
         case StorePolicyInfo policy:
            Vm.SelectedPolicy = policy;
            Vm.EditPolicyCommand();
            break;
      }
   }

   private void OnMoreClick(object sender, RoutedEventArgs args) {
      if (sender is not Button { ContextMenu: { } menu } button) return;
      menu.PlacementTarget = button;
      menu.IsOpen = true;
   }

   private void OnObjectsDragOver(object sender, DragEventArgs args) {
      args.Effects = args.Data.GetDataPresent(DataFormats.FileDrop) && Vm.IsBrowsing && Vm.CanBrowse && Vm.CanChangeContents ? DragDropEffects.Copy : DragDropEffects.None;
      args.Handled = true;
   }

   private async void OnObjectsDrop(object sender, DragEventArgs args) {
      if (!Vm.IsBrowsing || !Vm.CanBrowse || !Vm.CanChangeContents || args.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
      args.Handled = true;
      // Folders are not uploaded recursively; only dropped files are.
      await Vm.UploadFilesAsync(files.Where(System.IO.File.Exists).ToArray());
   }

   private void OnPreviewKeyDown(object sender, KeyEventArgs args) {
      if (args.Key == Key.Escape && Vm.IsEditorOpen && Vm.IsNotBusy) {
         Vm.CloseEditorCommand();
         args.Handled = true;
      }
   }

   /// <inheritdoc />
   public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Vm.IsLoaded ? Task.CompletedTask : Vm.ReloadAsync();

   /// <inheritdoc />
   public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

   /// <inheritdoc />
   public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

   /// <inheritdoc />
   public Task OnRelease(INavigation sender) {
      Vm.CloseEditorCommand();
      return Task.CompletedTask;
   }

   #region Converters

   /// <summary>Byte count to a size caption.</summary>
   public static IValueConverter SizeConverter { get; } = new FuncConverter(v => v is long b ? ObjectStorageManagerVm.FormatSize(b) : "");

   /// <summary>Optional byte limit to a caption; <c>null</c> reads "Unlimited".</summary>
   public static IValueConverter LimitConverter { get; } = new FuncConverter(v => ObjectStorageManagerVm.FormatLimit(v as long?));

   /// <summary>Volume probe result to a caption.</summary>
   public static IValueConverter ProbeConverter { get; } = new FuncConverter(v => v is true ? "OK" : "Problem");

   /// <summary>Policy to its retention caption.</summary>
   public static IValueConverter RetentionConverter { get; } = new FuncConverter(v => v is StorePolicyInfo p ? ObjectStorageManagerVm.RetentionText(p) : "");

   /// <summary>Version to a size or delete-marker caption.</summary>
   public static IValueConverter VersionConverter { get; } = new FuncConverter(v => v is StoreObjectVersionInfo { IsDeleteMarker: true } ? "Delete marker"
      : v is StoreObjectVersionInfo version ? ObjectStorageManagerVm.FormatSize(version.Size) : "");

   /// <summary>UTC time to a local time caption.</summary>
   public static IValueConverter LocalTimeConverter { get; } = new FuncConverter(v => v is DateTime t ? DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime().ToString("g") : "");

   /// <summary>Zero count to visible, anything else to collapsed.</summary>
   public static IValueConverter ZeroVisibilityConverter { get; } = new FuncConverter(v => v is 0 ? Visibility.Visible : Visibility.Collapsed);

   private sealed class FuncConverter(Func<object?, object> convert) : IValueConverter
   {
      public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => convert(value);
      public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
   }

   #endregion
}
