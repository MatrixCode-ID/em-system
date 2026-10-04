using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Api.Shared;

// ReSharper disable once CheckNamespace
public static class Extensions
{
   // UseEmProvider runs for every DbContext instance; AutoDetect opens a connection, so detect once per database.
   private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ServerVersion> mySqlVersions = new();

   public static void UseEm(this WebApplicationBuilder app, Action<EmApp> svc) {
      // var emApp = new EmApp() {
      //    Builder = app,
      // };
      // emApp.AddService<IEmApiCoreServices>(() => new CoreServices());
      // app.Services.AddSingleton(emApp);
      // svc(emApp);
   }

   public static void UseEmProvider(this DbContextOptionsBuilder opt, DatabaseProvider provider,
      string connectionString) {
      switch (provider) {
         case DatabaseProvider.MicrosoftSqlServer:
            opt.UseSqlServer(connectionString);
            // Lets approval move one shared connection between databases; inert outside its transaction.
            opt.AddInterceptors(Em.Api.Core.Approval.ApprovalDatabaseSwitch.Instance);
            break;
         case DatabaseProvider.MySql:
            opt.UseMySql(connectionString, mySqlVersions.GetOrAdd(connectionString, ServerVersion.AutoDetect));
            break;
         case DatabaseProvider.PostgreSql:
            opt.UseNpgsql(connectionString);
            break;
         default:
            throw new InvalidOperationException($"Unhandled database provider '{provider}'.");
      }
   }
}