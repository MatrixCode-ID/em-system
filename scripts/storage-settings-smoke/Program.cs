using System.Reflection;
using Em;
using System.Text.Json;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Api.Core.Registry;
using Em.Api.Core.Storage;
using Em.Shared;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var root = Path.Combine(Path.GetTempPath(), "em-settings-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var checks = 0;
void Check(string name, bool ok) { if (!ok) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
void Reject(string name, Action action, int? code = null) {
   try { action(); throw new Exception("FAIL accepted: " + name); }
   catch (ActionException ex) { Check(name, code is null || ex.StatusCode == code); }
   catch (InvalidOperationException) when (code is null) { Check(name, true); }
}
var cdn = new StorageFeatureSettings { Enabled = true, Directory = "data/cdn", MaxUploadMb = 200 };
var registry = new StorageFeatureSettings { Enabled = true, Directory = "data/registry" };
var persistence = new MemoryPersistence();
ManagedStorageSettings Create(IStorageSettingsPersistence? p) => new(root, p, cdn, registry, ["data/binary", "data/tasks"]);
try {
   var store = Create(persistence);
   Check("defaults preserve enabled/200 MB", store.Detail(true).Active == cdn && store.Status(false).ActiveEnabled);
   Check("default revision zero", store.Detail(true).Revision == 0);
   Check("legacy document NuGet defaults",store.Active(2).Enabled&&store.Active(2).Directory=="./data/nuget"&&store.Active(2).MaxUploadMb==250);
   var nugetSave=store.Save(2,new(0,store.Active(2) with {Enabled=false,MaxUploadMb=300}));
   Check("NuGet save requires restart and immutable active",nugetSave.RequiresRestart&&store.Active(2).Enabled&&nugetSave.Saved.MaxUploadMb==300);
   persistence.Value=null;store=Create(persistence);
   var draft = cdn with { Enabled = false, MaxUploadMb = 9 };
   var saved = store.Save(true, new(0, draft));
   Check("save pending, active remains enabled", saved.RequiresRestart && store.Status(true).ActiveEnabled && !saved.Saved.Enabled);
   var restarted = Create(persistence);
   Check("restart applies saved values", !restarted.Status(true).ActiveEnabled && !restarted.Status(true).RequiresRestart && restarted.Detail(true).Active.MaxUploadMb == 9);
   Check("unmodified registry retained", restarted.Detail(false).Saved == registry);
   Reject("stale revision rejected", () => store.Save(false, new(0, registry)), 409);
   Check("stale revision preserved other feature", store.Detail(true).Saved == draft);
   store.Save(false, new(1, registry with { Enabled = false }));
   Check("feature mutation preserves CDN", store.Detail(true).Saved == draft);
   store.Save(true, new(2, cdn));
   Check("restoring active cancels pending", !store.Status(true).RequiresRestart);
   var revision = store.Detail(true).Revision;
   persistence.Fail = true;
   try { store.Save(true, new(revision, draft)); throw new Exception("write unexpectedly accepted"); } catch (IOException) { Check("write failure surfaced", true); }
   Check("write failure does not change snapshot/revision", store.Detail(true).Revision == revision && store.Detail(true).Saved == cdn);
   persistence.Fail = false;
   var parallelResults = await Task.WhenAll(new[] { true, false }.Select(feature => Task.Run(() => {
      try { store.Save(feature, new(revision, feature ? draft : registry)); return true; }
      catch (ActionException ex) when (ex.StatusCode == 409) { return false; }
   })));
   Check("concurrent same revision has one winner", parallelResults.Count(x => x) == 1);
   Check("single shared revision increment", store.Detail(true).Revision == revision + 1);
   var absent = Path.Combine(root, "data", "new-cdn");
   Check("validate relative path resolves server root", store.Validate(true, cdn with { Directory = "data/new-cdn" }).AbsoluteDirectory == absent);
   Check("validate does not create directory", !Directory.Exists(absent));
   store.Save(true, new(store.Detail(true).Revision, cdn with { Directory = "data/new-cdn" }));
   Check("save enabled explicitly creates directory", Directory.Exists(absent));
   foreach (var path in new[] { "", "data/registry", "data/registry/child", "data", "data/binary", "data/tasks/child", ".", "bin", "../" })
      Check("unsafe path rejected: " + path, !store.Validate(true, cdn with { Directory = path }).Valid);
   Check("zero upload limit rejected", !store.Validate(true, cdn with { MaxUploadMb = 0 }).Valid);
   foreach (var path in new[] { "data/registry.", "data/registry:stream", "data/NUL", "data/CONT~1" })
      Check("ambiguous Windows path rejected: " + path, !store.Validate(true, cdn with { Directory = path }).Valid);
   var secretRoot = Path.Combine(root, "data", "secrets-test"); Directory.CreateDirectory(secretRoot);
   File.WriteAllText(Path.Combine(secretRoot, "emapi-config.json"), "fixture");
   Check("known secret file rejected", !store.Validate(true, cdn with { Directory = secretRoot }).Valid);
   if (OperatingSystem.IsWindows()) {
      var junction = Path.Combine(root, "data", "junction");
      var childJunction = Path.Combine(absent, "registry-link");
      foreach (var link in new[] { junction, childJunction }) {
         var info = new System.Diagnostics.ProcessStartInfo("powershell") { UseShellExecute = false, CreateNoWindow = true };
         info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-Command");
         info.ArgumentList.Add($"New-Item -ItemType Junction -Path '{link}' -Target '{Path.Combine(root, "data", "registry")}' -ErrorAction Stop | Out-Null");
         using var process = System.Diagnostics.Process.Start(info)!; process.WaitForExit();
         if (process.ExitCode != 0) throw new Exception("Could not create isolated junction fixture");
      }
      try {
         Check("junction root rejected", !store.Validate(true, cdn with { Directory = junction }).Valid);
         Check("junction descendant rejected", !store.Validate(true, cdn with { Directory = absent }).Valid);
         Check("junction parent escape rejected", !store.Validate(true, cdn with { Directory = junction + "/new" }).Valid);
      }
      finally { Directory.Delete(junction); Directory.Delete(childJunction); }
   }
   File.WriteAllText(Path.Combine(root, "data", "file"), "untouched");
   Check("file path rejected", !store.Validate(true, cdn with { Directory = "data/file" }).Valid);
   Check("user file untouched", File.ReadAllText(Path.Combine(root, "data", "file")) == "untouched");
   Reject("registry completeness guard invoked", () => store.Save(false, new(store.Detail(false).Revision, registry with { Directory = "data/registry-copy" }), _ => throw new ActionException("incomplete", 409)), 409);
   Check("failed registry check does not create target", !Directory.Exists(Path.Combine(root, "data", "registry-copy")));
   var staticStore = Create(null);
   Check("static reports capability unavailable", !staticStore.Status(true).Managed);
   Reject("static save rejected clearly", () => staticStore.Save(true, new(0, cdn)), 409);
   Reject("static validate rejected", () => staticStore.Validate(true, cdn), 409);
   var invalid = new MemoryPersistence { Value = "{}" };
   Reject("invalid document rejects startup", () => Create(invalid));
   invalid.Value = JsonSerializer.Serialize(new StorageSettingsDocument { Version = 99, Cdn = cdn, Registry = registry });
   Reject("unknown version rejects startup", () => Create(invalid));
   Check("probe cleanup", !Directory.EnumerateFiles(root, ".em-storage-probe-*", SearchOption.AllDirectories).Any());
   Check("status DTO contains no directory/path", !typeof(StorageFeatureStatus).GetProperties().Any(p => p.Name.Contains("Directory") || p.Name.Contains("Path")));
   foreach (var type in new[] { typeof(CdnServices), typeof(CtnServices) }) {
      var prefix = type == typeof(CdnServices) ? "Cdn" : "Ctn";
      var claim = type == typeof(CdnServices) ? ICdnServices.SettingsClaim : ICtnServices.SettingsClaim;
      foreach (var method in new[] { $"GetMeta_{prefix}Settings", $"PostGetMeta_{prefix}ValidateDirectory", $"PostGetMeta_{prefix}SettingsSave" }) {
         var member = type.GetMethod(method)!;
         var actual = member.GetCustomAttribute<GetActionAttribute>()?.Claim ?? member.GetCustomAttribute<PostActionAttribute>()?.Claim;
         Check("API settings claim: " + method, actual == claim);
      }
   }
   Check("host keys isolate hosts", MetaStorageSettingsPersistence.CreateHostKey("default", "A", root) != MetaStorageSettingsPersistence.CreateHostKey("default", "B", root));
   var integrity = typeof(CtnServices).Assembly.GetType("Em.Api.Core.Registry.RegistryStorageIntegrity")!.GetMethod("VerifyBlob", BindingFlags.NonPublic | BindingFlags.Static)!;
   var bytes = new byte[] { 7, 11, 19 };
   var digest = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
   var hex = digest[7..];
   var target = Path.Combine(root, "data", "blob-fixture");
   var blobFile = Path.Combine(target, "blobs", "sha256", hex[..2], hex);
   void VerifyBlob() {
      try { integrity.Invoke(null, [target, digest, (long)bytes.Length]); }
      catch (TargetInvocationException ex) { throw ex.InnerException!; }
   }
   Reject("real integrity missing referenced blob rejected", VerifyBlob, 409);
   Directory.CreateDirectory(Path.GetDirectoryName(blobFile)!); File.WriteAllBytes(blobFile, bytes);
   VerifyBlob(); Check("real integrity complete blob accepted", true);
   File.WriteAllBytes(blobFile, [1, 2, 3]);
   Reject("real integrity same-size corrupt blob rejected", VerifyBlob, 409);
   File.WriteAllBytes(blobFile, [1]);
   Reject("real integrity wrong-size blob rejected", VerifyBlob, 409);
   if (args.Contains("--sql")) await SqlTests();
   Console.WriteLine($"{checks} checks passed.");
}
finally {
   // Only delete this process's unique fixture directory, never a configured payload root.
   if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
       && Path.GetFileName(root).StartsWith("em-settings-test-")) Directory.Delete(root, true);
}

async Task SqlTests() {
   var local = JsonDocument.Parse(File.ReadAllText("../.artefacts/em-system/config/emapi-config.json"));
   var connection = Environment.GetEnvironmentVariable("EM_DB_CONNECTION_STRING") ?? local.RootElement.GetProperty("database").GetProperty("connectionString").GetString()!;
   var database = "EmSettingsTest_" + Guid.NewGuid().ToString("N");
   var cs = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master", Pooling = false };
   await using var admin = new SqlConnection(cs.ConnectionString); await admin.OpenAsync();
   await using (var command = admin.CreateCommand()) { command.CommandText = $"CREATE DATABASE [{database}]"; await command.ExecuteNonQueryAsync(); }
   try {
      cs.InitialCatalog = database;
      await using (var fixture = new ApiCoreContext(new DbContextOptionsBuilder<ApiCoreContext>().UseSqlServer(cs.ConnectionString).Options))
         await fixture.Database.EnsureCreatedAsync();
      var sql = new MetaStorageSettingsPersistence("fixture", () => new ApiCoreContext(new DbContextOptionsBuilder<ApiCoreContext>().UseSqlServer(cs.ConnectionString).Options));
      var host = Create(sql);
      host.Save(true, new(0, cdn with { Enabled = false }));
      Check("SQL ta_Meta persistence after restart", !Create(sql).Status(true).ActiveEnabled);
      var competing = Create(sql);
      host.Save(false, new(1, registry with { Enabled = false }));
      Reject("SQL external revision conflict", () => competing.Save(true, new(1, cdn)), 409);
      Check("SQL external reload", competing.Detail(false).Revision == 2 && !competing.Detail(false).Saved.Enabled);
      Check("SQL two feature preservation", !Create(sql).Status(true).ActiveEnabled && !Create(sql).Status(false).ActiveEnabled);
      Reject("SQL persistence CAS", () => sql.Write(0, "{}"), 409);
      var current = JsonSerializer.Deserialize<StorageSettingsDocument>(sql.Read()!)!;
      using var barrier = new Barrier(2);
      var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => {
         barrier.SignalAndWait();
         try { sql.Write(current.Revision, JsonSerializer.Serialize(current with { Revision = current.Revision + 1 })); return true; }
         catch (ActionException ex) when (ex.StatusCode == 409) { return false; }
      })));
      Check("SQL simultaneous writers one commit one conflict", outcomes.Count(x => x) == 1);
      await HttpTests(cs.ConnectionString);
   }
   finally {
      await using var command = admin.CreateCommand();
      command.CommandText = $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]";
      await command.ExecuteNonQueryAsync();
   }
}

