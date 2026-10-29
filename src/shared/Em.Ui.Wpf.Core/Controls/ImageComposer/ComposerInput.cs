using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Em.Ui.Wpf.Dialogs;
using Microsoft.Win32;

[assembly: InternalsVisibleTo("Em.Ui.Wpf.Core.Tests")]

namespace Em.Ui.Wpf.Controls;

internal interface IComposerInput
{
   bool CanPaste();
   BitmapSource? ClipboardImage();
   IEnumerable<string> ClipboardFiles();
   IEnumerable<string> PickFiles(Window? owner);
   bool ConfirmClear(Window? owner);
   void Error(Window? owner, string message);
}

internal sealed class ComposerInput : IComposerInput
{
   internal static bool IsImageFile(string path) => Path.GetExtension(path).ToLowerInvariant() is
      ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff";
   public bool CanPaste() {
      try { return Clipboard.ContainsImage() || ClipboardFiles().Any(IsImageFile); }
      catch (ExternalException) { return false; }
   }
   public BitmapSource? ClipboardImage() => Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
   public IEnumerable<string> ClipboardFiles() => Clipboard.ContainsFileDropList()
      ? Clipboard.GetFileDropList().Cast<string>().Where(IsImageFile).ToArray() : [];
   public IEnumerable<string> PickFiles(Window? owner) {
      var dialog = new OpenFileDialog { Multiselect = true, Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
      return dialog.ShowDialog(owner) == true ? dialog.FileNames : [];
   }
   public bool ConfirmClear(Window? owner) => EmMessageBox.Show(owner, "Clear canvas",
      "Clear all images and annotations? This cannot be undone.", MessageBoxButton.YesNo,
      MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
   public void Error(Window? owner, string message) => EmMessageBox.Show(owner, "Image composer", message,
      MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK);
}
