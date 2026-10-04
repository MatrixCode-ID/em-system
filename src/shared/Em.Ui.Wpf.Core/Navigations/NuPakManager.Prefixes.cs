using System.Windows;
using System.Windows.Controls;
using Em.Api.Core.Models;
namespace Em.Ui.Wpf.Navigations;
public partial class NuPakManager
{
   private Task RefreshPrefixes() => Run("prefixes",async()=> {
      var f=FeedId;var id=Prefix?.Id;var generation=_generation;
      var rows=f is null?[]:await _service!.GetMeta_NuPakPrefixes(f);
      if(generation!=_generation)return;prefixes.ItemsSource=rows;prefixes.SelectedItem=rows.FirstOrDefault(r=>r.Id==id);
   });
   private Task RefreshAccess() => Run("access",async()=> {
      var f=FeedId;var p=Prefix?.Id;var generation=_generation;
      var rows=f is null||p is null?[]:await _service!.GetMeta_NuPakPrefixAccess(f,p);
      if(generation==_generation&&p==Prefix?.Id)access.ItemsSource=rows;
   });
   private async void PrefixSelected(object s, SelectionChangedEventArgs e) {
      prefixName.Text = Prefix?.Name ?? ""; prefixDescription.Text = Prefix?.Description ?? ""; prefixActive.IsChecked = Prefix?.Active ?? true;
      _packageSkip = _binSkip = 0; _binPrefix = Prefix?.Id; packages.ItemsSource = versions.ItemsSource = null;
      await Task.WhenAll(RefreshPackages(), RefreshAccess(), RefreshBin());
   }
   private async void PrefixesRefresh(object s, RoutedEventArgs e) => await RefreshPrefixes();
   private async void AccessRefresh(object s, RoutedEventArgs e) => await RefreshAccess();
   private async void CreatePrefix(object s, RoutedEventArgs e) { if(FeedId is {} f) {var name=prefixName.Text;var description=prefixDescription.Text;await Mutate(async () => { await _service!.PostGetMeta_NuPakPrefixCreate(f,name,description); });} }
   private async void UpdatePrefix(object s, RoutedEventArgs e) { if (FeedId is {} f && Prefix is { } p) await Mutate(async () => { await _service!.PostGetMeta_NuPakPrefixUpdate(f,p.Id, prefixName.Text, prefixDescription.Text, prefixActive.IsChecked == true); }); }
   private async void DeletePrefix(object s, RoutedEventArgs e) { if (FeedId is {} f && Prefix is { } p && Confirm($"Delete prefix {p.Name} and its robot grants? Packages, including recycled versions, must be purged first.")) await Mutate(() => _service!.PostMeta_NuPakPrefixDelete(f,p.Id)); }
   private async void OpenUsers(object s, RoutedEventArgs e) { if (_app is not null) await Run("navigation", () => _app.NavigateTo("admin.users")); }
}
