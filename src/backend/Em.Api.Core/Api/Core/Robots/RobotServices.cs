using System.Data;
using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core;

[Module(Defaults.AdministrativeToolsModuleName)]
public class RobotServices(RobotContext db, IEnumerable<IRobotAccessManager> managers) : ServicesBase, IRobotServices
{
   private RobotContext Db => db;
   private IRobotAccessManager[] Managers => managers.ToArray();
   private static ActionException NotFound(string what) => new($"{what} was not found.", 404);
   private async Task<RobotAccessInfo[]> ReadAccessesAsync() {
      var all = new List<RobotAccessInfo>();
      foreach (var manager in Managers) all.AddRange(await manager.ReadAsync(AbortToken));
      return all.ToArray();
   }
   [GetAction(claim: IRobotServices.RobotClaim)]
   public async Task<RobotAccessManagerInfo[]> GetMeta_RobotManagers() {
      var result = new List<RobotAccessManagerInfo>();
      foreach (var manager in Managers) result.Add(await manager.DescribeAsync(AbortToken));
      return result.ToArray();
   }
   private IQueryable<ta_User> EligibleOwners => db.Users.AsNoTracking().Where(u =>
      u.cUserState == UserState.Active &&
      u.cUserId != Defaults.AdminUserId && u.cUserId != Defaults.DebuggerUserId);

   [GetAction(claim: IRobotServices.RobotClaim)]
   public Task<RobotOwnerInfo[]> GetMeta_RobotOwners() => EligibleOwners
      .OrderBy(u => u.cUserAccount).ThenBy(u => u.cUserId)
      .Select(u => new RobotOwnerInfo { Id = u.cUserId, Account = u.cUserAccount })
      .ToArrayAsync(AbortToken);

   #region Robot dan hak

   [GetAction(claim: IRobotServices.RobotClaim)]
   public async Task<RobotInfo[]> GetMeta_Robots() {
      var db = Db;
      var robots = await db.Robots.OrderBy(r => r.cRobotName).ToListAsync(AbortToken);
      var rights = await ReadAccessesAsync();
      var ownerIds = robots.Select(r => r.cRobotOwner_cUserId).Where(id => id != null).ToArray();
      var owners = await db.Users.AsNoTracking().Where(u => ownerIds.Contains(u.cUserId))
         .ToDictionaryAsync(u => u.cUserId, u => u.cUserAccount, AbortToken);
      return robots.Select(r => ToInfo(r, rights.Where(a => a.RobotId == r.cRobotId).ToArray(),
         r.cRobotOwner_cUserId is { } id ? owners.GetValueOrDefault(id) : null)).ToArray();
   }

   [PostAction(claim: IRobotServices.RobotClaim)]
   public async Task<RobotToken> PostGetMeta_RobotCreate(string name, string? description, DateTime? tokenExpiry, string? ownerUserId = null) {
      var db = Db;
      name = RobotNames.ValidateName(name);
      tokenExpiry = RequireFutureExpiry(tokenExpiry);
      if (await db.Robots.AnyAsync(r => r.cRobotName == name)) {
         throw new ActionException($"Robot '{name}' already exists.", 409);
      }

      ownerUserId = string.IsNullOrWhiteSpace(ownerUserId) ? null : ownerUserId.Trim();
      var owner = ownerUserId is null ? null : await EligibleOwners.SingleOrDefaultAsync(u => u.cUserId == ownerUserId, AbortToken)
         ?? throw new ActionException("The owner must be an active user account other than a system account.", 400);
      var token = RobotAuth.GenerateToken();
      var now = DateTime.UtcNow;
      var row = new ta_Robot {
         cRobotId = $"{Ulid.NewUlid()}", cRobotName = name, cRobotState = 1,
         cRobotDescription = RobotNames.ValidateDescription(description), cRobotOwner_cUserId = ownerUserId,
         cRobotTokenHash = RobotAuth.HashToken(token), cRobotTokenPrefix = RobotAuth.DisplayPrefix(token),
         cRobotTokenExpiry = tokenExpiry, ustamp = now, datestamp = now
      };
      db.Robots.Add(row);
      await SaveOrConflictAsync(db, $"Robot '{name}' already exists.");
      return new RobotToken { Robot = ToInfo(row, [], owner?.cUserAccount), Token = token };
   }

