using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Ui.Wpf.Publish;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GuiApp=Em.Ui.Wpf.Core.EmApp;

var repo=Path.GetFullPath(args.FirstOrDefault()??Environment.CurrentDirectory);var runner=new PublishProcessRunner();
try {var docker=await runner.RunAsync("docker",["version","--format","json"],repo);if(docker.ExitCode!=0){Console.WriteLine("PENDING Docker daemon unavailable.");return;}}catch(System.ComponentModel.Win32Exception){Console.WriteLine("PENDING Docker not installed.");return;}
using var config=JsonDocument.Parse(File.ReadAllText(Path.Combine(repo,"../.artefacts/em-system/config/emapi-config.json")),new JsonDocumentOptions {CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true});
var cs=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("EM_DB_CONNECTION_STRING")??config.RootElement.GetProperty("database").GetProperty("connectionString").GetString());
var database="EmPublishSmoke_"+Guid.NewGuid().ToString("N");var master=new SqlConnectionStringBuilder(cs.ConnectionString){InitialCatalog="master"};cs.InitialCatalog=database;
var root=Path.GetFullPath(Path.Combine(repo,"../.artefacts/em-system/publish-container-smoke",database));Directory.CreateDirectory(root);
var dockerConfig=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".docker","config.json");string? hash=File.Exists(dockerConfig)?PublishPaths.Hash(dockerConfig):null;
async Task Sql(string sql,string connection) {await using var db=new SqlConnection(connection);await db.OpenAsync();foreach(var batch in Regex.Split(sql,@"^GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase)) {if(string.IsNullOrWhiteSpace(batch))continue;await using var command=new SqlCommand(batch,db);await command.ExecuteNonQueryAsync();}}
WebApplication? web=null;bool created=false;int checks=0;
void Check(string name,bool value) {if(!value)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
try {
 await Sql($"CREATE DATABASE [{database}]",master.ConnectionString);created=true;
 await using(var core=new ApiCoreContext(new DbContextOptionsBuilder<ApiCoreContext>().UseSqlServer(cs.ConnectionString).Options))await core.Database.EnsureCreatedAsync();
 // Adapt only the isolated EF fixture's owner FK to its nvarchar(450) user key.
 var registrySchema=Regex.Replace(File.ReadAllText(Path.Combine(repo,"doc/sqlscript/mssql/sets/Ctn.sql")),@"\[cRobotOwner_cUserId\]\s+char\(26\)","[cRobotOwner_cUserId] nvarchar(450)");
 await Sql(registrySchema,cs.ConnectionString);
 var robot=Ulid.NewUlid().ToString();var rootId=Ulid.NewUlid().ToString();var token=RobotAuth.GenerateToken();
 await using(var sql=new SqlConnection(cs.ConnectionString)) {await sql.OpenAsync();await using var command=new SqlCommand("INSERT dbo.ta_Robot(cRobotId,cRobotName,cRobotState,cRobotTokenHash,cRobotTokenPrefix,ustamp,datestamp) VALUES(@id,'robot$smoke',1,@hash,@prefix,GETUTCDATE(),GETUTCDATE()); INSERT dbo.ta_CtnRoot(cCtnRootId,cCtnRootName,cCtnRootState,ustamp,datestamp) VALUES(@root,'smoke',1,GETUTCDATE(),GETUTCDATE()); INSERT dbo.ta_CtnRootRobot(cCtnRootId,cRobotId,cCtnRootRobotAccess,ustamp,datestamp) VALUES(@root,@id,'W',GETUTCDATE(),GETUTCDATE());",sql);command.Parameters.AddWithValue("@id",robot);command.Parameters.AddWithValue("@hash",RobotAuth.HashToken(token));command.Parameters.AddWithValue("@prefix",RobotAuth.DisplayPrefix(token));command.Parameters.AddWithValue("@root",rootId);await command.ExecuteNonQueryAsync();}
 var names=new[]{"dockerfile","local","template","base","module","compose-a","compose-b"};foreach(var name in names)await Sql($"INSERT dbo.ta_CtnImage(cCtnImageId,cCtnRootId,cCtnImageName,cCtnImageState,ustamp,datestamp) VALUES('{Ulid.NewUlid()}','{rootId}','{name}',1,GETUTCDATE(),GETUTCDATE())",cs.ConnectionString);
 var engine=Em.Api.Core.EmApp.BuildApp(["--contentRoot",root],b=> {b.SetDbProvider(cs.ConnectionString);b.AddContainerRegistry("./data/registry");});
 web=(WebApplication)typeof(Em.Api.Core.EmApp).GetField("_webApplication",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(engine)!;web.Urls.Clear();web.Urls.Add("http://127.0.0.1:0");typeof(Em.Api.Core.EmApp).GetMethod("MapContainerRegistry",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(engine,[web]);await web.StartAsync();
 var host=new Uri(web.Urls.Single()).Authority;
 var gui=(GuiApp)Activator.CreateInstance(typeof(GuiApp),BindingFlags.Instance|BindingFlags.NonPublic,null,[Array.Empty<string>()],null)!;
 var proxy=DispatchProxy.Create<ICtnServices,RegistryProxy>();var fake=(RegistryProxy)(object)proxy;fake.RootId=rootId;fake.Names=names;
 // Standalone fake GUI provider for the fixture.
#pragma warning disable ASP0000
 using var services=new ServiceCollection().AddSingleton(proxy).BuildServiceProvider();typeof(GuiApp).GetField("_serviceProvider",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(gui,services);gui.ActiveConnection=new Em.Ui.Core.Shared.ApiConnection {Host=web.Urls.Single(),ProfileName="Fixture",Timeout=30};
#pragma warning restore ASP0000
 var settings=new PublisherSettings {Profiles=Path.Combine(root,"Profiles"),Logs=Path.Combine(root,"Logs"),Work=Path.Combine(root,"Work")};var store=new ProfileStore(settings.Profiles);var secrets=new PublishSecretStore();var targets=new PublishTargets(gui,secrets);
 PublishProfile Profile(string name,ContainerMode mode) {var p=PublishProfile.Create(PublishKind.Container);p.Name=name;p.Workspace=root;p.Container!.Mode=mode;p.Container.Target=new() {Type=TargetType.BuiltIn,Server=targets.Scope,Root="smoke",Container=name,VersionTag="1.0.0",ExtraTags=["latest"]};p.Credentials.Add(new() {Username="robot$smoke",ScopeHost=host,Secret=token});secrets.ConvertMode(p,SensitiveDataStorage.Separate);return p;}
 async Task<Publisher> Publish(PublishProfile p) {var publisher=new Publisher(settings,store,secrets,targets);await publisher.Prepare(p,default);await publisher.Push(p,"Generic fixture release",default);Check(p.Name+" push",publisher.Prepared!.Artifacts.All(a=>a.Result==PublishResult.Success));await publisher.Verify(p,default);Check(p.Name+" verify digest",publisher.Prepared.Artifacts.All(a=>a.Verification=="Manifest digest matched"));return publisher;}
 File.WriteAllText(Path.Combine(root,"fixture.txt"),"fixture");File.WriteAllText(Path.Combine(root,"Dockerfile"),"FROM scratch\nCOPY fixture.txt /fixture.txt\n");
 var dockerfile=Profile("dockerfile",ContainerMode.Dockerfile);var built=await Publish(dockerfile);
 var local=Profile("local",ContainerMode.LocalImage);local.Container!.LocalImage=built.Prepared!.Artifacts[0].ImageId;await Publish(local);
 var project=Path.Combine(root,"Fixture.csproj");File.WriteAllText(project,"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup><ItemGroup><None Update=\"module.txt\" CopyToPublishDirectory=\"Always\"/></ItemGroup></Project>");File.WriteAllText(Path.Combine(root,"Program.cs"),"System.Console.WriteLine(\"Fixture\");");File.WriteAllText(Path.Combine(root,"module.txt"),"module");
 var template=Profile("template",ContainerMode.Template);template.Container!.Template=new() {Project="Fixture.csproj",BaseImage="scratch",Runtime="",Entrypoint=["./Fixture"]};await Publish(template);
 File.WriteAllText(Path.Combine(root,"compose.yml"),"name: em-publish-fixture\nservices:\n  a:\n    image: em-publish-fixture-a:local\n    build: .\n  b:\n    image: em-publish-fixture-b:local\n    build: .\n");
 var compose=Profile("compose-a",ContainerMode.Compose);compose.Container!.Compose.Services=[new() {Service="a",Repository="smoke/compose-a",VersionTag="1.0.0"},new() {Service="b",Repository="smoke/compose-b",VersionTag="1.0.0"}];await Publish(compose);
 File.WriteAllText(Path.Combine(root,"Base.Dockerfile"),"FROM scratch\nCOPY ./.file-base /app/\n");File.WriteAllText(Path.Combine(root,"Module.Dockerfile"),"ARG BASE_IMAGE\nFROM ${BASE_IMAGE}\nCOPY ./.file-module /app/\n");
 var baseProfile=Profile("base",ContainerMode.Template);baseProfile.Container!.Template=new() {Project="Fixture.csproj",Runtime="",DockerfileSource=DockerfileSource.ExistingFile,ExistingDockerfile="Base.Dockerfile",StagingSubfolder=".file-base",FileSet=new() {NamedList="Module files",ListMode=FileListMode.Exclude}};
 var module=Profile("module",ContainerMode.Template);module.RequireReleaseNotes=true;module.Container!.Template=new() {Project="Fixture.csproj",Runtime="",DockerfileSource=DockerfileSource.ExistingFile,ExistingDockerfile="Module.Dockerfile",StagingSubfolder=".file-module",UseSetBase=true,BaseProfileId=baseProfile.Id,FileSet=new() {NamedList="Module files",ListMode=FileListMode.IncludeOnly}};
 store.Save(baseProfile);store.Save(module);var set=Profile("set",ContainerMode.Set);set.Container!.Set=new() {Steps=[new() {ProfileId=baseProfile.Id},new() {ProfileId=module.Id}],FileLists=[new() {Name="Module files",Entries=["module.txt"]}]};var setPublisher=await Publish(set);
 Check("one shared publish",setPublisher.Prepared!.PublishOutputs.Count==1);
 module.Container.Template.BaseSelection=BaseSelection.LastPublished;module.Container.Template.FileSet.NamedList="";module.Container.Template.FileSet.Include=["module.txt"];module.Container.Target.VersionTag="1.0.1";await Publish(module);
 try {setPublisher.ValidateReleaseNotes(set," ");throw new Exception("Empty required Set notes accepted");}catch(InvalidDataException){Check("Set child requires release notes before operations",true);}
 Check("Docker user config unchanged",(File.Exists(dockerConfig)?PublishPaths.Hash(dockerConfig):null)==hash);
 Console.WriteLine($"PASS {checks} Docker publisher checks; fixture logs retained at {root}");
}finally {if(web!=null){await web.StopAsync();await web.DisposeAsync();}if(created&&Regex.IsMatch(database,"^EmPublishSmoke_[0-9a-f]{32}$"))await Sql($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]",master.ConnectionString);}

public class RegistryProxy:DispatchProxy {
 public string RootId="";public string[] Names=[];
 protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.Name switch {
  "GetMeta_CtnStatus"=>Task.FromResult(new StorageFeatureStatus(false,true,true,false)),
  "GetMeta_CtnRoots"=>Task.FromResult(new[]{new CtnRootInfo {Id=RootId,Name="smoke",IsActive=true}}),
  "GetMeta_CtnTree"=>Task.FromResult(new CtnTree {Images=Names.Select(n=>new CtnImageInfo {Name=n,IsActive=true}).ToArray()}),
  _=>throw new NotSupportedException(method.Name)
 };
}
