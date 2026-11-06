using Em.Api.Core.Models;
using Em.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace Em.Api.Core.NuPak;
internal sealed class NuPakStartup(IServiceProvider services) : IHostedService
{
   public async Task StartAsync(CancellationToken ct) {
      using var scope = services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<NuPakDbContext>();
      try {
         await EmSqlDialects.For(db).VerifySchemaAsync(db, new EmSchemaRequirement {
            ForeignKeys = ["FK_NuPakPackagePrefixFeed", "FK_NuPakPrefixFeed", "FK_NuPakPackageFeed", "FK_NuPakAuditFeed"],
            Views = ["vi_NuPakFeed"],
            UniqueConstraints = ["UQ_NuPakFeedSlug", "UQ_NuPakPrefixFeedId", "UQ_NuPakPrefix", "UQ_NuPakPackage", "UQ_NuPakVersion"]
         }, "NuPak multi-feed schema is incomplete. Run the upgrade and latest set scripts.", ct);
         if(await db.Meta.Where(m=>m.cMetaKey=="NuPakSchemaVersion").Select(m=>m.cMetaValue).SingleOrDefaultAsync(ct)!="2")
            throw new InvalidOperationException("NuPak schema version 2 is required.");
         await db.Feeds.FirstOrDefaultAsync(ct);
         await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Prefixes.OrderBy(r => r.cNuPakPrefixId), ct);
         await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Packages.OrderBy(r => r.cNuPakPackageId), ct);
         await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Versions.OrderBy(r => r.cNuPakVersionId), ct);
         await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Grants.OrderBy(r => r.cRobotId), ct);
         await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Audits.OrderBy(r => r.cNuPakAuditId), ct);
      } catch (Exception ex) { throw new InvalidOperationException($"NuGet module schema is unavailable. For legacy installations run {EmSqlDialects.TryFor(db)?.ScriptFolder}/updates/20261003-NuPakMultiFeed.sql, then tables/040-nupak.sql and views/vi_NuPak*.sql. For new installations run tables/040-nupak.sql and views/vi_NuPak*.sql. Never run the old binary on this schema.", ex); }
      var store = services.GetRequiredService<NuPakStore>();
      store.Initialize();
      await store.RecoverPurgesAsync(db, ct);
   }
   public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
