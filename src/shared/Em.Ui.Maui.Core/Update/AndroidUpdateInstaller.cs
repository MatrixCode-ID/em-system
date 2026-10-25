#if ANDROID
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui.ApplicationModel;
using Application = Android.App.Application;

[assembly: UsesPermission("android.permission.REQUEST_INSTALL_PACKAGES")]

namespace Em.Ui.Maui.Update;

internal static class AndroidUpdateInstaller {
   public static (string Id, string Version, long Code) Installed() {
      var context = Application.Context;
      var info = context.PackageManager!.GetPackageInfo(context.PackageName!, PackageInfoFlags.MetaData)!;
      return (context.PackageName!, info.VersionName ?? "", OperatingSystem.IsAndroidVersionAtLeast(28) ? info.LongVersionCode : info.VersionCode);
   }

   public static async Task InstallAsync(string apk, CancellationToken token) {
      var context = Application.Context;
      if (OperatingSystem.IsAndroidVersionAtLeast(26) && !context.PackageManager!.CanRequestPackageInstalls()) {
         context.StartActivity(new Intent(Android.Provider.Settings.ActionManageUnknownAppSources, Android.Net.Uri.Parse("package:" + context.PackageName)).AddFlags(ActivityFlags.NewTask));
         throw new InvalidOperationException("Allow this app to install updates, return here, then tap Update again.");
      }
      var installer = context.PackageManager!.PackageInstaller!;
      using var parameters = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
      var sessionId = installer.CreateSession(parameters);
      using var session = installer.OpenSession(sessionId)!;
      var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var action = context.PackageName + ".UPDATE_RESULT." + Guid.NewGuid().ToString("N");
      using var receiver = new InstallReceiver(result);
      if (OperatingSystem.IsAndroidVersionAtLeast(33)) context.RegisterReceiver(receiver, new IntentFilter(action), ReceiverFlags.NotExported);
      else context.RegisterReceiver(receiver, new IntentFilter(action));
      try {
         await using var input = File.OpenRead(apk);
         using (var output = session.OpenWrite("base.apk", 0, input.Length)!) {
            await input.CopyToAsync(output, token); session.Fsync(output);
         }
         var flags = PendingIntentFlags.UpdateCurrent;
         if (OperatingSystem.IsAndroidVersionAtLeast(31)) flags |= PendingIntentFlags.Mutable;
         using var pending = PendingIntent.GetBroadcast(context, sessionId, new Intent(action).SetPackage(context.PackageName), flags)!;
         session.Commit(pending.IntentSender);
         await result.Task.WaitAsync(token);
      }
      catch { try { installer.AbandonSession(sessionId); } catch (Java.Lang.Exception) { } throw; }
      finally { context.UnregisterReceiver(receiver); }
   }

   private sealed class InstallReceiver(TaskCompletionSource completion) : BroadcastReceiver {
      public override void OnReceive(Context? context, Intent? intent) {
         if (intent is null || context is null) return;
         var status = intent.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure);
         if (status == (int)PackageInstallStatus.PendingUserAction) {
            try {
               var confirmation = intent.GetParcelableExtra(Intent.ExtraIntent) as Intent ?? throw new InvalidOperationException("The installer supplied no confirmation intent.");
               context.StartActivity(confirmation.AddFlags(ActivityFlags.NewTask));
            } catch (Exception ex) { completion.TrySetException(ex); }
         } else if (status == (int)PackageInstallStatus.Success) completion.TrySetResult();
         else completion.TrySetException(new IOException("Android installation failed: " + intent.GetStringExtra(PackageInstaller.ExtraStatusMessage)));
      }
   }
}
#endif
