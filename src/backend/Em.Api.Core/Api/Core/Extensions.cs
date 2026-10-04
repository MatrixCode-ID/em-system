using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Api.Shared;

// ReSharper disable once CheckNamespace
public static class Extensions
{
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
            opt.UseMySQL(connectionString);
            break;
         case DatabaseProvider.PostgreSql:
            opt.UseNpgsql(connectionString);
            break;
         default:
            throw new InvalidOperationException($"Unhandled database provider '{provider}'.");
      }
   }
}