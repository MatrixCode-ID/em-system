using Microsoft.EntityFrameworkCore;
namespace Em.Api.Core.NuPak;
/// <summary>Server-wide NuPak settings as read from metadata.</summary>
public readonly record struct NuPakServerSettings(bool Enabled);
/// <summary>Reads the NuPak server settings from metadata.</summary>
public sealed class NuPakSettings {
   // Server reads and commits use the same gate. Feed metadata is read directly for each request;
   // there is no stale per-feed cache to repopulate after a committed update.
   /// <summary>Gate shared by reads and commits of the settings.</summary>
   public SemaphoreSlim Gate { get; } = new(1, 1);
   /// <summary>Reads the current server settings.</summary>
   public async Task<NuPakServerSettings> ReadAsync(NuPakDbContext db, CancellationToken ct) {
      await Gate.WaitAsync(ct);
      try {
         var value=await db.Meta.Where(r=>r.cMetaKey=="NuPakEnable").Select(r=>r.cMetaValue).SingleOrDefaultAsync(ct);
         return new(bool.TryParse(value,out var enabled)&&enabled);
      } finally {Gate.Release();}
   }
}
