using System.Windows;
using System.Windows.Controls;
using Em.Api.Core.Models;
using Em.Ui.Wpf.Shared;
namespace Em.Ui.Wpf.Navigations;

public partial class NuPakManager
{
   private NuPakPrefixInfo? Prefix => prefixes.SelectedItem as NuPakPrefixInfo;
   private NuPakPackageInfo? Package => packages.SelectedItem as NuPakPackageInfo;
   private NuPakVersionInfo? Version => versions.SelectedItem as NuPakVersionInfo;
   private Task RefreshPackages() => Run("packages",async()=> {
      var f=FeedId;var id=Prefix?.Id;var skip=_packageSkip;var text=search.Text;var generation=_generation;
      var rows=f is null||id is null?[]:await _service!.GetMeta_NuPakPackages(f,id,text,skip,100);
      if(generation==_generation&&id==Prefix?.Id&&skip==_packageSkip&&text==search.Text)packages.ItemsSource=rows;
   });
   private Task RefreshVersions() => Run("versions",async()=> {
      var f=FeedId;var id=Package?.Id;var generation=_generation;
      var rows=f is null||id is null?[]:await _service!.GetMeta_NuPakVersions(f,id);
      if(generation==_generation&&id==Package?.Id)versions.ItemsSource=rows;
   });
   private Task RefreshBin() => Run("bin",async()=> {
      var f=FeedId;var id=_binPrefix;var skip=_binSkip;var generation=_generation;
      var rows=f is null?[]:await _service!.GetMeta_NuPakRecycleBin(f,id,skip,100);
      if(generation==_generation&&id==_binPrefix&&skip==_binSkip)bin.ItemsSource=rows;
   });
   private async void PackageSelected(object s, SelectionChangedEventArgs e) { versions.ItemsSource = null; versionDetail.Text = ""; await RefreshVersions(); }
   private void VersionSelected(object s, SelectionChangedEventArgs e) { versionDetail.Text = Version is { } v ? $"{v.Original} · {(v.Prerelease ? "Prerelease" : "Stable")}\nSHA-512: {v.Hash}\nPushed by {v.PushedBy ?? "Deleted robot"} at {v.PushedAt:u}" : ""; }
   private async void PackagesRefresh(object s, RoutedEventArgs e) => await RefreshPackages();
   private async void VersionsRefresh(object s, RoutedEventArgs e) => await RefreshVersions();
   private async void BinRefresh(object s, RoutedEventArgs e) => await RefreshBin();
   private async void SearchClick(object s, RoutedEventArgs e) { _packageSkip = 0; await RefreshPackages(); }
   private async void PackagesPrevious(object s, RoutedEventArgs e) { _packageSkip = Math.Max(0, _packageSkip - 100); await RefreshPackages(); }
   private async void PackagesNext(object s, RoutedEventArgs e) { if (packages.Items.Count < 100) return; _packageSkip += 100; await RefreshPackages(); }
   private async void BinPrevious(object s, RoutedEventArgs e) { _binSkip = Math.Max(0, _binSkip - 100); await RefreshBin(); }
   private async void BinNext(object s, RoutedEventArgs e) { if (bin.Items.Count < 100) return; _binSkip += 100; await RefreshBin(); }
   private async void AllBinClick(object s, RoutedEventArgs e) { _binPrefix = null; _binSkip = 0; await RefreshBin(); }
   private async Task Mutate(Func<Task> action) {
      await Run("mutation", action);
      await Task.WhenAll(RefreshVersions(), RefreshPackages(), RefreshBin(), RefreshSettings(), RefreshFeed(), RefreshPrefixes(), RefreshAudit());
   }
   private async void RecycleClick(object s, RoutedEventArgs e) { if (FeedId is {} f && Version is { } v) await Mutate(() => _service!.PostMeta_NuPakVersionRecycle(f,v.Id)); }
   private async void RestoreClick(object s, RoutedEventArgs e) { if (FeedId is {} f && bin.SelectedItem is NuPakVersionInfo v) await Mutate(() => _service!.PostMeta_NuPakVersionRestore(f,v.Id)); }
   private async void PurgeClick(object s, RoutedEventArgs e) {
      if (CanManage && FeedId is {} f && bin.SelectedItem is NuPakVersionInfo v && Confirm($"Permanently delete {v.PackageName} {v.Version} ({NuPakDisplay.Size(v.Bytes)})?")) await Mutate(() => _service!.PostMeta_NuPakVersionPurge(f,v.Id));
   }
   private async void EmptyClick(object s, RoutedEventArgs e) {
      if (!CanManage || _busy.Contains("mutation") || _feed is not {} feed) return;
      var f=feed.Id;var prefix=_binPrefix;
      await Run("mutation", async () => {
         var all = new List<NuPakVersionInfo>(); int skip = 0;
         while (true) { var page = await _service!.GetMeta_NuPakRecycleBin(f,prefix, skip, 100); all.AddRange(page); if (page.Length < 100) break; skip += page.Length; }
         if (!Confirm($"Permanently delete from feed {feed.Name} ({feed.Slug}), {(prefix is null?"all prefixes":"selected prefix")}: {all.Count:N0} recycled versions ({NuPakDisplay.Size(all.Sum(v => v.Bytes))})?")) return;
         var result = await _service!.PostMeta_NuPakRecycleBinEmpty(f,prefix);
         message.Text = $"Purged {result.Count:N0} versions, freed {NuPakDisplay.Size(result.Bytes)}. Failed: {result.Failed}.";
      });
      await RefreshAllAsync();
   }
   private void CopyReference(object s, RoutedEventArgs e) { if (Version is { } v) Copy($"<PackageReference Include=\"{v.PackageName}\" Version=\"{v.Version}\" />"); }
   private void CopyCommand(object s, RoutedEventArgs e) { if (Version is { } v) Copy($"dotnet add package {v.PackageName} --version {v.Version}"); }
}
