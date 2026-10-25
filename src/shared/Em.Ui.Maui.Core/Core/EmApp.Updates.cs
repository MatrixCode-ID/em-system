using Em.Ui.Core.Release.Update;
using Em.Ui.Maui.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application = Microsoft.Maui.Controls.Application;

namespace Em.Ui.Maui.Core;

public partial class EmApp {
   private readonly SemaphoreSlim _updateGate = new(1, 1);
   public static string InstalledAndroidVersion {
      get {
#if ANDROID
         var installed = AndroidUpdateInstaller.Installed();
         return $"{installed.Version} ({installed.Code})";
#else
         return "";
#endif
      }
   }

   /// <summary>Checks the embedded release source before login or on explicit request.</summary>
   public async Task<bool> CheckForUpdatesAsync(bool manual = false) {
      if (!await _updateGate.WaitAsync(0)) return false;
      try {
         if (RootPage is not { } root || Application.Current is null) return true;
         ReleaseUpdateSource? source = null;
         ReleaseUpdateResult result;
         try {
            source = ReleaseUpdateSource.FromAssembly(Application.Current.GetType().Assembly);
#if ANDROID
            var installed = AndroidUpdateInstaller.Installed();
            using var client = new HttpClient();
            result = await new ReleaseUpdateChecker(client).CheckAsync(source, installed.Id, installed.Code);
#else
            result = new(ReleaseUpdateStatus.NotConfigured);
#endif
         } catch (Exception ex) { result = new(ReleaseUpdateStatus.Failed, Error: ex.Message); }
         if (result.Status is ReleaseUpdateStatus.Failed or ReleaseUpdateStatus.NotConfigured) {
            ServiceProvider.GetService<ILogger<EmApp>>()?.LogInformation("Android update check: {Status}: {Reason}", result.Status, result.Error);
            if (manual) await root.DisplayAlertAsync("Updates", result.Status == ReleaseUpdateStatus.NotConfigured ? "Updates are not configured for this build" : result.Error ?? "Update check failed", "OK");
            return true;
         }
         if (result.Status == ReleaseUpdateStatus.UpToDate) {
            if (manual) await root.DisplayAlertAsync("Updates", "This application is up to date.", "OK");
            return true;
         }
         var vm = new ReleaseUpdatePageVm {
            Mandatory = result.Status == ReleaseUpdateStatus.Mandatory, Source = source!, Result = result,
            VersionText = $"Installed: {InstalledAndroidVersion}\nAvailable: {result.Manifest!.Android!.VersionName} ({result.Manifest.Android.VersionCode})"
         };
         var page = new ReleaseUpdatePage(vm);
         await root.Navigation.PushModalAsync(page);
         var proceed = await vm.Completion.Task;
         if (proceed) await root.Navigation.PopModalAsync();
         return proceed;
      } finally { _updateGate.Release(); }
   }
}
