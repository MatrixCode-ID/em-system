using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using FontAwesome6;
using FontAwesome6.Fonts.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Logging;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Core
{
   public partial class EmApp
   {
      /// <summary>Navigation name of the Log Manager.</summary>
      public const string LogManagerNavigationName = "admin.logmanager";

      /// <summary>Claim key that opens the Log Manager (declared by the server's <c>AddLogServer</c>).</summary>
      internal const string LogManagerClaim = Defaults.LogModuleName + ":" + ILogServices.ManagerClaim;

      private ClientLogFolder _logFolder = null!;
      private ClientLogProvider _logProvider = null!;
      private ClientLogShipper _logShipper = null!;

      /// <summary>
      /// Logger factory of the client log engine: entries go to the local outbox
      /// (<c>%LocalAppData%\&lt;ApplicationName&gt;\Logs\outbox</c>) and are sent to the server after sign-in.
      /// Screens and modules may also take <c>ILogger&lt;T&gt;</c> from <see cref="ServiceProvider"/>.
      /// </summary>
      public ILoggerFactory LoggerFactory { get; private set; } = null!;

      /// <summary>Folder of the local log outbox.</summary>
      public string LogFolder => _logFolder.Path;

      /// <summary>Sends the local log outbox now (also done after sign-in, every minute and before sign-out).</summary>
      public Task<int> SendLogsAsync() => _logShipper.SendAsync();

      // Called at the end of BuildApp's registration phase, once ApplicationName is known.
      private static void InitClientLog(EmApp app) {
         var options = new ClientLogOptions {
            Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), app.AppId ?? app.ApplicationName, "Logs", "outbox"),
            App = app.ApplicationName,
            AppVersion = AppVersion.Current
         };
         app._logFolder = new ClientLogFolder(options);
         app._logFolder.RecoverOpenSegments();
         app._logProvider = new ClientLogProvider(app._logFolder, options,
            () => new ClientLogUser(app.ActiveUser?.cUserId, app.ActiveUser?.cUserAccount, null));
         app._logShipper = new ClientLogShipper(app._logFolder, options,
            () => app.ActiveUser is not null && app.GetActiveApiClient() is not null ? app.ServiceProvider.GetRequiredService<ILogServices>() : null);
         app.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(app._logProvider));
         app.Services.AddSingleton(app.LoggerFactory);
         app.Services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
         app.Services.AddSingleton<ILogServices>(_ => new Em.Ui.Core.LogClientService(app, app.GetActiveApiClient));
         app.Services.AddSingleton<IAuditServices>(_ => new Em.Ui.Core.AuditClientService(app, app.GetActiveApiClient));
      }

      // Unhandled exceptions are written to the local outbox before anything else happens, so a crash is visible in
      // the Log Manager after the next sign-in.
      private void HookUnhandledExceptions() {
         var logger = LoggerFactory.CreateLogger("Em.Ui.Unhandled");
         App!.DispatcherUnhandledException += (_, e) => {
            logger.LogCritical(e.Exception, "Unhandled exception on the UI thread: {Message}", e.Exception.Message);
            // The error is shown and the application keeps running, the same as ShowFirstScreenAsync does.
            e.Handled = true;
            try { MainWindow.ShowMboxError(e.Exception); } catch { }
         };
         TaskScheduler.UnobservedTaskException += (_, e) => {
            logger.LogError(e.Exception, "Unobserved task exception: {Message}", e.Exception.Message);
            e.SetObserved();
         };
         AppDomain.CurrentDomain.UnhandledException += (_, e) => {
            if (e.ExceptionObject is Exception ex) logger.LogCritical(ex, "Unhandled exception, the application stops: {Message}", ex.Message);
         };
      }

      // The sender follows the session: started once someone is signed in, flushed and stopped before sign-out.
      private void OnLogUserChanged() {
         if (ActiveUser is not null) _logShipper.Start();
         else _logShipper.Stop();
      }

      // Before the server is told about a sign-out, what is still local is sent with the session that wrote it.
      private async Task FlushLogsBeforeSignOutAsync() {
         try {
            var send = _logShipper.SendAsync();
            await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(5)));
         }
         catch {
            // Never holds the user back: what was not sent stays local for the next sign-in.
         }
         _logShipper.Stop();
      }

      private StaticTool OpenLogFolderTool() => new() {
         Name = "tools.logfolder",
         Title = "Open log folder",
         Subtitle = "Local log outbox",
         Description = "Opens the folder where this client keeps its log until it is sent to the server.",
         Icon = EFontAwesomeIcon.Solid_FolderOpen.CreateImageSource(System.Windows.Media.Brushes.Gray),
         Invoke = owner => {
            try {
               Directory.CreateDirectory(LogFolder);
               Process.Start(new ProcessStartInfo(LogFolder) { UseShellExecute = true });
            }
            catch (Exception x) {
               owner.ShowMboxError(x);
            }
            return Task.CompletedTask;
         }
      };
   }
}
