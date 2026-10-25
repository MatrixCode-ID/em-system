using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Logging;

namespace Em.Ui.Maui.Core
{
   public partial class EmApp
   {
      private ClientLogFolder _logFolder = null!;
      private ClientLogProvider _logProvider = null!;
      private ClientLogShipper _logShipper = null!;

      /// <summary>
      /// Logger factory of the client log engine: entries go to the local outbox under
      /// <c>FileSystem.AppDataDirectory/Logs/outbox</c> and are sent to the server after sign-in.
      /// </summary>
      public ILoggerFactory LoggerFactory { get; private set; } = null!;

      /// <summary>Folder of the local log outbox.</summary>
      public string LogFolder => _logFolder.Path;

      /// <summary>Sends the local log outbox now.</summary>
      public Task<int> SendLogsAsync() => _logShipper.SendAsync();

      // Called at the end of BuildApp's registration phase, once ApplicationName is known.
      private static void InitClientLog(EmApp app) {
         var options = new ClientLogOptions {
            Folder = app.AppId.Length > 0 ? Path.Combine(FileSystem.AppDataDirectory, app.AppId, "Logs", "outbox") : Path.Combine(FileSystem.AppDataDirectory, "Logs", "outbox"),
            App = app.AppId.Length > 0 ? app.AppId : app.ApplicationName,
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

         var logger = app.LoggerFactory.CreateLogger("Em.Ui.Unhandled");
         TaskScheduler.UnobservedTaskException += (_, e) => {
            logger.LogError(e.Exception, "Unobserved task exception: {Message}", e.Exception.Message);
            e.SetObserved();
         };
         AppDomain.CurrentDomain.UnhandledException += (_, e) => {
            if (e.ExceptionObject is Exception ex) logger.LogCritical(ex, "Unhandled exception, the application stops: {Message}", ex.Message);
         };
         app.ActiveUserChanged += (_, _) => {
            if (app.ActiveUser is not null) app._logShipper.Start();
            else app._logShipper.Stop();
         };
         // Debug mode signed its user in during BuildApp, before this subscription existed.
         if (app.ActiveUser is not null) app._logShipper.Start();
      }

      // Before the server is told about a sign-out, what is still local is sent with the session that wrote it.
      private async Task FlushLogsBeforeSignOutAsync() {
         try {
            await Task.WhenAny(_logShipper.SendAsync(), Task.Delay(TimeSpan.FromSeconds(5)));
         }
         catch {
            // Never holds the user back.
         }
         _logShipper.Stop();
      }
   }
}