   [PostAction(claim: IRobotServices.RobotClaim)]
   public async Task<RobotToken> PostGetMeta_RobotRegenerate(string robotId, DateTime? tokenExpiry) {
      var db = Db;
      var robot = await RequireRobotAsync(db, robotId);
      tokenExpiry = RequireFutureExpiry(tokenExpiry);

      // Satu robot satu token: yang lama langsung tidak berlaku begitu hash-nya tertimpa.
      var token = RobotAuth.GenerateToken();
      robot.cRobotTokenHash = RobotAuth.HashToken(token);
      robot.cRobotTokenPrefix = RobotAuth.DisplayPrefix(token);
      robot.cRobotTokenExpiry = tokenExpiry;
      robot.cRobotTokenLastUsed = null;
      robot.ustamp = DateTime.UtcNow;
      db.UpdateRow(robot);
      await db.SaveChangesAsync();
      var ownerAccount = robot.cRobotOwner_cUserId is { } ownerId
         ? await db.Users.AsNoTracking().Where(u => u.cUserId == ownerId).Select(u => u.cUserAccount).SingleOrDefaultAsync(AbortToken)
         : null;
      return new RobotToken { Robot = ToInfo(robot, (await ReadAccessesAsync()).Where(a => a.RobotId == robotId).ToArray(), ownerAccount), Token = token };
   }

   [PostAction(claim: IRobotServices.RobotClaim)]
   public async Task PostMeta_RobotUpdate(string robotId, string? description, bool isActive, DateTime? tokenExpiry) {
      var db = Db;
      var text = RobotNames.ValidateDescription(description);
      var state = isActive ? 1 : 0;
      tokenExpiry = ToUtc(tokenExpiry);
      var now = DateTime.UtcNow;
      var rows = await db.Robots.Where(r => r.cRobotId == robotId).ExecuteUpdateAsync(s => s
         .SetProperty(r => r.cRobotDescription, text)
         .SetProperty(r => r.cRobotState, state)
         .SetProperty(r => r.cRobotTokenExpiry, tokenExpiry)
         .SetProperty(r => r.ustamp, now));
      if (rows == 0) throw NotFound("Robot");
   }

   #endregion

   [PostAction(claim: IRobotServices.RobotClaim)]
   public async Task PostMeta_RobotDelete(string robotId) {
      var providers = Managers;
      await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken)) {
         await RequireRobotAsync(db, robotId);
         foreach (var manager in providers) await manager.PrepareDeleteAsync(db, robotId, AbortToken);
         await db.Robots.Where(r => r.cRobotId == robotId).ExecuteDeleteAsync(AbortToken);
         await tx.CommitAsync(AbortToken);
      }
      foreach (var manager in providers) await manager.AfterDeleteAsync(CancellationToken.None);
   }

   [PostAction(claim: IRobotServices.RobotClaim)]
   public async Task PostMeta_RobotAccessSet(string robotId, string managerId, string resourceId, string access) {
      var manager = Managers.SingleOrDefault(m => m.Id == managerId) ?? throw NotFound("Robot manager");
      await RequireRobotAsync(db, robotId);
      var definition = await manager.DescribeAsync(AbortToken);
      if (!definition.Resources.Any(r => r.Id == resourceId)) throw NotFound("Manager resource");
      if (access.Length != 0 && !definition.Options.Any(o => o.Code == access))
         throw new ActionException("Invalid access for this manager.", 400);
      await manager.SetAsync(robotId, resourceId, access, AbortToken);
   }
   // Semua waktu di registry UTC. Waktu dari UI yang berjenis Local diubah ke UTC; yang tanpa jenis
   // dianggap sudah UTC.
   private static DateTime? ToUtc(DateTime? value) => value is { } v
      ? v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc)
      : null;

   private static DateTime? RequireFutureExpiry(DateTime? expiry) {
      var utc = ToUtc(expiry);
      if (utc is { } value && value <= DateTime.UtcNow) {
         throw new ActionException("The token expiry must be in the future.", 400);
      }

      return utc;
   }

   private static async Task<ta_Robot> RequireRobotAsync(RobotContext db, string robotId) =>
      await db.Robots.SingleOrDefaultAsync(r => r.cRobotId == robotId) ?? throw NotFound("Robot");

   // Insert yang bentrok dengan indeks unik (dua permintaan membuat nama yang sama bersamaan) dijawab
   // 409 seperti pemeriksaan di depannya, bukan 500.
   private static async Task SaveOrConflictAsync(RobotContext db, string conflictMessage) {
      try {
         await db.SaveChangesAsync();
      } catch (DbUpdateException) {
         throw new ActionException(conflictMessage, 409);
      }
   }

   private static RobotInfo ToInfo(ta_Robot r, RobotAccessInfo[] accesses, string? ownerAccount = null) => new() {
      Id = r.cRobotId, Name = r.cRobotName, Description = r.cRobotDescription,
      OwnerUserId = r.cRobotOwner_cUserId, OwnerAccount = ownerAccount,
      IsActive = r.cRobotState == 1, TokenPrefix = r.cRobotTokenPrefix,
      TokenExpiry = r.cRobotTokenExpiry, TokenLastUsed = r.cRobotTokenLastUsed, Accesses = accesses
   };


}
