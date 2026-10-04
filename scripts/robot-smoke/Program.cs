// Integration smoke tests against the configured SQL Server. Creates only ULID/random-named
// fixtures and removes them in finally. No user passwords or credentials are printed.
// Run from repo root: dotnet run --project scripts/robot-smoke
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;
using Em;

var config = JsonDocument.Parse(File.ReadAllText("../.artefacts/em-system/config/emapi-config.json"));
var connection = Environment.GetEnvironmentVariable("EM_DB_CONNECTION_STRING") ??
   config.RootElement.GetProperty("database").GetProperty("connectionString").GetString()!;
await using var db = new RobotContext(new DbContextOptionsBuilder<RobotContext>().UseSqlServer(connection).Options);
if (args.Contains("--inspect-owner")) {
   var account = args.Last();
   var users = await db.Users.AsNoTracking().Where(u => u.cUserAccount == account)
      .Select(u => new { u.cUserAccount, u.cUserState, u.cUserIsAdmin, SystemAccount = u.cUserId == Defaults.AdminUserId || u.cUserId == Defaults.DebuggerUserId }).ToArrayAsync();
   var eligible = await new RobotServices(db, []).GetMeta_RobotOwners();
   foreach (var user in users) Console.WriteLine($"Account={user.cUserAccount}; State={user.cUserState}; Admin={user.cUserIsAdmin}; System={user.SystemAccount}; Eligible={eligible.Any(u => u.Account == user.cUserAccount)}");
   if (users.Length == 0) Console.WriteLine("Account not found in configured database.");
   return;
}
if (args.Contains("--migrate-owner")) {
   var sql = File.ReadAllText("doc/sqlscript/mssql/updates/20261003-RobotOwner.sql");
   await db.Database.ExecuteSqlRawAsync(sql);
   await db.Database.ExecuteSqlRawAsync(sql);
   Console.WriteLine("Owner migration applied twice (idempotence check).");
}
var assembly = typeof(RobotServices).Assembly;
var contextType = assembly.GetType("Em.Api.Core.Registry.CtnContext")!;
var builder = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
builder.UseSqlServer(connection);
await using var containers = (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
var storeType = assembly.GetType("Em.Api.Core.Registry.CtnBlobStore")!;
var store = storeType.GetProperty("Disabled")!.GetValue(null)!;
var container = (IRobotAccessManager)Activator.CreateInstance(
   assembly.GetType("Em.Api.Core.Registry.CtnRobotAccessManager")!, containers, store, builder.Options)!;
var jobs = new SampleManager("Jobs", "Execute");
var failing = new SampleManager("Failure", "Audit");
var service = new RobotServices(db, [container, jobs, failing]);
var name = "smoke-" + Guid.NewGuid().ToString("N")[..12];
var root = Ulid.NewUlid().ToString();
var image = Ulid.NewUlid().ToString();
var upload = Ulid.NewUlid().ToString();
var manifest = Ulid.NewUlid().ToString();
string? robotId = null;
var ownerId = Ulid.NewUlid().ToString();
var ownerAccount = name + "-owner";
var checks = 0;
void Check(string label, bool success) {
   if (!success) throw new InvalidOperationException("FAIL " + label);
   checks++; Console.WriteLine("PASS " + label);
}
async Task Reject(string label, int status, Func<Task> action) {
   try { await action(); throw new InvalidOperationException("Expected rejection: " + label); }
   catch (ActionException ex) { Check(label, ex.StatusCode == status); }
}
async Task<bool> Authenticate(string username, string token) {
   var http = new DefaultHttpContext();
   http.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + token));
   return await RobotAuth.AuthenticateAsync(http, db) != null;
}
try {
   var contactId = await db.Users.Select(u => u.cContactId).FirstAsync();
   db.Users.Add(new ta_User {
      cUserId = ownerId, cUserAccount = ownerAccount, cContactId = contactId,
      cUserState = UserState.Active, cUserIsAdmin = false, ustamp = DateTime.UtcNow, datestamp = DateTime.UtcNow
   });
   await db.SaveChangesAsync();
   db.ChangeTracker.Clear();
   Check("regular owner listed", (await service.GetMeta_RobotOwners()).Any(u => u.Id == ownerId && u.Account == ownerAccount));
   await Reject("unknown owner rejected", 400, () => service.PostGetMeta_RobotCreate(name + "-invalid", null, null, Ulid.NewUlid().ToString()));
   await db.Users.Where(u => u.cUserId == ownerId).ExecuteUpdateAsync(s => s.SetProperty(u => u.cUserIsAdmin, true));
   Check("admin owner listed", (await service.GetMeta_RobotOwners()).Any(u => u.Id == ownerId));
   Check("system owners excluded", !(await service.GetMeta_RobotOwners()).Any(u => u.Id == Defaults.AdminUserId || u.Id == Defaults.DebuggerUserId));
   await Reject("system admin owner rejected", 400, () => service.PostGetMeta_RobotCreate(name + "-invalid", null, null, Defaults.AdminUserId));
   await db.Users.Where(u => u.cUserId == ownerId).ExecuteUpdateAsync(s => s.SetProperty(u => u.cUserIsAdmin, false).SetProperty(u => u.cUserState, UserState.Inactive));
   Check("inactive owner excluded", !(await service.GetMeta_RobotOwners()).Any(u => u.Id == ownerId));
   await Reject("inactive owner rejected", 400, () => service.PostGetMeta_RobotCreate(name + "-invalid", null, null, ownerId));
   await db.Users.Where(u => u.cUserId == ownerId).ExecuteUpdateAsync(s => s.SetProperty(u => u.cUserState, UserState.Active).SetProperty(u => u.cUserIsAdmin, true));
   await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.ta_CtnRoot (cCtnRootId,cCtnRootName,cCtnRootState,ustamp,datestamp) VALUES ({root},{name},1,GETUTCDATE(),GETUTCDATE())");
   await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.ta_CtnImage (cCtnImageId,cCtnRootId,cCtnImageName,cCtnImageState,ustamp,datestamp) VALUES ({image},{root},'smoke',1,GETUTCDATE(),GETUTCDATE())");
   var token = await service.PostGetMeta_RobotCreate(name, "smoke fixture", null, ownerId);
   robotId = token.Robot.Id;
   Check("admin create returns owner", token.Robot.OwnerUserId == ownerId && token.Robot.OwnerAccount == ownerAccount);
   var owned = (await service.GetMeta_Robots()).Single(r => r.Id == robotId);
   Check("owner persisted and listed", owned.OwnerUserId == ownerId && owned.OwnerAccount == ownerAccount);
   Check("new robot has no grants", token.Robot.Accesses.Length == 0);
   Check("token authenticates", await Authenticate(name, token.Token));
   Check("username must match", !await Authenticate(name + "wrong", token.Token));
   Check("wrong token rejected", !await Authenticate(name, token.Token + "wrong"));
   var stored = await db.Robots.SingleAsync(r => r.cRobotId == robotId);
   Check("only SHA256 stored", stored.cRobotTokenHash == RobotAuth.HashToken(token.Token) && stored.cRobotTokenHash.Length == 64);
   await Reject("duplicate name", 409, () => service.PostGetMeta_RobotCreate(name, null, null));
   db.ChangeTracker.Clear();
   await Reject("invalid name", 400, () => service.PostGetMeta_RobotCreate("Bad Name", null, null));
   await Reject("past expiry on create", 400, () => service.PostGetMeta_RobotCreate(name + "-past", null, DateTime.UtcNow.AddDays(-1)));
   var definitions = await service.GetMeta_RobotManagers();
   Check("three independent managers", definitions.Any(d => d.Id == "Container") && definitions.Any(d => d.Id == "Jobs") && definitions.Any(d => d.Id == "Failure"));
   Check("manager-defined non-RW access", definitions.Single(d => d.Id == "Jobs").Options.Single().Code == "Execute");
   await service.PostMeta_RobotAccessSet(robotId, "Container", root, "R");
   await service.PostMeta_RobotAccessSet(robotId, "Jobs", "queue", "Execute");
   var info = (await service.GetMeta_Robots()).Single(r => r.Id == robotId);
   Check("one robot spans managers", info.Accesses.Any(a => a.ManagerId == "Container" && a.Access == "R") && info.Accesses.Any(a => a.ManagerId == "Jobs" && a.Access == "Execute"));
   await service.PostMeta_RobotAccessSet(robotId, "Container", root, "W");
   Check("container grant updates", (await container.ReadAsync(default)).Single(a => a.RobotId == robotId).Access == "W");
   await Reject("invalid manager", 404, () => service.PostMeta_RobotAccessSet(robotId, "Missing", root, "R"));
   await Reject("invalid resource", 404, () => service.PostMeta_RobotAccessSet(robotId, "Container", "Missing", "R"));
   await Reject("manager access validation", 400, () => service.PostMeta_RobotAccessSet(robotId, "Jobs", "queue", "W"));
   await service.PostMeta_RobotAccessSet(robotId, "Jobs", "queue", "");
   await service.PostMeta_RobotAccessSet(robotId, "Jobs", "queue", "");
   Check("revoke is idempotent", !(await jobs.ReadAsync(default)).Any(a => a.RobotId == robotId));
   await service.PostMeta_RobotUpdate(robotId, "changed", false, null);
   Check("disabled login rejected", !await Authenticate(name, token.Token));
   await service.PostMeta_RobotUpdate(robotId, "changed", true, DateTime.UtcNow.AddDays(-1));
   Check("expired login rejected", !await Authenticate(name, token.Token));
   var regenerated = await service.PostGetMeta_RobotRegenerate(robotId, DateTime.UtcNow.AddDays(1));
   Check("regeneration keeps owner", regenerated.Robot.OwnerUserId == ownerId && regenerated.Robot.OwnerAccount == ownerAccount);
   Check("old token invalidated", !await Authenticate(name, token.Token));
   Check("new token authenticates", await Authenticate(name, regenerated.Token));
   await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.ta_CtnUpload (cCtnUploadId,cCtnImageId,cRobotId,cCtnUploadSize,ustamp,datestamp) VALUES ({upload},{image},{robotId},0,GETUTCDATE(),GETUTCDATE())");
   await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.ta_CtnManifest (cCtnManifestId,cCtnImageId,cCtnManifestDigest,cCtnManifestMediaType,cCtnManifestSize,cCtnManifestContent,cCtnManifestPushedBy_cRobotId,ustamp,datestamp) VALUES ({manifest},{image},'sha256:smoke','application/json',2,0x7B7D,{robotId},GETUTCDATE(),GETUTCDATE())");
   failing.FailDelete = true;
   await Reject("failed cleanup rolls back deletion", 409, () => service.PostMeta_RobotDelete(robotId));
   Check("identity survives rollback", await db.Robots.AnyAsync(r => r.cRobotId == robotId));
   Check("container grants survive rollback", (await container.ReadAsync(default)).Any(a => a.RobotId == robotId));
   Check("upload survives rollback", await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM dbo.ta_CtnUpload WHERE cCtnUploadId={upload}").SingleAsync() == 1);
   failing.FailDelete = false;
   await db.Users.Where(u => u.cUserId == ownerId).ExecuteDeleteAsync();
   var orphaned = (await service.GetMeta_Robots()).Single(r => r.Id == robotId);
   Check("deleted owner clears link", orphaned.OwnerUserId == null && orphaned.OwnerAccount == null);
   Check("deleted owner leaves robot login working", await Authenticate(name, regenerated.Token));
   await service.PostMeta_RobotDelete(robotId);
   Check("identity deleted", !await db.Robots.AnyAsync(r => r.cRobotId == robotId));
   Check("token invalid after delete", !await Authenticate(name, regenerated.Token));
   Check("container grants deleted", !(await container.ReadAsync(default)).Any(a => a.RobotId == robotId));
   Check("upload deleted", await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM dbo.ta_CtnUpload WHERE cCtnUploadId={upload}").SingleAsync() == 0);
   Check("manifest retained without pusher", await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM dbo.ta_CtnManifest WHERE cCtnManifestId={manifest} AND cCtnManifestPushedBy_cRobotId IS NULL").SingleAsync() == 1);
   var independent = new RobotServices(db, []);
   var standalone = await independent.PostGetMeta_RobotCreate(name + "-standalone", null, null);
   Check("owner optional", standalone.Robot.OwnerUserId == null && standalone.Robot.OwnerAccount == null);
   Check("registration without registry provider", (await independent.GetMeta_RobotManagers()).Length == 0);
   await independent.PostMeta_RobotDelete(standalone.Robot.Id);
   Console.WriteLine($"{checks} checks passed.");
}
finally {
   await db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.ta_CtnUpload WHERE cCtnUploadId={upload}");
   await db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.ta_CtnManifest WHERE cCtnManifestId={manifest}");
   await db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.ta_CtnRootRobot WHERE cCtnRootId={root}");
   await db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.ta_CtnImage WHERE cCtnImageId={image}");
   await db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.ta_CtnRoot WHERE cCtnRootId={root}");
   await db.Robots.Where(r => r.cRobotName == name || r.cRobotName == name + "-standalone" || r.cRobotName == name + "-invalid").ExecuteDeleteAsync();
   await db.Users.Where(u => u.cUserId == ownerId).ExecuteDeleteAsync();
}

sealed class SampleManager(string id, string code) : IRobotAccessManager
{
   public string Id => id;
   public bool FailDelete { get; set; }
   private readonly List<RobotAccessInfo> _grants = [];
   public Task<RobotAccessManagerInfo> DescribeAsync(CancellationToken ct) => Task.FromResult(new RobotAccessManagerInfo {
      Id = id, Name = id, Resources = [new() { Id = "queue", Name = "Queue" }], Options = [new() { Code = code, Label = code }]
   });
   public Task<RobotAccessInfo[]> ReadAsync(CancellationToken ct) => Task.FromResult(_grants.ToArray());
   public Task SetAsync(string robotId, string resourceId, string access, CancellationToken ct) {
      _grants.RemoveAll(a => a.RobotId == robotId && a.ResourceId == resourceId);
      if (access.Length > 0) _grants.Add(new() { RobotId = robotId, ManagerId = id, ResourceId = resourceId, Access = access });
      return Task.CompletedTask;
   }
   public Task PrepareDeleteAsync(RobotContext identities, string robotId, CancellationToken ct) {
      if (FailDelete) throw new ActionException("Synthetic cleanup failure", 409);
      return Task.CompletedTask;
   }
   public Task AfterDeleteAsync(CancellationToken ct) => Task.CompletedTask;
}
