using Em.Ui.Wpf.Windows;
using System.Windows;
using System.Windows.Controls;

namespace Em.Ui.Wpf.Navigations;

/// <summary>
/// Dialog kecil untuk membuat atau mengubah feed NuGet: slug (hanya saat membuat), nama, dan deskripsi.
/// Dialog hanya mengumpulkan teks; validasi dan penyimpanan tetap di layanan.
/// </summary>
public sealed class NuPakFeedDialog : EmWindow
{
   private readonly TextBox _slug = new();
   private readonly TextBox _name = new();
   private readonly TextBox _description = new();

   public string Slug => _slug.Text.Trim();
   public string FeedName => _name.Text.Trim();
   public string Description => _description.Text.Trim();

   /// <param name="slug">Slug feed yang diubah; null saat membuat feed baru.</param>
   public NuPakFeedDialog(string? slug, string name, string? description)
   {
      var creating = slug is null;
      Title = creating ? "New feed" : "Edit feed";
      Width = 460;
      SizeToContent = SizeToContent.Height;
      ResizeMode = ResizeMode.NoResize;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Em.Ui.Wpf.Core;component/Styles/MaterialDesign.xaml", UriKind.Relative) });
      ShowMinimizeButton = false;

      var panel = new StackPanel { Margin = new Thickness(22) };
      Content = panel;
      AddField(panel, "Slug", _slug, "Immutable lowercase slug, used in the feed address.", creating ? "" : slug!, creating);
      AddField(panel, "Name", _name, "Feed display name.", name, true);
      AddField(panel, "Description", _description, "Optional.", description ?? "", true);

      var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
      panel.Children.Add(buttons);
      var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
      cancel.SetResourceReference(StyleProperty, "outlinedButtonStyle");
      var save = new Button { Content = creating ? "Create" : "Save", IsDefault = true };
      save.SetResourceReference(StyleProperty, "filledButtonStyle");
      save.Click += (_, _) => DialogResult = true;
      buttons.Children.Add(cancel);
      buttons.Children.Add(save);
      Loaded += (_, _) => (creating ? _slug : _name).Focus();
   }

   private static void AddField(Panel panel, string label, TextBox box, string tip, string text, bool enabled)
   {
      var caption = new TextBlock { Text = label };
      caption.SetResourceReference(StyleProperty, "fieldLabelStyle");
      panel.Children.Add(caption);
      box.Text = text;
      box.IsEnabled = enabled;
      box.ToolTip = tip;
      box.Margin = new Thickness(0, 0, 0, 12);
      box.SetResourceReference(StyleProperty, "fieldBoxStyle");
      panel.Children.Add(box);
   }
}
