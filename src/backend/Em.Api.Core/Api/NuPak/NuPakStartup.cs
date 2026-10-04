using Em.Api.Core.Models;
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
         await db.Database.ExecuteSqlRawAsync("""
            IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_NuPakPackagePrefixFeed' AND is_disabled=0 AND is_not_trusted=0)
            OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_NuPakPrefixFeed' AND is_disabled=0 AND is_not_trusted=0)
            OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_NuPakPackageFeed' AND is_disabled=0 AND is_not_trusted=0)
            OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_NuPakAuditFeed' AND is_disabled=0 AND is_not_trusted=0)
            OR OBJECT_ID('dbo.vi_NuPakFeed','V') IS NULL
            OR NOT EXISTS(SELECT 1 FROM sys.key_constraints WHERE name='UQ_NuPakFeedSlug')
            OR NOT EXISTS(SELECT 1 FROM sys.key_constraints WHERE name='UQ_NuPakPrefixFeedId')
            OR NOT EXISTS(SELECT 1 FROM sys.key_constraints WHERE name='UQ_NuPakPrefix')
            OR NOT EXISTS(SELECT 1 FROM sys.key_constraints WHERE name='UQ_NuPakPackage')
            OR NOT EXISTS(SELECT 1 FROM sys.key_constraints WHERE name='UQ_NuPakVersion')
            THROW 51000,'NuPak multi-feed schema is incomplete. Run the upgrade and latest set scripts.',1;
            """,ct);
         if(await db.Meta.Where(m=>m.cMetaKey=="NuPakSchemaVersion").Select(m=>m.cMetaValue).SingleOrDefaultAsync(ct)!="2")
            throw new InvalidOperationException("NuPak schema version 2 is required.");
         await db.Feeds.FirstOrDefaultAsync(ct);
         await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Prefixes.OrderBy(r => r.cNuPakPrefixId), ct);
         await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Packages.OrderBy(r => r.cNuPakPackageId), ct);
         await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Versions.OrderBy(r => r.cNuPakVersionId), ct);
         await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Grants.OrderBy(r => r.cRobotId), ct);
         await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Audits.OrderBy(r => r.cNuPakAuditId), ct);
      } catch (Exception ex) { throw new InvalidOperationException("NuGet module schema is unavailable. For legacy installations run doc/sqlscript/mssql/updates/20261003-NuPakMultiFeed.sql, then sets/NuPak.sql. For new installations run sets/NuPak.sql. Never run the old binary on this schema.", ex); }
      var store = services.GetRequiredService<NuPakStore>();
      store.Initialize();
      await store.RecoverPurgesAsync(db, ct);
   }
   public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
