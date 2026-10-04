using System.IO;
using System.Text.Json;
using Em.Ui.Wpf.Publish;

var repo=Path.GetFullPath(args.FirstOrDefault()??Environment.CurrentDirectory);
var root=Path.GetFullPath(Path.Combine(repo,"../.artefacts/em-system/publish-smoke",Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);var count=0;
void Check(string name,bool condition) {if(!condition)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
void Reject(string name,Action action) {try {action();}catch(Exception ex)when(ex is IOException or InvalidDataException or ArgumentException) {Check(name,true);return;}throw new Exception("FAIL accepted "+name);}
var store=new ProfileStore(Path.Combine(root,"Profiles"));var secrets=new PublishSecretStore();
var p=PublishProfile.Create(PublishKind.NuGet);p.Name="Smoke";p.Workspace=repo;p.NuGet!.Sources.Add(new() {Path="src/backend/Em.Api.slnx"});
var entry=store.Save(p);Check("profile roundtrip",store.List(PublishKind.NuGet).Single().Profile!.Id==p.Id);
var corrupt=Path.Combine(store.Root,"NuGet","bad.json");File.WriteAllText(corrupt,"{");Check("corrupt item does not break listing",store.List(PublishKind.NuGet).Any(e=>e.Error!=null));
File.AppendAllText(entry.File," ");Reject("external change detected",()=>store.Save(p,entry));entry=store.Save(p,entry,true);
var credential=new PublishCredential {ScopeHost="packages.example",Username="robot$smoke",Secret="fixture-secret-only"};p.Credentials.Add(credential);
secrets.ConvertMode(p,SensitiveDataStorage.Separate);Check("separate removes inline",credential.Secret==null&&credential.SecretRef==credential.Id&&secrets.Get(credential,"packages.example")=="fixture-secret-only");
Check("host binding",secrets.Get(credential,"other.example")==null);secrets.ConvertMode(p,SensitiveDataStorage.Plaintext);Check("plaintext retrieves session value",credential.Secret=="fixture-secret-only");
var export=Path.Combine(root,"export.json");store.Export(p,export);Check("export default redacts",!File.ReadAllText(export).Contains("fixture-secret-only")&&ProfileJson.Read(File.ReadAllText(export)).Workspace==".");
store.Export(p,export,true);Check("export explicit inline only",File.ReadAllText(export).Contains("fixture-secret-only"));
secrets.ConvertMode(p,SensitiveDataStorage.Separate);store.Export(p,export,true);Check("separate never exports secret",!File.ReadAllText(export).Contains("fixture-secret-only"));
var clone=store.Duplicate(p);Check("duplicate independent ID and secret reference",clone.Profile!.Id!=p.Id&&clone.Profile.Credentials[0].SecretRef==null);
Reject("unknown sensitive mode rejected",()=>ProfileJson.Read(ProfileJson.Write(p).Replace("Separate","Encrypted")));
credential.Remember=true;secrets.Put(credential,"remembered-fixture",true);var fresh=new PublishSecretStore();Check("DPAPI remember",fresh.Get(credential,"packages.example")=="remembered-fixture");secrets.Put(credential,"session-fixture",false);Check("without remember does not persist",fresh.Get(credential,"packages.example")==null);
var masker=new SecretMasker();masker.Add("session-fixture");var run=new PublishRun {ProfileId=p.Id,ProfileName=p.Name};var log=new PublishLog(Path.Combine(root,"Logs"),run,masker);log.Line("password session-fixture");Check("log masking",!File.ReadAllText(Path.Combine(log.Directory,"output.log")).Contains("session-fixture"));Check("unfinished run interrupted",PublishLog.History(Path.Combine(root,"Logs")).Single().Run.Result==PublishResult.Interrupted);
Reject("active run cannot be deleted",()=>PublishLog.Delete(Path.Combine(root,"Logs"),new(log.Directory,run)));
log.Finish(PublishResult.Success);Check("completed run",PublishLog.History(Path.Combine(root,"Logs")).Single().Run.Result==PublishResult.Success);
Reject("workspace traversal rejected",()=>PublishPaths.Inside(root,"../escape"));
var workRoot=Path.Combine(root,"Work");string work;
using(var ws=new RunWorkspace(workRoot,Guid.NewGuid().ToString("N"),false)) {work=ws.Directory;File.WriteAllText(ws.PathFor("artifact.txt"),"owned");}Check("owned run workspace cleaned",!Directory.Exists(work));
var source=Path.Combine(root,"files");Directory.CreateDirectory(Path.Combine(source,"modules"));File.WriteAllText(Path.Combine(source,"base.dll"),"base");File.WriteAllText(Path.Combine(source,"modules","module.dll"),"module");File.WriteAllText(Path.Combine(source,"base.pdb"),"debug");
var lists=new[]{new NamedFileList {Name="Module files",Entries=["modules"]}};
var baseFiles=TemplateBuilder.SelectFiles(source,new() {NamedList="Module files",ListMode=FileListMode.Exclude},lists);
var moduleFiles=TemplateBuilder.SelectFiles(source,new() {NamedList="Module files",ListMode=FileListMode.IncludeOnly},lists);
Check("shared complement file sets",baseFiles.SequenceEqual(["base.dll"])&&moduleFiles.SequenceEqual(["modules/module.dll"]));
Reject("missing list entry defaults error",()=>TemplateBuilder.SelectFiles(source,new() {NamedList="Missing"},[new() {Name="Missing",Entries=["missing.dll"]}]));
Reject("required excluded file rejected",()=>TemplateBuilder.SelectFiles(source,new() {Exclude=["base.dll"],RequiredFiles=["base.dll"]},[]));
var dockerfile=TemplateBuilder.Dockerfile(new() {UseSetBase=true,Entrypoint=["./app"],StagingSubfolder=".file-module",MarkEntrypointExecutable=true,TimeZone="Asia/Jakarta",RunBeforeCopy=["apt-get update"]});Check("generated immutable-base template",dockerfile.Contains("ARG BASE_IMAGE")&&dockerfile.Contains(".file-module")&&dockerfile.Contains("chmod +x")&&dockerfile.Contains("ENV TZ=Asia/Jakarta"));
var reader=new ProjectReader(new PublishProcessRunner());var projects=await reader.Projects(Path.Combine(repo,"src/backend/Em.Api.slnx"));Check("read repository slnx",projects.Length>1);var info=await reader.Read(Path.Combine(repo,"src/shared/Em.Libs/Em.Libs.csproj"));Check("MSBuild evaluated library",info.IsPackable&&info.PackageId=="Em.Libs"&&!info.IsHost);
var host=await reader.Read(Path.Combine(repo,"src/backend/Em.Api/Em.Api.csproj"));Check("MSBuild evaluated host",!host.IsPackable&&host.IsHost);
var desktop=await reader.Read(Path.Combine(repo,"src/frontend/Em.Ui.Wpf/Em.Ui.Wpf.csproj"));Check("desktop excluded as Linux host",!desktop.IsHost);
var settings=new PublisherSettings {Profiles=Path.Combine(root,"Profiles"),Logs=Path.Combine(root,"Logs"),Work=Path.Combine(root,"Work")};var publisher=new Publisher(settings,store,secrets,new(null,secrets));
var before=Publisher.Fingerprint(p);p.NuGet.Target.ServiceIndex="https://packages.example/v3/index.json";Check("target change does not stale build",Publisher.Fingerprint(p)==before);p.NuGet.Configuration="Debug";Check("build change stale",Publisher.Fingerprint(p)!=before);
try {await publisher.Push(p,"",default);throw new Exception("accepted empty prepare");}catch(InvalidOperationException){Check("push requires prepare",true);}
p.RequireReleaseNotes=true;Reject("release notes required before operations",()=>publisher.ValidateReleaseNotes(p," "));
Reject("Unicode Docker tag rejected",()=>PublishTargets.ValidateTag("versi-\u00e9"));
using(var cancel=new CancellationTokenSource(300)) {
 try {await new PublishProcessRunner().RunAsync("powershell",["-NoProfile","-NonInteractive","-Command","Start-Sleep -Seconds 30"],repo,ct:cancel.Token);throw new Exception("cancel ignored");}
 catch(OperationCanceledException) {Check("cancel stops owned process",true);}
}
var dockerConfig=Path.Combine(root,"docker-user-config");Directory.CreateDirectory(dockerConfig);var dockerConfigFile=Path.Combine(dockerConfig,"config.json");
File.WriteAllText(dockerConfigFile,JsonSerializer.Serialize(new {auths=new Dictionary<string,object> { ["https://registry.example"]=new {auth=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("robot$fixture:fixture-only:token"))}}}));
var dockerConfigHash=PublishPaths.Hash(dockerConfigFile);var dockerCredential=await PublishTargets.DockerCredential("registry.example",default,dockerConfig);
Check("Docker login read-only and exact password",dockerCredential?.Credential.Username=="robot$fixture"&&dockerCredential?.Secret=="fixture-only:token"&&PublishPaths.Hash(dockerConfigFile)==dockerConfigHash);
Check("Docker login cannot cross host",await PublishTargets.DockerCredential("other.example",default,dockerConfig)==null);
Console.WriteLine($"PASS {count} publisher checks. Artifacts: {root}");
