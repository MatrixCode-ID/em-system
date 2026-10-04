using System.Net.Http;
using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Api.Core.NuPak;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
// Explicit repo path avoids assumptions when --artifacts-path is used.
if (args.Length > 0 && !args[0].StartsWith("--",StringComparison.Ordinal)) repo = Path.GetFullPath(args[0]);
var cs = Environment.GetEnvironmentVariable("EM_DB_CONNECTION_STRING");
if(string.IsNullOrWhiteSpace(cs)) {
   // Same file the host build copies: emapi-config.json in the artefacts folder beside the repo.
   using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo, "../.artefacts/em-system/config/emapi-config.json")),
      new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
   cs=config.RootElement.GetProperty("database").GetProperty("connectionString").GetString();
}
if(string.IsNullOrWhiteSpace(cs)) throw new InvalidOperationException("Set EM_DB_CONNECTION_STRING or provide the host local configuration.");
if(args.Contains("--verify-local")) {
   var options=new DbContextOptionsBuilder<NuPakDbContext>().UseSqlServer(cs).Options;await using var db=new NuPakDbContext(options);
   if(await db.Feeds.CountAsync()!=0||await db.Prefixes.CountAsync()!=0||await db.Packages.CountAsync()!=0||await db.Versions.CountAsync()!=0||await db.Grants.CountAsync()!=0)throw new Exception("Local NuPak is not empty.");
   if(await db.Meta.Where(m=>m.cMetaKey=="NuPakSchemaVersion").Select(m=>m.cMetaValue).SingleAsync()!="2")throw new Exception("Missing schema marker.");
   Console.WriteLine("PASS local schema marker 2; zero feeds/prefixes/packages/versions/grants; NuPakEnable="+await db.Meta.Where(m=>m.cMetaKey=="NuPakEnable").Select(m=>m.cMetaValue).SingleOrDefaultAsync());return;
}
if(args.Contains("--prepare-upgrade")) {
   var localStore=new NuPakStore(Path.Combine(repo,"src/backend/Em.Api/data/nuget"),250);localStore.PreflightLegacyArtifacts();
   await using var conn=new SqlConnection(cs);await conn.OpenAsync();
   await using(var check=new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.ta_NuPakPrefix','cNuPakFeedId') IS NULL AND (EXISTS(SELECT 1 FROM dbo.ta_NuPakPrefix) OR EXISTS(SELECT 1 FROM dbo.ta_NuPakPackage) OR EXISTS(SELECT 1 FROM dbo.ta_NuPakVersion) OR EXISTS(SELECT 1 FROM dbo.ta_NuPakPrefixRobot)) THEN 1 ELSE 0 END",conn)) {
      if(Convert.ToInt32(await check.ExecuteScalarAsync())!=0)throw new InvalidOperationException("Populated legacy NuPak: stop before backup/update and determine destination feeds.");
   }
   Console.WriteLine("PASS local NuPak legacy table/store preflight empty.");
   var backupId="NuPakMultiFeed_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
   string folder;
   await using(var query=new SqlCommand("SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultBackupPath'))",conn)) folder=(string?)await query.ExecuteScalarAsync()??throw new InvalidOperationException("SQL Server default backup path is unavailable.");
   var database=new SqlConnectionStringBuilder(cs).InitialCatalog;
   var backupFile=Path.Combine(folder,backupId+".bak");
   await using(var backup=new SqlCommand("BACKUP DATABASE "+"["+database.Replace("]","]]")+"] TO DISK=@file WITH COPY_ONLY,CHECKSUM; RESTORE VERIFYONLY FROM DISK=@file WITH CHECKSUM;",conn){CommandTimeout=300}) {
      backup.Parameters.AddWithValue("@file",backupFile);await backup.ExecuteNonQueryAsync();
   }
   var snapshot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Em","backups",backupId);Directory.CreateDirectory(snapshot);
   if(Directory.Exists(localStore.Root))ZipFile.CreateFromDirectory(localStore.Root,Path.Combine(snapshot,"nuget-store.zip"));
   else File.WriteAllText(Path.Combine(snapshot,"store-absent.txt"),"NuPak store did not exist at backup time.");
   File.WriteAllText(Path.Combine(snapshot,"backup-pair.txt"),"Core database COPY_ONLY CHECKSUM backup (VERIFYONLY passed): "+backupFile+Environment.NewLine+"NuPak store: "+localStore.Root);
   Console.WriteLine("PASS full core database backup verified: "+backupFile);Console.WriteLine("Store snapshot and pair manifest: "+snapshot);return;
}
if (args.Contains("--install-schema")||args.Contains("--upgrade-schema")) {
   await using var connection = new SqlConnection(cs); await connection.OpenAsync();
   var preflightStore=new NuPakStore(Path.Combine(repo,"src/backend/Em.Api/data/nuget"),250);preflightStore.PreflightLegacyArtifacts();
   var script=args.Contains("--upgrade-schema")?"updates/20261003-NuPakMultiFeed.sql":"sets/NuPak.sql";
   await using var command = new SqlCommand(File.ReadAllText(Path.Combine(repo, "doc/sqlscript/mssql",script)), connection);
   await command.ExecuteNonQueryAsync(); Console.WriteLine("NuPak schema installed."); return;
}
var checks = 0;
void Check(string label, bool result) { if (!result) throw new Exception("FAIL " + label); checks++; Console.WriteLine("PASS " + label); }
async Task Reject(string label, Func<Task> action, int status) {
   try { await action(); throw new Exception("Unexpected success: " + label); }
   catch (ActionException ex) { Check(label, ex.StatusCode == status); }
}
var publicBuilder = new Em.Api.Shared.EmAppBuilder();
publicBuilder.AddPublicEndpoint("/nuget", _ => Task.CompletedTask);
foreach(var invalid in new[]{"", "/", "nuget", "/api", "/API/sub", "/cdn", "/v2", "/nuget/child", "/nuget", "/bad%20path", "/bad path"}) {
   try {publicBuilder.AddPublicEndpoint(invalid,_=>Task.CompletedTask);throw new Exception("Invalid prefix accepted");}
   catch(InvalidOperationException) {Check("public prefix rejected: "+invalid,true);}
}
var dbName = "EmNuPakSmoke_" + Guid.NewGuid().ToString("N");
var master = new SqlConnectionStringBuilder(cs) { InitialCatalog = "master" };
var isolated = new SqlConnectionStringBuilder(cs) { InitialCatalog = dbName };
var root = Path.Combine(Path.GetTempPath(), dbName);
async Task Sql(string command, string connectionString) {
   await using var conn = new SqlConnection(connectionString); await conn.OpenAsync();
   await using var cmd = new SqlCommand(command, conn); await cmd.ExecuteNonQueryAsync();
}
var created = false;
WebApplication? server = null;
try {
   await Sql($"CREATE DATABASE [{dbName}]", master.ConnectionString); created = true;
   // Engine identity tables are copied structurally from the local database without any user data.
   var source = new SqlConnectionStringBuilder(cs).InitialCatalog.Replace("]", "]]");
   await Sql($"SELECT TOP 0 * INTO dbo.ta_Robot FROM [{source}].dbo.ta_Robot; ALTER TABLE dbo.ta_Robot ADD CONSTRAINT PK_SmokeRobot PRIMARY KEY(cRobotId); SELECT TOP 0 * INTO dbo.ta_Meta FROM [{source}].dbo.ta_Meta; ALTER TABLE dbo.ta_Meta ADD CONSTRAINT PK_SmokeMeta PRIMARY KEY(cMetaKey);", isolated.ConnectionString);
   var upgradeSql=File.ReadAllText(Path.Combine(repo,"doc/sqlscript/mssql/updates/20261003-NuPakMultiFeed.sql"));
   await Sql(File.ReadAllText(Path.Combine(repo,"scripts/nupak-smoke/legacy-schema.sql")),isolated.ConnectionString);
   await Sql("INSERT dbo.ta_NuPakPrefix(cNuPakPrefixId,cNuPakPrefixName,cNuPakPrefixState,ustamp,datestamp) VALUES('01ARZ3NDEKTSV4RRFFQ69G5FAV','Legacy.',1,GETUTCDATE(),GETUTCDATE())",isolated.ConnectionString);
   try {await Sql(upgradeSql,isolated.ConnectionString);throw new Exception("Legacy populated migration accepted");}
   catch(SqlException ex)when(ex.Number==51000){Check("populated legacy preflight rejected",true);}
   await using(var verify=new SqlConnection(isolated.ConnectionString)) {
      await verify.OpenAsync();await using var command=new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.ta_NuPakFeed') IS NULL AND COL_LENGTH('dbo.ta_NuPakPrefix','cNuPakFeedId') IS NULL AND EXISTS(SELECT 1 FROM dbo.ta_NuPakPrefix) THEN 1 ELSE 0 END",verify);
      Check("legacy rejection leaves schema and data unchanged",Convert.ToInt32(await command.ExecuteScalarAsync())==1);
   }
   await Sql("DELETE dbo.ta_NuPakPrefix; INSERT dbo.ta_NuPakAudit(cNuPakAuditId,cNuPakAuditAt,cNuPakAuditAction,cNuPakAuditActorKind,cNuPakAuditActorName,cNuPakAuditResult,ustamp,datestamp) VALUES('01ARZ3NDEKTSV4RRFFQ69G5FAV',GETUTCDATE(),'Legacy','User','legacy','Success',GETUTCDATE(),GETUTCDATE()); INSERT dbo.ta_Meta(cMetaKey,cMetaValue,cMetaDescription,ustamp) VALUES('NuPakEnable','False','Preserved toggle',GETUTCDATE())",isolated.ConnectionString);
   await Sql("CREATE TRIGGER dbo.SmokeMigrationFail ON dbo.ta_Meta AFTER INSERT,UPDATE AS BEGIN THROW 51001,'Synthetic marker commit failure',1;END",isolated.ConnectionString);
   try {await Sql(upgradeSql,isolated.ConnectionString);throw new Exception("Migration failure ignored");}
   catch(SqlException ex)when(ex.Number==51001){Check("migration failure propagated",true);}
   await using(var verify=new SqlConnection(isolated.ConnectionString)) {
      await verify.OpenAsync();await using var cmd=new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.ta_NuPakFeed') IS NULL AND COL_LENGTH('dbo.ta_NuPakPrefix','cNuPakFeedId') IS NULL THEN 1 ELSE 0 END",verify);
      Check("failed marker rolls back whole migration",Convert.ToInt32(await cmd.ExecuteScalarAsync())==1);
   }
   await Sql("DROP TRIGGER dbo.SmokeMigrationFail",isolated.ConnectionString);
   await Sql(upgradeSql,isolated.ConnectionString);
   await using(var verify=new SqlConnection(isolated.ConnectionString)) {
      await verify.OpenAsync();await using var command=new SqlCommand("SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.ta_NuPakFeed) AND EXISTS(SELECT 1 FROM dbo.ta_NuPakAudit WHERE cNuPakFeedId IS NULL AND cNuPakAuditFeedName='Legacy single-feed history') AND EXISTS(SELECT 1 FROM dbo.ta_Meta WHERE cMetaKey='NuPakEnable' AND cMetaValue='False') THEN 1 ELSE 0 END",verify);
      Check("empty legacy upgrade zero feeds/history/toggle",Convert.ToInt32(await command.ExecuteScalarAsync())==1);
   }
   await Sql(File.ReadAllText(Path.Combine(repo, "doc/sqlscript/mssql/sets/NuPak.sql")), isolated.ConnectionString);
   async Task<string> SchemaColumns() {
      await using var conn=new SqlConnection(isolated.ConnectionString);await conn.OpenAsync();
      await using var cmd=new SqlCommand("SELECT t.name,c.name,ty.name,c.max_length,c.is_nullable FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE t.name LIKE 'ta_NuPak%' ORDER BY t.name,c.name",conn);
      await using var reader=await cmd.ExecuteReaderAsync();var result=new StringBuilder();while(await reader.ReadAsync())result.AppendLine(string.Join("|",Enumerable.Range(0,5).Select(i=>reader.GetValue(i).ToString())));return result.ToString();
   }
   var upgradeColumns=await SchemaColumns();
   // Fresh installation fixture: only module tables in our disposable database are removed.
   await Sql("DROP TABLE dbo.ta_NuPakAudit,dbo.ta_NuPakPrefixRobot,dbo.ta_NuPakVersion,dbo.ta_NuPakPackage,dbo.ta_NuPakPrefix,dbo.ta_NuPakFeed",isolated.ConnectionString);
   await Sql(File.ReadAllText(Path.Combine(repo,"doc/sqlscript/mssql/sets/NuPak.sql")),isolated.ConnectionString);
   Check("fresh/update column schemas identical",upgradeColumns==await SchemaColumns());
   var options = new DbContextOptionsBuilder<NuPakDbContext>().UseSqlServer(isolated.ConnectionString).Options;
   var robotOptions = new DbContextOptionsBuilder<RobotContext>().UseSqlServer(isolated.ConnectionString).Options;
   await using var db = new NuPakDbContext(options);
   var store = new NuPakStore(root, 1); store.Initialize(); var settings = new NuPakSettings();
   // This standalone fixture intentionally builds a separate provider for hosted startup validation.
#pragma warning disable ASP0000
   using(var startupServices=new ServiceCollection().AddDbContext<NuPakDbContext>(o=>o.UseSqlServer(isolated.ConnectionString)).AddSingleton(store).BuildServiceProvider()) {
      var startup=(Microsoft.Extensions.Hosting.IHostedService)Activator.CreateInstance(typeof(NuPakStore).Assembly.GetType("Em.Api.Core.NuPak.NuPakStartup")!,startupServices)!;
      var scratch=store.TempPath();File.WriteAllText(scratch,"preserve before schema validation");
      await Sql("UPDATE dbo.ta_Meta SET cMetaValue='1' WHERE cMetaKey='NuPakSchemaVersion'",isolated.ConnectionString);
      try {await startup.StartAsync(default);throw new Exception("Old marker accepted");}catch(InvalidOperationException){Check("startup rejects old marker before cleanup",File.Exists(scratch));}
      await Sql("UPDATE dbo.ta_Meta SET cMetaValue='2' WHERE cMetaKey='NuPakSchemaVersion'",isolated.ConnectionString);
      await startup.StartAsync(default);Check("zero feed startup accepted after validation",!File.Exists(scratch)&&await db.Feeds.CountAsync()==0);
   }
#pragma warning restore ASP0000
   var legacyDir=Path.Combine(root,"packages");Directory.CreateDirectory(legacyDir);var legacyArtifact=Path.Combine(legacyDir,"fixture.nupkg.purge");File.WriteAllText(legacyArtifact,"fixture");
   try {store.PreflightLegacyArtifacts();throw new Exception("Legacy artifact accepted");}catch(InvalidOperationException){Check("legacy recovery artifact blocks startup without deletion",File.Exists(legacyArtifact));}File.Delete(legacyArtifact);
   var service = new NuPakServices(db, store, settings);
   typeof(ServicesBase).GetProperty("HttpContext")!.SetValue(service, new DefaultHttpContext());
   typeof(ServicesBase).GetProperty("Request")!.SetValue(service, new ActionRequest { cUserId = $"{Ulid.NewUlid()}", cUserAccount = "smoke-user" });
   var token = RobotAuth.GenerateToken(); var robotId = $"{Ulid.NewUlid()}";
   var now = DateTime.UtcNow;
   db.Robots.Add(new ta_Robot { cRobotId = robotId, cRobotName = "smoke", cRobotState = 1, cRobotTokenHash = RobotAuth.HashToken(token),
      cRobotTokenPrefix = RobotAuth.DisplayPrefix(token), ustamp = now, datestamp = now });
   await db.SaveChangesAsync(); db.ChangeTracker.Clear();
   var actor = new NuPakActor("Robot", robotId, "smoke", "127.0.0.1");
   Check("defaults off/private", !(await service.GetMeta_NuPakStatus()).Enabled);
   Check("installation has zero feeds",(await service.GetMeta_NuPakFeeds()).Length==0);
   var feedInfo=await service.PostGetMeta_NuPakFeedCreate("alpha","Alpha",null);
   Check("new feed off and private",!feedInfo.Enabled&&!feedInfo.AnonymousRead);
   await service.PostGetMeta_NuPakFeedUpdate(feedInfo.Id,feedInfo.Name,null,true,false);
   await service.PostGetMeta_NuPakSetEnabled(true);db.ChangeTracker.Clear();
   var feed=await db.Feeds.SingleAsync();var feedId=feed.cNuPakFeedId;
   var broad = await service.PostGetMeta_NuPakPrefixCreate(feedId,"MatrixCode.", null);
   var narrow = await service.PostGetMeta_NuPakPrefixCreate(feedId,"MatrixCode.Test.", "Smoke"); db.ChangeTracker.Clear();
   var access = new NuPakRobotAccessManager(db, options);
   await access.SetAsync(robotId, broad.Id, "R", default); await access.SetAsync(robotId, narrow.Id, "W", default); db.ChangeTracker.Clear();
   Check("provider resources and R/W", (await access.DescribeAsync(default)).Resources.Length == 2 && (await access.ReadAsync(default)).Length == 2);
   async Task<NuPakStore.Upload> Package(string id = "MatrixCode.Test.Package", string version = "1.0.0+build") {
      var stream = new MemoryStream();
      using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) {
         using var writer = new StreamWriter(zip.CreateEntry("sample.nuspec").Open());
         writer.Write($"<package><metadata><id>{id}</id><version>{version}</version><authors>Smoke</authors><description>Example</description><dependencies><group targetFramework=\"net10.0\" /></dependencies></metadata></package>");
      }
      stream.Position = 0; return await store.ReceiveAsync(stream, default);
   }
   var upload = await Package(); Check("metadata normalized/build stripped", upload.Version == "1.0.0" && upload.Original == "1.0.0+build");
   await NuPakOperations.PushAsync(db, store,feed, upload, robotId, actor); db.ChangeTracker.Clear();
   var row = await db.Versions.SingleAsync(); var package = await db.Packages.SingleAsync();
   Check("longest prefix owns package", package.cNuPakPrefixId == narrow.Id);
   await Reject("duplicate 409", async () => await NuPakOperations.PushAsync(db, store,feed, await Package(), robotId, actor), 409); db.ChangeTracker.Clear();
   await Reject("prefix delete blocked", () => service.PostMeta_NuPakPrefixDelete(feedId,narrow.Id), 409); db.ChangeTracker.Clear();
   await NuPakOperations.ChangeStateAsync(db, store,feedId, row.cNuPakVersionId, false, actor); db.ChangeTracker.Clear();
   Check("recycle hidden and counted", (await service.GetMeta_NuPakVersions(feedId,package.cNuPakPackageId)).Length == 0 && (await service.GetMeta_NuPakStorageSize()).RecycleBytes == row.cNuPakVersionSize);
   await Reject("recycled duplicate reserved", async () => await NuPakOperations.PushAsync(db, store,feed, await Package(), robotId, actor), 409); db.ChangeTracker.Clear();
   await NuPakOperations.ChangeStateAsync(db, store,feedId, row.cNuPakVersionId, true, actor); db.ChangeTracker.Clear();
   await db.Versions.Where(v => v.cNuPakVersionId == row.cNuPakVersionId).ExecuteUpdateAsync(s => s.SetProperty(v => v.cNuPakVersionSize, 5L * 1024 * 1024 * 1024));
   Check("bigint storage >4 GB", (await service.GetMeta_NuPakStorageSize()).TotalBytes == 5L * 1024 * 1024 * 1024);
   await service.PostGetMeta_NuPakSetEnabled(true); Check("toggle invalidation immediate", (await settings.ReadAsync(db, default)).Enabled); db.ChangeTracker.Clear();
   foreach (var invalid in new[] { "../escape", "a/b", "a\\b", "a:", "." }) {
      try { store.PackagePath(feedId,invalid, "1.0.0"); throw new Exception("Unsafe path accepted"); } catch (ActionException) { Check("unsafe id " + invalid, true); }
   }
   await Reject("invalid zip", async () => await store.ReceiveAsync(new MemoryStream([1, 2, 3]), default), 400);
   await Reject("stream size limit", async () => await store.ReceiveAsync(new MemoryStream(new byte[1024 * 1024 + 1]), default), 413);
   Check("all actions explicitly claimed", typeof(NuPakServices).GetMethods().Where(m => m.Name.StartsWith("GetMeta_") || m.Name.StartsWith("PostMeta_") || m.Name.StartsWith("PostGetMeta_"))
      .Where(m => typeof(INuPakServices).GetMethod(m.Name) is not null).All(m => m.GetCustomAttributes().Any(a => a is GetActionAttribute or PostActionAttribute)));
   var settingsActions=new[]{"GetMeta_NuPakSettings","PostGetMeta_NuPakValidateDirectory","PostGetMeta_NuPakSettingsSave","PostGetMeta_NuPakSetEnabled","PostGetMeta_NuPakFeedCreate","PostGetMeta_NuPakFeedUpdate","PostMeta_NuPakFeedDelete","PostMeta_NuPakVersionPurge","PostMeta_NuPakRecycleBinEmpty"};
   foreach(var method in typeof(INuPakServices).GetMethods()) {
      var impl=typeof(NuPakServices).GetMethod(method.Name)!;
      var claim=impl.GetCustomAttribute<GetActionAttribute>()?.Claim??impl.GetCustomAttribute<PostActionAttribute>()?.Claim;
      Check("claim boundary "+method.Name,claim==(settingsActions.Contains(method.Name)?INuPakServices.SettingsClaim:INuPakServices.ManagerClaim));
   }
   async Task<NuPakStore.Upload> RawSpec(string xml, bool duplicate=false) {
      using var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true)) {
         using(var writer=new StreamWriter(zip.CreateEntry("bad.nuspec").Open()))writer.Write(xml);
         if(duplicate) using(var writer=new StreamWriter(zip.CreateEntry("other.nuspec").Open()))writer.Write(xml);
      }stream.Position=0;return await store.ReceiveAsync(stream,default);
   }
   await Reject("DTD rejected",async()=>await RawSpec("<!DOCTYPE package [<!ENTITY x 'test'>]><package><metadata><id>&x;</id><version>1.0.0</version></metadata></package>"),400);
   await Reject("duplicate root nuspec rejected",async()=>await RawSpec("<package><metadata><id>MatrixCode.Test.Bad</id><version>1.0.0</version></metadata></package>",true),400);
   await Reject("prefix rename containing package blocked",async()=>await service.PostGetMeta_NuPakPrefixUpdate(feedId,narrow.Id,"Changed.",null,true),409);db.ChangeTracker.Clear();
   var late=await service.PostGetMeta_NuPakPrefixCreate(feedId,"MatrixCode.Test.Package",null);db.ChangeTracker.Clear();
   await NuPakOperations.PushAsync(db,store,feed,await Package(version:"2.0.0"),robotId,actor);db.ChangeTracker.Clear();
   Check("later specific prefix preserves owner",(await db.Packages.SingleAsync()).cNuPakPrefixId==narrow.Id);
   var v2=await db.Versions.SingleAsync(v=>v.cNuPakVersionNumber=="2.0.0");
   await NuPakOperations.ChangeStateAsync(db,store,feedId,v2.cNuPakVersionId,false,actor);db.ChangeTracker.Clear();
   var v2file=store.PackagePath(feedId,"MatrixCode.Test.Package","2.0.0");File.Move(v2file,v2file+".purge");
   await store.RecoverPurgesAsync(db,default);Check("purge crash rollback restores file",File.Exists(v2file));
   await NuPakOperations.PurgeAsync(db,store,feedId,v2.cNuPakVersionId,actor);db.ChangeTracker.Clear();Check("purge removes file and row",!File.Exists(v2file)&&!await db.Versions.AnyAsync(v=>v.cNuPakVersionId==v2.cNuPakVersionId));
   await service.PostMeta_NuPakPrefixDelete(feedId,late.Id);db.ChangeTracker.Clear();
   await access.SetAsync(robotId,narrow.Id,"R",default);db.ChangeTracker.Clear();
   await Reject("R cannot push",async()=>await NuPakOperations.PushAsync(db,store,feed,await Package(version:"3.0.0"),robotId,actor),403);db.ChangeTracker.Clear();
   await Reject("unmatched prefix push rejected",async()=>await NuPakOperations.PushAsync(db,store,feed,await Package(id:"Other.Package"),robotId,actor),403);db.ChangeTracker.Clear();
   await access.SetAsync(robotId,narrow.Id,"W",default);db.ChangeTracker.Clear();
   Check("prefix read access panel",(await service.GetMeta_NuPakPrefixAccess(feedId,narrow.Id)).Single().RobotName=="smoke");
   Check("package aggregate and latest semver",(await service.GetMeta_NuPakPackages(feedId,narrow.Id,null,0,100)).Single().LatestVersion=="1.0.0");
   Check("audit filters",(await service.GetMeta_NuPakAudit(feedId,new NuPakAuditFilter {Action="Purge",Package="MatrixCode.Test.Package"},0,100)).Length==1);
   var racer1=await Package(id:"MatrixCode.Test.Race");var racer2=await Package(id:"MatrixCode.Test.Race");
   async Task<bool> Race(NuPakStore.Upload upload) {await using var context=new NuPakDbContext(options);try{await NuPakOperations.PushAsync(context,store,feed,upload,robotId,actor);return true;}catch(ActionException ex)when(ex.StatusCode==409){return false;}}
   var winners=await Task.WhenAll(Race(racer1),Race(racer2));Check("concurrent push one winner",winners.Count(w=>w)==1);
   var raceVersion=await (from p in db.Packages join v in db.Versions on p.cNuPakPackageId equals v.cNuPakPackageId where p.cNuPakPackageName=="MatrixCode.Test.Race" select v).SingleAsync();
   await NuPakOperations.ChangeStateAsync(db,store,feedId,raceVersion.cNuPakVersionId,false,actor);db.ChangeTracker.Clear();
   await NuPakOperations.PurgeAsync(db,store,feedId,raceVersion.cNuPakVersionId,actor);db.ChangeTracker.Clear();
   await Sql("CREATE TRIGGER dbo.SmokeAuditFail ON dbo.ta_NuPakAudit AFTER INSERT AS BEGIN THROW 51000, 'Synthetic audit write failure', 1; END",isolated.ConnectionString);
   try { await NuPakOperations.PushAsync(db,store,feed,await Package(id:"MatrixCode.Test.Fail"),robotId,actor);throw new Exception("Injected failure ignored"); }
   catch(DbUpdateException) {Check("failed SQL push removes file",!File.Exists(store.PackagePath(feedId,"MatrixCode.Test.Fail","1.0.0")));}
   finally {await Sql("DROP TRIGGER dbo.SmokeAuditFail",isolated.ConnectionString);db.ChangeTracker.Clear();}
   Check("failed push rolls back package and version",!await db.Packages.AnyAsync(p=>p.cNuPakPackageName=="MatrixCode.Test.Fail"));
   await NuPakOperations.PushAsync(db,store,feed,await Package(id:"MatrixCode.Test.PurgeFailure"),robotId,actor);db.ChangeTracker.Clear();
   var purgeVersion=await (from p in db.Packages join v in db.Versions on p.cNuPakPackageId equals v.cNuPakPackageId where p.cNuPakPackageName=="MatrixCode.Test.PurgeFailure" select v).SingleAsync();
   await NuPakOperations.ChangeStateAsync(db,store,feedId,purgeVersion.cNuPakVersionId,false,actor);db.ChangeTracker.Clear();
   await Sql("CREATE TRIGGER dbo.SmokeAuditFail ON dbo.ta_NuPakAudit AFTER INSERT AS BEGIN THROW 51000, 'Synthetic audit write failure', 1; END",isolated.ConnectionString);
   try {await NuPakOperations.PurgeAsync(db,store,feedId,purgeVersion.cNuPakVersionId,actor);throw new Exception("Purge failure ignored");}
   catch(DbUpdateException) {Check("failed purge restores file and rolls back row",File.Exists(store.PackagePath(feedId,"MatrixCode.Test.PurgeFailure","1.0.0"))&&await db.Versions.AnyAsync(v=>v.cNuPakVersionId==purgeVersion.cNuPakVersionId));}
   finally {await Sql("DROP TRIGGER dbo.SmokeAuditFail",isolated.ConnectionString);db.ChangeTracker.Clear();}
   if(OperatingSystem.IsWindows()) {
      using(var locked=File.Open(store.PackagePath(feedId,"MatrixCode.Test.PurgeFailure","1.0.0"),FileMode.Open,FileAccess.Read,FileShare.None)) {
         var partial=await service.PostMeta_NuPakRecycleBinEmpty(feedId,narrow.Id);db.ChangeTracker.Clear();Check("empty bin reports file failure and retains metadata",partial.Failed==1&&await db.Versions.AnyAsync(v=>v.cNuPakVersionId==purgeVersion.cNuPakVersionId));
      }
   }
   await NuPakOperations.PurgeAsync(db,store,feedId,purgeVersion.cNuPakVersionId,actor);db.ChangeTracker.Clear();
   var builder = WebApplication.CreateBuilder(); builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning); builder.WebHost.UseUrls("http://127.0.0.1:0");
   builder.Services.AddDbContext<NuPakDbContext>(o => o.UseSqlServer(isolated.ConnectionString));
   builder.Services.AddDbContext<RobotContext>(o => o.UseSqlServer(isolated.ConnectionString)); builder.Services.AddSingleton(store); builder.Services.AddSingleton(settings);
   server = builder.Build(); server.UsePathBase("/house"); server.Map("/nuget", branch => branch.Run(NuPakEndpoint.HandleAsync));
   await server.StartAsync();
   var address = server.Urls.Single() + "/house/nuget/alpha";
   using var http = new HttpClient();
   Check("private challenge 401", (await http.GetAsync(address + "/v3/index.json")).StatusCode == HttpStatusCode.Unauthorized);
   http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("smoke:" + token)));
   var index = await http.GetStringAsync(address + "/v3/index.json"); Check("PathBase absolute index", index.Contains(address + "/v3/flatcontainer/"));
   Check("flat list", (await http.GetStringAsync(address + "/v3/flatcontainer/matrixcode.test.package/index.json")).Contains("1.0.0"));
   using var range = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, address + "/v3/flatcontainer/matrixcode.test.package/1.0.0/matrixcode.test.package.1.0.0.nupkg"); range.Headers.Range = new RangeHeaderValue(0, 9);
   Check("Range 206", (await http.SendAsync(range)).StatusCode == HttpStatusCode.PartialContent);
   Check("registration dependencies", (await http.GetStringAsync(address + "/v3/registration/matrixcode.test.package/index.json")).Contains("dependencyGroups"));
   Check("search semver2", (await http.GetStringAsync(address + "/v3/search?q=MatrixCode&semVerLevel=2.0.0")).Contains("MatrixCode.Test.Package"));
   using(var request=new HttpRequestMessage(System.Net.Http.HttpMethod.Get,address+"/v3/registration/matrixcode.test.package/index.json")) {
      request.Headers.AcceptEncoding.ParseAdd("gzip");using var response=await http.SendAsync(request);using var bytes=new MemoryStream(await response.Content.ReadAsByteArrayAsync());using var gzip=new GZipStream(bytes,CompressionMode.Decompress);
      using var json=await JsonDocument.ParseAsync(gzip);Check("registration gzip negotiation",response.Content.Headers.ContentEncoding.Contains("gzip")&&json.RootElement.GetProperty("count").GetInt32()==1);
   }
   Check("registration standalone leaf",(await http.GetStringAsync(address+"/v3/registration/matrixcode.test.package/1.0.0.json")).Contains("packageContent"));
   Check("search package type filter",(await http.GetStringAsync(address+"/v3/search?packageType=Unknown&semVerLevel=2.0.0")).Contains("\"totalHits\":0"));
   async Task<HttpResponseMessage> HttpPush(string file) {
      using var form=new MultipartFormDataContent();form.Add(new StreamContent(File.OpenRead(file)),"package","fixture.nupkg");return await http.PutAsync(address+"/v2/package",form);
   }
   var duplicateUpload=await Package();using(var response=await HttpPush(duplicateUpload.Path))Check("HTTP duplicate 409 with no sensitive body",response.StatusCode==HttpStatusCode.Conflict&&(await response.Content.ReadAsStringAsync())=="");File.Delete(duplicateUpload.Path);
   using(var invalid=new StringContent("invalid"))using(var response=await http.PutAsync(address+"/v2/package",invalid))Check("HTTP malformed push 400",response.StatusCode==HttpStatusCode.BadRequest);
   var tooLarge=Path.Combine(root,"too-large.nupkg");File.WriteAllBytes(tooLarge,new byte[1024*1024+1]);using(var response=await HttpPush(tooLarge))Check("HTTP streaming limit 413",response.StatusCode==HttpStatusCode.RequestEntityTooLarge);File.Delete(tooLarge);
   var unmatched=await Package(id:"Other.Package");using(var response=await HttpPush(unmatched.Path))Check("HTTP unmatched prefix 403",response.StatusCode==HttpStatusCode.Forbidden);File.Delete(unmatched.Path);
   Check("denied and failed write audits",await db.Audits.AnyAsync(a=>a.cNuPakAuditResult=="Denied")&&await db.Audits.AnyAsync(a=>a.cNuPakAuditResult=="Failed"));
   Check("nuspec response",(await http.GetStringAsync(address+"/v3/flatcontainer/matrixcode.test.package/1.0.0/matrixcode.test.package.nuspec")).Contains("metadata"));
   Check("HEAD download",(await http.SendAsync(new HttpRequestMessage(System.Net.Http.HttpMethod.Head,address+"/v3/flatcontainer/matrixcode.test.package/1.0.0/matrixcode.test.package.1.0.0.nupkg"))).IsSuccessStatusCode);
   await access.SetAsync(robotId, narrow.Id, "", default); db.ChangeTracker.Clear();
   Check("unauthorized read 404", (await http.GetAsync(address + "/v3/flatcontainer/matrixcode.test.package/index.json")).StatusCode == HttpStatusCode.NotFound);
   Check("unauthorized search filtered", (await http.GetStringAsync(address + "/v3/search?q=MatrixCode&semVerLevel=2.0.0")).Contains("\"totalHits\":0"));
   await access.SetAsync(robotId, narrow.Id, "W", default); db.ChangeTracker.Clear();
   await service.PostGetMeta_NuPakFeedUpdate(feedId,"Alpha",null,true,true); db.ChangeTracker.Clear(); http.DefaultRequestHeaders.Authorization = null;
   Check("anonymous read allowed", (await http.GetAsync(address + "/v3/index.json")).IsSuccessStatusCode);
   http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("smoke:bad")));
   Check("invalid creds rejected in anonymous", (await http.GetAsync(address + "/v3/index.json")).StatusCode == HttpStatusCode.Unauthorized);
   http.DefaultRequestHeaders.Authorization = null; http.DefaultRequestHeaders.Add("X-NuGet-ApiKey", token);
   Check("API key delete recycles", (await http.DeleteAsync(address + "/v2/package/MatrixCode.Test.Package/1.0.0")).StatusCode == HttpStatusCode.NoContent);
   Check("API key read scoped and recycled 404", (await http.GetAsync(address + "/v3/flatcontainer/matrixcode.test.package/index.json")).StatusCode == HttpStatusCode.NotFound);
   http.DefaultRequestHeaders.Remove("X-NuGet-ApiKey");
   Check("anonymous recycled read 404", (await http.GetAsync(address + "/v3/flatcontainer/matrixcode.test.package/index.json")).StatusCode == HttpStatusCode.NotFound);
   File.WriteAllText(Path.Combine(root,"nuget.config"),$"<configuration><packageSources><clear/><add key=\"Em\" value=\"{address}/v3/index.json\" allowInsecureConnections=\"true\"/></packageSources></configuration>");
   var cliToken=token;var cliName="smoke";
   async Task<string> Cli(params string[] arguments) {
      var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
      foreach (var argument in arguments) start.ArgumentList.Add(argument);
      start.Environment["NuGetPackageSourceCredentials_Em"] = "Username="+cliName+";Password=" + cliToken + ";ValidAuthenticationTypes=Basic";
      using var process = Process.Start(start)!;
      var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
      await process.WaitForExitAsync(); var text = await output + await error;
      if (process.ExitCode != 0) throw new Exception("CLI failed: " + text.Replace(token, "[redacted]").Replace(cliToken,"[redacted]"));
      return text;
   }
   var cliPrefix = await service.PostGetMeta_NuPakPrefixCreate(feedId,"Em.", "Engine smoke packages");
   await access.SetAsync(robotId, cliPrefix.Id, "W", default); db.ChangeTracker.Clear();
   var enginePackage = Path.GetFullPath(Path.Combine(repo,"../.artefacts/em-system/nuget-pack/Em.Libs.0.1.0-pre-alpha.1.nupkg"));
   await Cli("nuget", "push", enginePackage, "--source", address + "/v3/index.json", "--api-key", token, "--allow-insecure-connections");
   Check("dotnet nuget push real engine package", await db.Packages.AnyAsync(p=>p.cNuPakPackageName=="Em.Libs"));
   if(args.Contains("--publisher")) {
      var wp=typeof(Em.Ui.Wpf.Core.EmApp);
      var gui=(Em.Ui.Wpf.Core.EmApp)Activator.CreateInstance(wp,BindingFlags.Instance|BindingFlags.NonPublic,null,[Array.Empty<string>()],null)!;
      // Independent fake GUI provider; it does not host ASP.NET singleton services.
#pragma warning disable ASP0000
      var guiProxy=DispatchProxy.Create<INuPakServices,PublisherNuPakProxy>();((PublisherNuPakProxy)(object)guiProxy).Backend=service;
      using var guiServices=new ServiceCollection().AddSingleton(guiProxy).BuildServiceProvider();
#pragma warning restore ASP0000
      wp.GetField("_serviceProvider",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(gui,guiServices);
      gui.ActiveConnection=new Em.Ui.Core.Shared.ApiConnection {ProfileName="Smoke",Host=server.Urls.Single()+"/house",Timeout=30};
      var publisherSettings=new Em.Ui.Wpf.Publish.PublisherSettings {Profiles=Path.Combine(root,"publisher-profiles"),Logs=Path.Combine(root,"publisher-logs"),Work=Path.Combine(root,"publisher-work")};
      var publisherSecrets=new Em.Ui.Wpf.Publish.PublishSecretStore();
      var publisherProfile=Em.Ui.Wpf.Publish.PublishProfile.Create(Em.Ui.Wpf.Publish.PublishKind.NuGet);publisherProfile.Workspace=repo;publisherProfile.NuGet!.VersionOverride="0.1.0-publisher.1";
      publisherProfile.NuGet.Target=new() {Type=Em.Ui.Wpf.Publish.TargetType.BuiltIn,Server=gui.ActiveConnection.Host,Feed=feedId};
      publisherProfile.Credentials.Add(new() {Username="smoke",ScopeHost=new Uri(address).Authority,Secret=token});
      publisherSecrets.ConvertMode(publisherProfile,Em.Ui.Wpf.Publish.SensitiveDataStorage.Separate);
      publisherProfile.NuGet.Sources.Add(new() {Path="src/shared/Em.Libs/Em.Libs.csproj"});
      publisherProfile.NuGet.Sources.Add(new() {Path="src/shared/Em.Ui.Core/Em.Ui.Core.csproj"});
      var publisher=new Em.Ui.Wpf.Publish.Publisher(publisherSettings,new(publisherSettings.Profiles),publisherSecrets,new(gui,publisherSecrets));
      await publisher.Check(publisherProfile,default);await publisher.Prepare(publisherProfile,default);
      Check("publisher multi-source prepare",publisher.Prepared!.Artifacts.Count==2);
      await publisher.Push(publisherProfile,"Publisher fixture",default);Check("publisher built-in NuPak in-process push",publisher.Prepared.Artifacts.All(a=>a.Result==Em.Ui.Wpf.Publish.PublishResult.Success));
      await publisher.Verify(publisherProfile,default);Check("publisher private NuPak SHA512 verify",publisher.Prepared.Artifacts.All(a=>a.Verification=="SHA-512 matched"));
      publisher.AddPackages(publisherProfile,publisher.Prepared.Artifacts.Select(a=>a.File).ToArray());
      await publisher.Push(publisherProfile,"Duplicate fixture",default);Check("publisher duplicate handling",publisher.Prepared.Artifacts.Count(a=>a.Result==Em.Ui.Wpf.Publish.PublishResult.Duplicate)==2);
   }
   var searchResult = await Cli("package","search","Em.Libs","--source",address+"/v3/index.json","--prerelease","--format","json");
   Check("dotnet package search real", searchResult.Contains("Em.Libs",StringComparison.OrdinalIgnoreCase));
   // A dependency-free client fixture makes restore use this source exclusively, including a fresh package cache.
   var restorePackage = Path.Combine(root,"MatrixCode.Test.Restore.1.0.0.nupkg");
   using (var zip = ZipFile.Open(restorePackage, ZipArchiveMode.Create)) {
      using (var writer = new StreamWriter(zip.CreateEntry("restore.nuspec").Open())) writer.Write("<package><metadata><id>MatrixCode.Test.Restore</id><version>1.0.0</version><authors>Smoke</authors><description>Restore test</description></metadata></package>");
      using var output = zip.CreateEntry("lib/net10.0/Em.Api.Core.Models.dll").Open(); using var input=File.OpenRead(typeof(INuPakServices).Assembly.Location); input.CopyTo(output);
   }
   await Cli("nuget","push",restorePackage,"--source",address+"/v3/index.json","--api-key",token,"--allow-insecure-connections");
   var restoreProject=Path.Combine(root,"restore.csproj");
   File.WriteAllText(restoreProject,"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include=\"MatrixCode.Test.Restore\" Version=\"1.0.0\" /></ItemGroup></Project>");
   var restoreConfig=Path.Combine(root,"nuget.config");
   File.WriteAllText(restoreConfig,$"<configuration><packageSources><clear/><add key=\"Em\" value=\"{address}/v3/index.json\" allowInsecureConnections=\"true\"/></packageSources><packageSourceMapping><packageSource key=\"Em\"><package pattern=\"MatrixCode.*\"/></packageSource></packageSourceMapping></configuration>");
   await service.PostGetMeta_NuPakFeedUpdate(feedId,"Alpha",null,true,false); db.ChangeTracker.Clear();
   await Cli("restore",restoreProject,"--configfile",restoreConfig,"--packages",Path.Combine(root,"client-packages"),"--no-cache","--force");
   Check("dotnet restore private feed with mapping and Basic", File.Exists(Path.Combine(root,"client-packages/matrixcode.test.restore/1.0.0/matrixcode.test.restore.1.0.0.nupkg")));
   await Cli("nuget","delete","Em.Libs","0.1.0-pre-alpha.1","--source",address+"/v3/index.json","--api-key",token,"--non-interactive");
   Check("dotnet nuget delete real", await db.Versions.AnyAsync(v=>v.cNuPakVersionNumber=="0.1.0-pre-alpha.1" && v.cNuPakVersionState==-2));
   await service.PostMeta_NuPakRecycleBinEmpty(feedId,cliPrefix.Id); db.ChangeTracker.Clear();
   await service.PostGetMeta_NuPakSetEnabled(false); db.ChangeTracker.Clear();
   Check("disabled feed 404", (await http.GetAsync(address + "/v3/index.json")).StatusCode == HttpStatusCode.NotFound);
   Check("management works when off", (await service.GetMeta_NuPakPrefixes(feedId)).Length == 3);
   await using(var identities=new RobotContext(robotOptions)) {
      await using var tx=await identities.Database.BeginTransactionAsync();
      await access.PrepareDeleteAsync(identities,robotId,default);
      await identities.Robots.Where(r=>r.cRobotId==robotId).ExecuteDeleteAsync();
      await tx.RollbackAsync();
   }
   Check("robot deletion transaction rolls back grants/references",await db.Grants.AnyAsync(g=>g.cRobotId==robotId)&&await db.Versions.AnyAsync(v=>v.cNuPakVersionPushedBy_cRobotId==robotId));
   await using(var identities=new RobotContext(robotOptions)) {
      await using var tx=await identities.Database.BeginTransactionAsync();await access.PrepareDeleteAsync(identities,robotId,default);
      await identities.Robots.Where(r=>r.cRobotId==robotId).ExecuteDeleteAsync();await tx.CommitAsync();
   }
   Check("robot deletion cleans grants and pushed-by",!await db.Grants.AnyAsync(g=>g.cRobotId==robotId)&&!await db.Versions.AnyAsync(v=>v.cNuPakVersionPushedBy_cRobotId==robotId));
   Check("audit survives robot deletion",await db.Audits.AnyAsync(a=>a.cNuPakAuditActorName=="smoke"));
   var empty = await service.PostMeta_NuPakRecycleBinEmpty(feedId,null); db.ChangeTracker.Clear();
   Check("empty bin size and remove orphan package", empty.Count == 1 && empty.Bytes > uint.MaxValue && !await db.Packages.AnyAsync(p => p.cNuPakPackageName == "MatrixCode.Test.Package"));
   Check("audit retained", await db.Audits.AnyAsync(a => a.cNuPakAuditAction == "Push") && await db.Audits.AnyAsync(a => a.cNuPakAuditAction == "EmptyBin"));
   await service.PostGetMeta_NuPakSetEnabled(true);
   var beta=await service.PostGetMeta_NuPakFeedCreate("beta","Beta",null);
   await service.PostGetMeta_NuPakFeedUpdate(beta.Id,"Beta",null,true,true);db.ChangeTracker.Clear();
   var betaFeed=await db.Feeds.SingleAsync(f=>f.cNuPakFeedId==beta.Id);
   var betaPrefix=await service.PostGetMeta_NuPakPrefixCreate(beta.Id,"MatrixCode.Test.",null);db.ChangeTracker.Clear();
   var betaToken=RobotAuth.GenerateToken();var betaRobot=$"{Ulid.NewUlid()}";
   db.Robots.Add(new(){cRobotId=betaRobot,cRobotName="beta-robot",cRobotState=1,cRobotTokenHash=RobotAuth.HashToken(betaToken),cRobotTokenPrefix=RobotAuth.DisplayPrefix(betaToken),ustamp=now,datestamp=now});
   await db.SaveChangesAsync();db.ChangeTracker.Clear();await access.SetAsync(betaRobot,betaPrefix.Id,"W",default);
   // The original robot was deliberately deleted by the preceding regression checks.
   var alphaToken=RobotAuth.GenerateToken();var alphaRobot=$"{Ulid.NewUlid()}";
   db.Robots.Add(new(){cRobotId=alphaRobot,cRobotName="alpha-robot",cRobotState=1,cRobotTokenHash=RobotAuth.HashToken(alphaToken),cRobotTokenPrefix=RobotAuth.DisplayPrefix(alphaToken),ustamp=now,datestamp=now});
   await db.SaveChangesAsync();db.ChangeTracker.Clear();await access.SetAsync(alphaRobot,narrow.Id,"W",default);
   var ua=await Package(id:"MatrixCode.Test.Isolated");
   var ub=await RawSpec("<package><metadata><id>MatrixCode.Test.Isolated</id><version>1.0.0</version><authors>Beta</authors><description>Different artifact</description></metadata></package>");
   var hashA=ua.Hash;var hashB=ub.Hash;
   await Task.WhenAll(PushSeparate(feed,ua,alphaRobot),PushSeparate(betaFeed,ub,betaRobot));db.ChangeTracker.Clear();
   async Task PushSeparate(ta_NuPakFeed f,NuPakStore.Upload u,string robot) {await using var context=new NuPakDbContext(options);await NuPakOperations.PushAsync(context,store,f,u,robot,new("Robot",robot,"fixture",null));}
   var alphaPackage=await db.Packages.SingleAsync(p=>p.cNuPakFeedId==feedId&&p.cNuPakPackageName=="MatrixCode.Test.Isolated");
   var betaPackage=await db.Packages.SingleAsync(p=>p.cNuPakFeedId==beta.Id&&p.cNuPakPackageName=="MatrixCode.Test.Isolated");
   var alphaVersion=await db.Versions.SingleAsync(v=>v.cNuPakPackageId==alphaPackage.cNuPakPackageId);
   var betaVersion=await db.Versions.SingleAsync(v=>v.cNuPakPackageId==betaPackage.cNuPakPackageId);
   Check("same id/version distinct hashes and paths",hashA!=hashB&&alphaVersion.cNuPakVersionHash==hashA&&betaVersion.cNuPakVersionHash==hashB&&store.PackagePath(feedId,"MatrixCode.Test.Isolated","1.0.0")!=store.PackagePath(beta.Id,"MatrixCode.Test.Isolated","1.0.0"));
   await Reject("cross-feed prefix access",async()=>await service.GetMeta_NuPakPrefixAccess(feedId,betaPrefix.Id),404);
   await Reject("cross-feed prefix packages",async()=>await service.GetMeta_NuPakPackages(feedId,betaPrefix.Id,null,0,100),404);
   await Reject("cross-feed versions",async()=>await service.GetMeta_NuPakVersions(feedId,betaPackage.cNuPakPackageId),404);
   await Reject("cross-feed recycle",()=>service.PostMeta_NuPakVersionRecycle(feedId,betaVersion.cNuPakVersionId),404);
   await Reject("cross-feed restore",()=>service.PostMeta_NuPakVersionRestore(feedId,betaVersion.cNuPakVersionId),404);
   await Reject("cross-feed purge",()=>service.PostMeta_NuPakVersionPurge(feedId,betaVersion.cNuPakVersionId),404);
   await Reject("cross-feed bin",async()=>await service.GetMeta_NuPakRecycleBin(feedId,betaPrefix.Id,0,100),404);
   await Reject("cross-feed empty",async()=>await service.PostMeta_NuPakRecycleBinEmpty(feedId,betaPrefix.Id),404);
   await Reject("cross-feed prefix update",async()=>await service.PostGetMeta_NuPakPrefixUpdate(feedId,betaPrefix.Id,"Other.",null,true),404);
   await Reject("cross-feed prefix delete",()=>service.PostMeta_NuPakPrefixDelete(feedId,betaPrefix.Id),404);
   await Reject("nonempty feed delete",()=>service.PostMeta_NuPakFeedDelete(beta.Id),409);
   var baseAddress=server.Urls.Single()+"/house/nuget";var betaAddress=baseAddress+"/beta";
   using var alphaHttp=new HttpClient();alphaHttp.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes("alpha-robot:"+alphaToken)));
   Check("A robot B download forbidden",(await alphaHttp.GetAsync(betaAddress+"/v3/flatcontainer/matrixcode.test.isolated/index.json")).StatusCode==HttpStatusCode.NotFound);
   Check("A robot B search empty",(await alphaHttp.GetStringAsync(betaAddress+"/v3/search")).Contains("\"totalHits\":0"));
   Check("B anonymous download allowed",(await http.GetAsync(betaAddress+"/v3/flatcontainer/matrixcode.test.isolated/index.json")).IsSuccessStatusCode);
   Check("A remains private",(await http.GetAsync(address+"/v3/index.json")).StatusCode==HttpStatusCode.Unauthorized);
   Check("B registration URL",(await http.GetStringAsync(betaAddress+"/v3/registration/matrixcode.test.isolated/index.json")).Contains(betaAddress+"/v3/flatcontainer/"));
   Check("B nuspec different",(await http.GetStringAsync(betaAddress+"/v3/flatcontainer/matrixcode.test.isolated/1.0.0/matrixcode.test.isolated.nuspec")).Contains("Different artifact"));
   await service.PostGetMeta_NuPakFeedUpdate(beta.Id,"Beta",null,false,true);
   Check("feed disable immediate",(await http.GetAsync(betaAddress+"/v3/index.json")).StatusCode==HttpStatusCode.NotFound);
   Check("feed toggle preserves A",(await alphaHttp.GetAsync(address+"/v3/index.json")).IsSuccessStatusCode);
   await service.PostGetMeta_NuPakFeedUpdate(beta.Id,"Beta",null,true,true);
   http.DefaultRequestHeaders.Add("X-NuGet-ApiKey","wrong");Check("bad API key anonymous feed 401",(await http.GetAsync(betaAddress+"/v3/index.json")).StatusCode==HttpStatusCode.Unauthorized);http.DefaultRequestHeaders.Remove("X-NuGet-ApiKey");
   await service.PostGetMeta_NuPakSetEnabled(false);
   Check("server off hides both feeds",(await http.GetAsync(betaAddress+"/v3/index.json")).StatusCode==HttpStatusCode.NotFound&&(await alphaHttp.GetAsync(address+"/v3/index.json")).StatusCode==HttpStatusCode.NotFound);
   Check("server off keeps per-feed toggle",(await service.GetMeta_NuPakFeed(beta.Id)).Enabled&&(await service.GetMeta_NuPakFeed(feedId)).Enabled);
   await service.PostGetMeta_NuPakSetEnabled(true);
   await Reject("A robot cannot push B",async()=>await NuPakOperations.PushAsync(db,store,betaFeed,await Package(version:"9.0.0"),alphaRobot,new("Robot",alphaRobot,"alpha",null)),403);db.ChangeTracker.Clear();
   await Reject("B robot cannot push A",async()=>await NuPakOperations.PushAsync(db,store,feed,await Package(version:"9.0.0"),betaRobot,new("Robot",betaRobot,"beta",null)),403);db.ChangeTracker.Clear();
   var ordinary=await service.PostGetMeta_NuPakFeedCreate("default","Ordinary",null);
   await service.PostGetMeta_NuPakFeedUpdate(ordinary.Id,"Ordinary",null,true,true);
   foreach(var method in new[]{System.Net.Http.HttpMethod.Get,System.Net.Http.HttpMethod.Head,System.Net.Http.HttpMethod.Put,System.Net.Http.HttpMethod.Delete})
      foreach(var old in new[]{"/v3/index.json","/v2/package"}) Check("legacy 404 "+method+old,(await http.SendAsync(new HttpRequestMessage(method,baseAddress+old))).StatusCode==HttpStatusCode.NotFound);
   foreach(var slug in new[]{"unknown","v2","v3","bad_slug","UPPER","a%2Fb"})Check("invalid/unknown slug 404 "+slug,(await http.GetAsync(baseAddress+"/"+slug+"/v3/index.json")).StatusCode==HttpStatusCode.NotFound);
   var raceFeed=await service.PostGetMeta_NuPakFeedCreate("race-delete","Race",null);await service.PostGetMeta_NuPakFeedUpdate(raceFeed.Id,"Race",null,true,false);
   var racePrefix=await service.PostGetMeta_NuPakPrefixCreate(raceFeed.Id,"MatrixCode.Test.",null);await access.SetAsync(alphaRobot,racePrefix.Id,"W",default);db.ChangeTracker.Clear();
   var raceMetadata=await db.Feeds.SingleAsync(f=>f.cNuPakFeedId==raceFeed.Id);
   async Task<bool> DeleteRace() {
      await using var context=new NuPakDbContext(options);var svc=new NuPakServices(context,store,settings);
      typeof(ServicesBase).GetProperty("HttpContext")!.SetValue(svc,new DefaultHttpContext());typeof(ServicesBase).GetProperty("Request")!.SetValue(svc,new ActionRequest{cUserId=$"{Ulid.NewUlid()}",cUserAccount="race"});
      try {await svc.PostMeta_NuPakFeedDelete(raceFeed.Id);return true;}catch(ActionException ex)when(ex.StatusCode==409){return false;}
   }
   async Task<bool> PushDeleteRace() {
      await using var context=new NuPakDbContext(options);try {await NuPakOperations.PushAsync(context,store,raceMetadata,await Package(id:"MatrixCode.Test.DeleteRace"),alphaRobot,new("Robot",alphaRobot,"race",null));return true;}catch(ActionException ex)when(ex.StatusCode==404){return false;}
   }
   var deleteWinners=await Task.WhenAll(DeleteRace(),PushDeleteRace());db.ChangeTracker.Clear();
   Check("feed delete vs push has exactly one winner",deleteWinners.Count(v=>v)==1);
   if(await db.Feeds.AnyAsync(f=>f.cNuPakFeedId==raceFeed.Id)) {
      var v=await (from pack in db.Packages join ver in db.Versions on pack.cNuPakPackageId equals ver.cNuPakPackageId where pack.cNuPakFeedId==raceFeed.Id select ver.cNuPakVersionId).SingleAsync();
      await service.PostMeta_NuPakVersionRecycle(raceFeed.Id,v);await service.PostMeta_NuPakRecycleBinEmpty(raceFeed.Id,null);await service.PostMeta_NuPakFeedDelete(raceFeed.Id);
   }
   Check("feed delete race preserves A/B",await db.Versions.AnyAsync(v=>v.cNuPakVersionId==alphaVersion.cNuPakVersionId)&&await db.Versions.AnyAsync(v=>v.cNuPakVersionId==betaVersion.cNuPakVersionId));
   await service.PostMeta_NuPakFeedDelete(ordinary.Id);db.ChangeTracker.Clear();
   Check("ordinary default removable; historical audit retained",await db.Audits.AnyAsync(a=>a.cNuPakAuditFeedSlug=="default"&&a.cNuPakFeedId==null));
   await Sql(File.ReadAllText(Path.Combine(repo,"doc/sqlscript/mssql/updates/20261003-NuPakMultiFeed.sql")),isolated.ConnectionString);
   await Sql(File.ReadAllText(Path.Combine(repo,"doc/sqlscript/mssql/sets/NuPak.sql")),isolated.ConnectionString);
   Check("rerun populated multi-feed preserves artifacts",await db.Feeds.CountAsync()==2&&await db.Versions.AnyAsync(v=>v.cNuPakVersionId==betaVersion.cNuPakVersionId));
   try {await Sql($"UPDATE dbo.ta_NuPakPackage SET cNuPakPrefixId='{narrow.Id}' WHERE cNuPakPackageId='{betaPackage.cNuPakPackageId}'",isolated.ConnectionString);throw new Exception("Cross-feed FK accepted");}
   catch(SqlException ex)when(ex.Number==547){Check("composite FK rejects cross-feed prefix",true);}
   var cliA=await RawSpec("<package><metadata><id>MatrixCode.Test.CliIdentical</id><version>1.0.0</version><authors>A</authors><description>Artifact A</description></metadata></package>");
   var cliB=await RawSpec("<package><metadata><id>MatrixCode.Test.CliIdentical</id><version>1.0.0</version><authors>B</authors><description>Artifact B</description></metadata></package>");
   var cliFileA=Path.Combine(root,"cli-a.nupkg");var cliFileB=Path.Combine(root,"cli-b.nupkg");File.Move(cliA.Path,cliFileA);File.Move(cliB.Path,cliFileB);
   cliToken=alphaToken;cliName="alpha-robot";
   await Cli("nuget","push",cliFileA,"--source",address+"/v3/index.json","--api-key",alphaToken,"--allow-insecure-connections");
   cliToken=betaToken;cliName="beta-robot";
   await Cli("nuget","push",cliFileB,"--source",betaAddress+"/v3/index.json","--api-key",betaToken,"--allow-insecure-connections");
   File.WriteAllText(restoreProject,"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include=\"MatrixCode.Test.CliIdentical\" Version=\"1.0.0\" /></ItemGroup></Project>");
   foreach(var fixture in new[]{(Name:"a",Source:address,Token:alphaToken,Robot:"alpha-robot",Hash:cliA.Hash),(Name:"b",Source:betaAddress,Token:betaToken,Robot:"beta-robot",Hash:cliB.Hash)}) {
      cliToken=fixture.Token;cliName=fixture.Robot;
      File.WriteAllText(restoreConfig,$"<configuration><packageSources><clear/><add key=\"Em\" value=\"{fixture.Source}/v3/index.json\" allowInsecureConnections=\"true\"/></packageSources></configuration>");
      var cache=Path.Combine(root,"cache-"+fixture.Name);
      await Cli("restore",restoreProject,"--configfile",restoreConfig,"--packages",cache,"--no-cache","--force");
      var downloaded=File.ReadAllBytes(Path.Combine(cache,"matrixcode.test.cliidentical/1.0.0/matrixcode.test.cliidentical.1.0.0.nupkg"));
      Check("real CLI identical ID/version isolated hash "+fixture.Name,Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(downloaded))==fixture.Hash);
   }
   foreach(var v in await db.Versions.Where(v=>v.cNuPakVersionState==1&&db.Packages.Any(p=>p.cNuPakFeedId==beta.Id&&p.cNuPakPackageId==v.cNuPakPackageId)&&v.cNuPakVersionId!=betaVersion.cNuPakVersionId).Select(v=>v.cNuPakVersionId).ToArrayAsync())await service.PostMeta_NuPakVersionRecycle(beta.Id,v);
   await service.PostMeta_NuPakVersionRecycle(beta.Id,betaVersion.cNuPakVersionId);
   var betaFile=store.PackagePath(beta.Id,"MatrixCode.Test.Isolated","1.0.0");File.Move(betaFile,betaFile+".purge");await store.RecoverPurgesAsync(db,default);
   Check("purge recovery B preserves A",File.Exists(betaFile)&&File.Exists(store.PackagePath(feedId,"MatrixCode.Test.Isolated","1.0.0")));
   await service.PostMeta_NuPakRecycleBinEmpty(beta.Id,null);await service.PostMeta_NuPakFeedDelete(beta.Id);db.ChangeTracker.Clear();
   Check("delete feed removes prefixes/grants; keeps other feed",!await db.Prefixes.AnyAsync(p=>p.cNuPakFeedId==beta.Id)&&await db.Feeds.AnyAsync(f=>f.cNuPakFeedId==feedId));
   await service.PostMeta_NuPakVersionRecycle(feedId,alphaVersion.cNuPakVersionId);
   // Remove the remaining fixture active versions only in this isolated database.
   foreach(var v in await db.Versions.Where(v=>v.cNuPakVersionState==1).Select(v=>v.cNuPakVersionId).ToArrayAsync())await service.PostMeta_NuPakVersionRecycle(feedId,v);
   await service.PostMeta_NuPakRecycleBinEmpty(feedId,null);await service.PostMeta_NuPakFeedDelete(feedId);db.ChangeTracker.Clear();
   Check("last feed deletion returns to zero",(await service.GetMeta_NuPakFeeds()).Length==0&&(await service.GetMeta_NuPakStatus()).Enabled);
   Console.WriteLine($"{checks} checks passed in isolated SQL database and local HTTP server.");
} finally {
   if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
   SqlConnection.ClearAllPools();
   if (created && dbName.StartsWith("EmNuPakSmoke_") && dbName.Length == 47) await Sql($"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]", master.ConnectionString);
   if (Directory.Exists(root) && Path.GetFileName(root) == dbName && Path.GetDirectoryName(root) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)) Directory.Delete(root, true);
}

public class PublisherNuPakProxy:DispatchProxy {
 public INuPakServices Backend=null!;
 protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.Name=="GetMeta_NuPakStorageStatus" ? Task.FromResult(new StorageFeatureStatus(false,true,true,false)) : method.Invoke(Backend,args);
}
