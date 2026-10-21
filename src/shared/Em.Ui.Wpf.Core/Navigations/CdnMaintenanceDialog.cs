using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Em.Api.Core.Models;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Navigations;

internal sealed class CdnMaintenanceDialog : EmWindow {
   private readonly ICdnServices _service;
   private readonly ListBox _recycle = new() { DisplayMemberPath = nameof(CdnRecycleItem.Path) };
   private readonly ListBox _managed = new() { DisplayMemberPath = nameof(CdnManagedFolder.Path) };
   private readonly TextBlock _summary = new() { Margin = new Thickness(0, 12, 0, 12) };
   private readonly TextBlock _usage = new() { Margin = new Thickness(0, 12, 0, 8) };
   private readonly TextBox _quota = new() { MinWidth = 180 };
   private readonly StackPanel _panel = new() { Margin = new Thickness(22, 12, 22, 22) };
   private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };
   private bool _busy;

   internal CdnMaintenanceDialog(ICdnServices service) {
      _service = service;
      Title = "CDN maintenance"; Width = 840; Height = 680; ShowMinimizeButton = false;
      Resources.MergedDictionaries.Add(new ResourceDictionary {
         Source = new Uri("pack://application:,,,/Em.Ui.Wpf.Core;component/Styles/MaterialDesign.xaml")
      });
      _recycle.SetResourceReference(StyleProperty, "surfaceListBoxStyle");
      _managed.SetResourceReference(StyleProperty, "surfaceListBoxStyle");
      _recycle.Height = 160; _managed.Height = 120;
      var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
      AddButton(toolbar, "Refresh", Refresh);
      AddButton(toolbar, "Clear Temporary Files", ClearTemporary);
      _panel.Children.Add(toolbar); _panel.Children.Add(_summary);
      _panel.Children.Add(new TextBlock { Text = "Recycle bin" }); _panel.Children.Add(_recycle);
      var recycleActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 16) };
      AddButton(recycleActions, "Restore", async () => {
         if (_recycle.SelectedItem is CdnRecycleItem item) await _service.PostMeta_CdnRestore(item.Id);
      });
      AddButton(recycleActions, "Delete permanently", async () => {
         if (_recycle.SelectedItem is CdnRecycleItem item && Confirm($"Permanently delete '{item.Path}' ({CdnManagerVm.FormatSize(item.Size)})?"))
            await _service.PostMeta_CdnRecycleDelete(item.Id);
      });
      AddButton(recycleActions, "Empty Recycle Bin", async () => {
         var items = await _service.GetMeta_CdnRecycle();
         if (Confirm($"Permanently delete {items.Length:N0} items ({CdnManagerVm.FormatSize(items.Sum(i => i.Size))})? Open downloads are skipped."))
            await _service.PostMeta_CdnRecycleEmpty();
      });
      _panel.Children.Add(recycleActions);
      _panel.Children.Add(new TextBlock { Text = "Managed folders (read-only in CDN Manager)" });
      _panel.Children.Add(_managed); _panel.Children.Add(_usage);
      var quotaPanel = new StackPanel { Orientation = Orientation.Horizontal,
         ToolTip = "Quota in bytes, zero or greater. Empty means unlimited. Reducing below usage keeps existing files." };
      ToolTipService.SetShowOnDisabled(quotaPanel, true);
      NumericInput.SetKind(_quota, NumericInputKind.Integer);
      quotaPanel.Children.Add(new TextBlock { Text = "Quota (bytes)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
      quotaPanel.Children.Add(_quota);
      AddButton(quotaPanel, "Save quota", async () => {
         if (_managed.SelectedItem is CdnManagedFolder item)
            await _service.PostMeta_CdnManagedQuota(item.Path, string.IsNullOrWhiteSpace(_quota.Text) ? null : long.Parse(_quota.Text, CultureInfo.CurrentCulture));
      });
      AddButton(quotaPanel, "Release", async () => {
         if (_managed.SelectedItem is not CdnManagedFolder item) return;
         var dialog = new TextInputDialog("Release managed folder", $"Type '{Path.GetFileName(item.Path)}' to allow ordinary CDN changes.", "Folder name", "Release") { Owner = this };
         if (dialog.ShowDialog() == true) await _service.PostMeta_CdnManagedRelease(item.Path, dialog.Vm.Result);
      });
      _panel.Children.Add(quotaPanel); _panel.Children.Add(_error);
      _managed.SelectionChanged += (_, _) => {
         if (_managed.SelectedItem is not CdnManagedFolder item) return;
         _quota.Text = item.QuotaBytes?.ToString(CultureInfo.CurrentCulture) ?? "";
         _usage.Text = $"Owner: {item.Owner.Id} · {CdnManagerVm.FormatSize(item.UsedBytes)} / {(item.QuotaBytes is { } q ? CdnManagerVm.FormatSize(q) : "Unlimited")}" +
            (item.QuotaBytes is { } quota && item.UsedBytes > quota ? " · OVER QUOTA" : "");
      };
      Content = new ScrollViewer { Content = _panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
      Loaded += async (_, _) => await Run(Refresh);
   }

   private bool Confirm(string text) => this.ShowMboxDecideWarning(text, Title) == MessageBoxResult.Yes;
   private void AddButton(Panel panel, string text, Func<Task> action) {
      var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) };
      button.SetResourceReference(StyleProperty, "outlinedButtonStyle");
      button.Click += async (_, _) => await Run(async () => { await action(); await Refresh(); });
      panel.Children.Add(button);
   }
   private async Task Run(Func<Task> action) {
      if (_busy) return;
      _busy = true; _panel.IsEnabled = false; _error.Text = "";
      try { await action(); }
      catch (Exception ex) { _error.Text = ex.Message; }
      finally { _busy = false; _panel.IsEnabled = true; }
   }
   private async Task Refresh() {
      var recycle = await _service.GetMeta_CdnRecycle();
      var garbage = await _service.GetMeta_CdnTemporaryGarbage();
      _recycle.ItemsSource = recycle;
      _managed.ItemsSource = await _service.GetMeta_CdnManagedFolders();
      _summary.Text = $"Recycle: {recycle.Length:N0} items, {CdnManagerVm.FormatSize(recycle.Sum(i => i.Size))} · Temporary garbage: {garbage.FileCount:N0} items, {CdnManagerVm.FormatSize(garbage.TotalBytes)}";
   }
   private async Task ClearTemporary() {
      var garbage = await _service.GetMeta_CdnTemporaryGarbage();
      if (Confirm($"Delete {garbage.FileCount:N0} temporary garbage items ({CdnManagerVm.FormatSize(garbage.TotalBytes)})? Active operations are excluded."))
         await _service.PostMeta_CdnTemporaryClear();
   }
}