async Task HttpTests(string connection) {
   var contextType = typeof(CtnServices).Assembly.GetType("Em.Api.Core.Registry.CtnContext")!;
   var registryOptions = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
   registryOptions.UseSqlServer(connection);
   using (var registryContext = (DbContext)Activator.CreateInstance(contextType, registryOptions.Options)!) {
      foreach (var batch in System.Text.RegularExpressions.Regex.Split(registryContext.Database.GenerateCreateScript(), @"^GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline))
         if (!string.IsNullOrWhiteSpace(batch)) await registryContext.Database.ExecuteSqlRawAsync(batch);
   }
   await using (var db = new ApiCoreContext(new DbContextOptionsBuilder<ApiCoreContext>().UseSqlServer(connection).Options)) {
      foreach (var id in new[] { "limited", "manager", "settings" }) {
         db.ta_Users.Add(new ta_User { cUserId = id, cUserAccount = id, cUserState = UserState.Active, datestamp = DateTime.UtcNow, ustamp = DateTime.UtcNow });
         var claims = id == "limited" ? Array.Empty<string>() : id == "manager"
            ? new[] { ICdnServices.CdnClaim, ICtnServices.CtnClaim, INuPakServices.ManagerClaim }
            : new[] { ICdnServices.CdnClaim, ICtnServices.CtnClaim, ICdnServices.SettingsClaim, ICtnServices.SettingsClaim, INuPakServices.ManagerClaim, INuPakServices.SettingsClaim, IRobotServices.RobotClaim };
         foreach (var claim in claims) db.ta_UserClaims.Add(new ta_UserClaim { cUserClaimId = Ulid.NewUlid().ToString(), cUserId = id,
            cUserClaimName = Defaults.AdministrativeToolsModuleName + ":" + claim, cUserClaimStart = DateTime.UtcNow.AddDays(-1), cUserClaimExpiry = DateTime.UtcNow.AddDays(1), datestamp = DateTime.UtcNow });
      }
      await db.SaveChangesAsync();
   }
   // This isolated fixture uses EF-created robot keys (nvarchar(450)); production SQL uses char(26).
   var nuPakScript=File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"doc/sqlscript/mssql/sets/NuPak.sql"))
      .Replace("cNuPakVersionPushedBy_cRobotId char(26)", "cNuPakVersionPushedBy_cRobotId nvarchar(450)")
      .Replace("cRobotId char(26)", "cRobotId nvarchar(450)");
   await using(var np=new SqlConnection(connection)) {
      await np.OpenAsync();foreach(var batch in System.Text.RegularExpressions.Regex.Split(nuPakScript,@"^GO\s*$",System.Text.RegularExpressions.RegexOptions.Multiline|System.Text.RegularExpressions.RegexOptions.IgnoreCase)) {
         if(string.IsNullOrWhiteSpace(batch))continue;await using var command=new SqlCommand(batch,np);await command.ExecuteNonQueryAsync();
      }
   }
   var disabledCdn = cdn with { Enabled = false };
   var disabledRegistry = registry with { Enabled = false };
   async Task<(EmApp App, WebApplication Web, HttpClient Client)> Start(bool managed) {
      var engine = EmApp.BuildApp(["--contentRoot", root], b => {
         b.SetDbProvider(connection); b.ActionRateLimit = -1;
         if (managed) { b.AddManagedStorageSettings("http-fixture", disabledCdn, disabledRegistry); b.AddNuPak(); } else b.AddNuPak("data/nuget");
      });
      var web = (WebApplication)typeof(EmApp).GetField("_webApplication", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
      if (managed) {
         typeof(CtnServices).Assembly.GetType("Em.Api.Core.Registry.CtnStartupChecks")!.GetMethod("VerifyTables")!.Invoke(null, [engine.ServiceProvider]);
         using var scope = engine.ServiceProvider.CreateScope();
         typeof(CtnServices).Assembly.GetType("Em.Api.Core.Registry.RegistryStorageIntegrity")!.GetMethod("VerifyStartup", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [scope.ServiceProvider.GetRequiredService(contextType), Path.Combine(root, "data", "registry")]);
      }
      web.Urls.Clear(); web.Urls.Add("http://127.0.0.1:0");
      web.MapMethods("/api/{module}/{action}", ["GET", "POST"], (string module, string action, HttpContext http) => engine.ProcessRequest(module, action, http));
      foreach (var method in new[] { "MapCdn", "MapContainerRegistry" }) typeof(EmApp).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [web]);
      foreach(var endpoint in ((ValueTuple<string,RequestDelegate>[])typeof(EmApp).GetField("_publicEndpoints",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(engine)!))web.Map(endpoint.Item1,branch=>branch.Run(endpoint.Item2));
      await web.StartAsync();
      return (engine, web, new HttpClient { BaseAddress = new Uri(web.Urls.Single()) });
   }
   async Task<string> Token(EmApp app, string id) {
      using var scope = app.ServiceProvider.CreateScope();
      var type = typeof(EmApp).Assembly.GetType("Em.Api.Core.ITokenServices")!;
      var service = scope.ServiceProvider.GetRequiredService(type);
      return (await (Task<TokenResult>)type.GetMethod("IssueAsync")!.Invoke(service, [id])!).AccessToken;
   }
   async Task<(int Code, string Body)> Call(HttpClient client, string method, string action, string? token = null, object? parameter = null) {
      using var request = new HttpRequestMessage(new System.Net.Http.HttpMethod(method), "/api/Administrative%20Tools/" + action);
      if (token is not null) request.Headers.Authorization = new("Bearer", token);
      if (parameter is not null) {
         var arguments = parameter is object?[] array ? array : new[] { parameter };
         var payload = arguments.Select((value, ordinal) => new { ParameterType = value?.GetType().FullName, ParameterOrdinal = ordinal, ValueData = value }).Where(p => p.ValueData is not null);
         request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
      }
      using var response = await client.SendAsync(request);
      return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
   }
   var running = await Start(true);
   try {
      var limited = await Token(running.App, "limited");
      var manager = await Token(running.App, "manager");
      var settings = await Token(running.App, "settings");
      foreach (var feature in new[] { "Cdn", "Ctn" }) {
         Check("HTTP unauthenticated detail denied " + feature, (await Call(running.Client, "GET", $"GetMeta_{feature}Settings")).Code == 401);
         Check("HTTP without manager claim denied " + feature, (await Call(running.Client, "GET", $"GetMeta_{feature}Status", limited)).Code == 403);
         Check("HTTP old manager claim cannot read paths " + feature, (await Call(running.Client, "GET", $"GetMeta_{feature}Settings", manager)).Code == 403);
         var status = await Call(running.Client, "GET", $"GetMeta_{feature}Status", manager);
         Check("HTTP general status contains no path " + feature, status.Code == 200 && !status.Body.Contains("Directory") && !status.Body.Contains(root));
         Check("HTTP settings available when disabled " + feature, (await Call(running.Client, "GET", $"GetMeta_{feature}Settings", settings)).Code == 200);
         var detail = JsonDocument.Parse((await Call(running.Client, "GET", $"GetMeta_{feature}Settings", settings)).Body).RootElement.GetProperty("Data");
         var request = new StorageSettingsSave(detail.GetProperty("Revision").GetInt64(), feature == "Cdn" ? cdn with { MaxUploadMb = 1 } : registry);
         Check("HTTP old claim cannot save " + feature, (await Call(running.Client, "POST", $"PostGetMeta_{feature}SettingsSave", manager, request)).Code == 403);
         Check("HTTP settings claim saves when disabled " + feature, (await Call(running.Client, "POST", $"PostGetMeta_{feature}SettingsSave", settings, request)).Code == 200);
         Check("HTTP stale revision conflicts " + feature, (await Call(running.Client, "POST", $"PostGetMeta_{feature}SettingsSave", settings, request)).Code == 409);
      }
      var nuDetail=JsonDocument.Parse((await Call(running.Client,"GET","GetMeta_NuPakSettings",settings)).Body).RootElement.GetProperty("Data");
      Check("HTTP NuGet default without legacy section",nuDetail.GetProperty("Active").GetProperty("Enabled").GetBoolean()&&nuDetail.GetProperty("Active").GetProperty("MaxUploadMb").GetInt32()==250);
      var nuRequest=new StorageSettingsSave(nuDetail.GetProperty("Revision").GetInt64(),new() {Enabled=false,Directory="data/nuget",MaxUploadMb=300});
      Check("HTTP NuGet manager cannot save settings",(await Call(running.Client,"POST","PostGetMeta_NuPakSettingsSave",manager,nuRequest)).Code==403);
      var nuSaved=await Call(running.Client,"POST","PostGetMeta_NuPakSettingsSave",settings,nuRequest);
      Check("HTTP NuGet save requires restart",nuSaved.Code==200&&JsonDocument.Parse(nuSaved.Body).RootElement.GetProperty("Data").GetProperty("RequiresRestart").GetBoolean());
      var pending = JsonDocument.Parse((await Call(running.Client, "GET", "GetMeta_CdnStatus", manager)).Body).RootElement.GetProperty("Data");
      Check("HTTP save leaves disabled runtime until restart", !pending.GetProperty("ActiveEnabled").GetBoolean() && pending.GetProperty("RequiresRestart").GetBoolean());
      Check("HTTP public CDN remains disabled before restart", (int)(await running.Client.GetAsync("/cdn/payload.txt")).StatusCode == 404);
      Check("HTTP OCI remains disabled", (int)(await running.Client.GetAsync("/v2/")).StatusCode == 404);
   }
   finally { running.Client.Dispose(); await running.Web.StopAsync(); await running.Web.DisposeAsync(); }
   string? robotAuthorization = null;
   string? pushedDigest = null;
   running = await Start(true);
   try {
      var settings = await Token(running.App, "settings");
      var status = JsonDocument.Parse((await Call(running.Client, "GET", "GetMeta_CdnStatus", settings)).Body).RootElement.GetProperty("Data");
      Check("HTTP restart applies enabled and clears pending", status.GetProperty("ActiveEnabled").GetBoolean() && !status.GetProperty("RequiresRestart").GetBoolean());
      var nuStatus=JsonDocument.Parse((await Call(running.Client,"GET","GetMeta_NuPakStorageStatus",settings)).Body).RootElement.GetProperty("Data");
      Check("HTTP NuGet restart applies disabled",!nuStatus.GetProperty("ActiveEnabled").GetBoolean()&&!nuStatus.GetProperty("RequiresRestart").GetBoolean());
      Check("HTTP NuGet disabled endpoint 404",(int)(await running.Client.GetAsync("/nuget/unknown/v3/index.json")).StatusCode==404);
      File.WriteAllText(Path.Combine(root, "data", "cdn", "payload.txt"), "fixture payload");
      Check("HTTP public CDN download after restart", await running.Client.GetStringAsync("/cdn/payload.txt") == "fixture payload");
      using var range = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, "/cdn/payload.txt"); range.Headers.Range = new(0, 6);
      using var response = await running.Client.SendAsync(range);
      Check("HTTP public CDN Range", (int)response.StatusCode == 206 && await response.Content.ReadAsStringAsync() == "fixture");
      using (var upload = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/api/Administrative%20Tools/PostGetMeta_CdnUpload")) {
         upload.Headers.Authorization = new("Bearer", settings);
         upload.Headers.Add(Defaults.StreamPayloadHeader, StreamPayloadProtocol.Encode(new CdnUploadRequest { FileName = "too-large.bin" }));
         upload.Content = new ByteArrayContent(new byte[1024 * 1024 + 1]);
         using var oversized = await running.Client.SendAsync(upload);
         Check("HTTP restarted upload limit enforced", (int)oversized.StatusCode == 413 && !File.Exists(Path.Combine(root, "data", "cdn", "too-large.bin")));
      }
      var settingsDetail = JsonDocument.Parse((await Call(running.Client, "GET", "GetMeta_CdnSettings", settings)).Body).RootElement.GetProperty("Data");
      var delayedContent = new DelayedContent();
      using (var upload = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/api/Administrative%20Tools/PostGetMeta_CdnUpload")) {
         upload.Headers.Authorization = new("Bearer", settings);
         upload.Headers.Add(Defaults.StreamPayloadHeader, StreamPayloadProtocol.Encode(new CdnUploadRequest { FileName = "ongoing.bin" }));
         upload.Content = delayedContent;
         var inProgress = running.Client.SendAsync(upload);
         try {
            await delayedContent.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var save = new StorageSettingsSave(settingsDetail.GetProperty("Revision").GetInt64(), disabledCdn with { MaxUploadMb = 1 });
            Check("HTTP pending disable saved during streaming upload", (await Call(running.Client, "POST", "PostGetMeta_CdnSettingsSave", settings, save)).Code == 200);
         }
         finally { delayedContent.Release.TrySetResult(); }
         using var finished = await inProgress;
         Check("HTTP pending disable does not interrupt upload", (int)finished.StatusCode == 200 && new FileInfo(Path.Combine(root, "data", "cdn", "ongoing.bin")).Length == 1024);
      }
      JsonElement Data((int Code, string Body) result) {
         if (result.Code != 200) throw new Exception(result.Body);
         return JsonDocument.Parse(result.Body).RootElement.GetProperty("Data");
      }
      var rootId = Data(await Call(running.Client, "POST", "PostGetMeta_CtnRootCreate", settings, new object?[] { "fixture", null })).GetProperty("Id").GetString()!;
      _ = Data(await Call(running.Client, "POST", "PostGetMeta_CtnImageCreate", settings, new object?[] { rootId, null, "image", null }));
      var robot = Data(await Call(running.Client, "POST", "PostGetMeta_RobotCreate", settings, new object?[] { "fixture-robot", null, null, null }));
      var robotId = robot.GetProperty("Robot").GetProperty("Id").GetString()!;
      Check("HTTP robot grant setup", (await Call(running.Client, "POST", "PostMeta_RobotAccessSet", settings, new object?[] { robotId, "Container", rootId, "W" })).Code == 200);
      robotAuthorization = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("fixture-robot:" + robot.GetProperty("Token").GetString()));
      async Task<HttpResponseMessage> Oci(string method, string path, byte[]? body = null) {
         using var request = new HttpRequestMessage(new System.Net.Http.HttpMethod(method), path);
         request.Headers.Authorization = new("Basic", robotAuthorization);
         if (body is not null) request.Content = new ByteArrayContent(body);
         return await running.Client.SendAsync(request);
      }
      using var login = await Oci("GET", "/v2/"); Check("HTTP OCI robot login", (int)login.StatusCode == 200);
      var blob = new byte[] { 4, 8, 15, 16, 23, 42 };
      pushedDigest = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(blob));
      using var begin = await Oci("POST", "/v2/fixture/image/blobs/uploads/");
      Check("HTTP OCI upload starts", (int)begin.StatusCode == 202);
      using var commit = await Oci("PUT", begin.Headers.Location!.ToString() + "?digest=" + pushedDigest, blob);
      Check("HTTP OCI push blob", (int)commit.StatusCode == 201);
      using var pull = await Oci("GET", "/v2/fixture/image/blobs/" + pushedDigest);
      Check("HTTP OCI pull blob", (int)pull.StatusCode == 200 && (await pull.Content.ReadAsByteArrayAsync()).SequenceEqual(blob));
      var configDetail = Data(await Call(running.Client, "GET", "GetMeta_CtnSettings", settings));
      var changeRoot = new StorageSettingsSave(configDetail.GetProperty("Revision").GetInt64(), registry with { Directory = "data/incomplete-target" });
      Check("HTTP populated registry root change to incomplete target rejected", (await Call(running.Client, "POST", "PostGetMeta_CtnSettingsSave", settings, changeRoot)).Code == 409);
      using (var noGrant = await Oci("GET", "/v2/other/image/blobs/" + pushedDigest)) Check("HTTP robot cannot access ungranted root", (int)noGrant.StatusCode == 404);
   }
   finally { running.Client.Dispose(); await running.Web.StopAsync(); await running.Web.DisposeAsync(); }
   running = await Start(true);
   try {
      using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, "/v2/fixture/image/blobs/" + pushedDigest);
      request.Headers.Authorization = new("Basic", robotAuthorization);
      using var response = await running.Client.SendAsync(request);
      Check("HTTP OCI grant/token/blob preserved after restart", (int)response.StatusCode == 200 && (await response.Content.ReadAsByteArrayAsync()).Length == 6);
      Check("HTTP pending CDN disable applies after restart", (int)(await running.Client.GetAsync("/cdn/payload.txt")).StatusCode == 404);
      Check("HTTP disabled CDN preserves payload data", File.Exists(Path.Combine(root, "data", "cdn", "payload.txt")));
   }
   finally { running.Client.Dispose(); await running.Web.StopAsync(); await running.Web.DisposeAsync(); }
   running = await Start(false);
   try {
      var settings = await Token(running.App, "settings");
      Check("HTTP static host Save returns clear conflict", (await Call(running.Client, "POST", "PostGetMeta_CdnSettingsSave", settings, new StorageSettingsSave(0, cdn))).Code == 409);
   }
   finally { running.Client.Dispose(); await running.Web.StopAsync(); await running.Web.DisposeAsync(); }
}

sealed class MemoryPersistence : IStorageSettingsPersistence
{
   public string Description => "isolated memory fixture";
   public string? Value;
   public bool Fail;
   public string? Read() => Value;
   public void Write(long expectedRevision, string json) {
      if (Fail) throw new IOException("simulated persistence failure");
      if ((Value is null ? 0 : JsonSerializer.Deserialize<StorageSettingsDocument>(Value)!.Revision) != expectedRevision) throw new ActionException("conflict", 409);
      Value = json;
   }
}

sealed class DelayedContent : HttpContent
{
   internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
   internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
   protected override bool TryComputeLength(out long length) { length = 0; return false; }
   protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) {
      await stream.WriteAsync(new byte[512]); await stream.FlushAsync(); Started.TrySetResult();
      await Release.Task;
      await stream.WriteAsync(new byte[512]);
   }
}
