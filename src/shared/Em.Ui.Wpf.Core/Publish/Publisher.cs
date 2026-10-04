using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using NuGet.Packaging;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Em.Ui.Wpf.Publish;

public sealed class PreparedPublish {
 public required PublishProfile Profile { get; init; }
 public required string BuildFingerprint { get; init; }
 public required string Connection { get; init; }
 public List<PublishArtifact> Artifacts { get; init; }=[];
 public string RunId { get; init; }="";
 public Dictionary<string,string> PublishOutputs { get; init; }=[];
 public Dictionary<string,PublishProfile> SetProfiles { get; init; }=[];
}
public sealed class Publisher(PublisherSettings settings,ProfileStore profiles,PublishSecretStore secrets,PublishTargets targets) {
 private static readonly ConcurrentDictionary<string,byte> Busy=new();
 private readonly PublishProcessRunner _process=new();
 private PublishTargets? _operationTargets;
 private PublishTargets Targets=>_operationTargets??targets;
 private Dictionary<string,string> _effective=[];
 private string? _dockerConfig;
 private void DockerEnvironment(bool useMyLogin) {
  _process.DefaultEnvironment=new() {{"DOCKER_BUILDKIT","1"}};
  if(!useMyLogin&&_dockerConfig!=null)_process.DefaultEnvironment["DOCKER_CONFIG"]=_dockerConfig;
 }
 private async Task<string> ContainerTarget(PublishProfile p,CancellationToken ct) {
  if(_effective.TryGetValue(p.Id,out var cached))return cached;
  var result=await Targets.ResolveContainer(p,ct);_effective[p.Id]=result;return result;
 }
 public PreparedPublish? Prepared { get; private set; }
 public event Action<string>? Output;
 public static string Fingerprint(PublishProfile profile) {
  var p=profile.Clone();p.Credentials=[];p.SensitiveDataStorage=SensitiveDataStorage.Separate;p.Name="";p.Description="";p.KeepWorkspace=false;p.RequireReleaseNotes=false;
  if(p.NuGet!=null)p.NuGet.Target=new();if(p.Container!=null) {p.Container.Target=new();p.Container.UseMyDockerLogin=false;foreach(var service in p.Container.Compose.Services){service.Repository="";service.VersionTag="";}}
  return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ProfileJson.Write(p))));
 }
 public async Task<string[]> Check(PublishProfile profile,CancellationToken ct) {
  profile.Validate();if(!Directory.Exists(profile.Workspace))throw new DirectoryNotFoundException("Select an existing workspace.");
  var tools=await _process.CheckTools(profile,ct);
  if(tools.Any(x=>x.StartsWith("Install ",StringComparison.Ordinal)||x.StartsWith("Start ",StringComparison.Ordinal)))throw new InvalidOperationException(string.Join("\n",tools));
  var target=profile.Kind==PublishKind.NuGet?await Targets.ResolveNuGet(profile,ct):profile.Container!.Mode==ContainerMode.Set?"Set targets checked below":await targets.ResolveContainer(profile,ct);
  if(profile.Container?.Mode!=ContainerMode.Set) {
   if(profile.Kind==PublishKind.NuGet)await Targets.NuGetRepository(target,Targets.Credential(profile,target),ct);
   else if(!profile.Container!.UseMyDockerLogin)Targets.Credential(profile,target.Split('/')[0]);
  }
  if(profile.NuGet!=null)foreach(var source in profile.NuGet.Sources)if(!File.Exists(profile.Resolve(source.Path)))throw new FileNotFoundException("Source missing: "+source.Path);
  if(profile.Container?.Mode==ContainerMode.Set) {
   var visited=new HashSet<string>{profile.Id};foreach(var step in profile.Container.Set.Steps) {if(!visited.Add(step.ProfileId))throw new InvalidDataException("Set contains a repeated/cyclic profile.");var child=profiles.Load(step.ProfileId);if(child.Container?.Mode==ContainerMode.Set)throw new InvalidDataException("Nested Sets are not supported.");await Check(child,ct);}
  }
  return [..tools,"Target: "+target];
 }
 private async Task<PublishRun> Execute(PublishProfile p,string operation,string notes,Func<PublishLog,RunWorkspace,CancellationToken,Task> action,CancellationToken ct,string? retry=null) {
  if(!Busy.TryAdd(p.Id,0))throw new InvalidOperationException("An operation is already active for this profile.");
  PublishLog? log=null;RunWorkspace? work=null;string? auth=null;var reserved=new List<string>();
  try {
   _operationTargets=targets.Freeze();_effective=[];
   var mask=new SecretMasker();foreach(var c in p.Credentials)mask.AddCredential(c.Username,secrets.Get(c,c.ScopeHost));
   var snapshot=p.Clone();snapshot.Credentials=[];
   var run=new PublishRun {ProfileId=p.Id,ProfileName=p.Name,Kind=p.Kind,Operation=operation,ReleaseNotes=notes,RetryOf=retry,Settings=JsonSerializer.SerializeToElement(snapshot,ProfileJson.Options)};
   log=new(settings.Logs,run,mask);log.Output+=s=>Output?.Invoke(s);
   work=new(settings.Work,run.Id,p.KeepWorkspace);
   if(p.Container is {} container) {
    if(container.Mode==ContainerMode.Set&&container.Set.Steps.Count==0)throw new InvalidDataException("Set requires at least one step.");
    var children=container.Mode==ContainerMode.Set?container.Set.Steps.Select(s=>Prepared?.SetProfiles.GetValueOrDefault(s.ProfileId)??profiles.Load(s.ProfileId)).ToArray():[p];
    if(container.Mode==ContainerMode.Set)foreach(var child in children) {
     if(!Busy.TryAdd(child.Id,0))throw new InvalidOperationException("A Set step is repeated or is already active: "+child.Name);reserved.Add(child.Id);
    }
    foreach(var child in children) {
     if(child.Container?.Mode==ContainerMode.Set)throw new InvalidDataException("Nested Sets are not supported.");
     if(operation=="Push"&&child.RequireReleaseNotes&&string.IsNullOrWhiteSpace(notes))throw new InvalidDataException("Release notes required by "+child.Name);
     if(operation is "Prepare" or "Push")await ContainerTarget(child,ct);
     if(child.Container?.Mode==ContainerMode.Compose)foreach(var mapping in child.Container.Compose.Services)await Targets.ValidateComposeTarget(child,mapping);
    }
   }
   if(p.Container!=null) {
    auth=work.PathFor("docker-config");Directory.CreateDirectory(auth);_dockerConfig=auth;DockerEnvironment(p.Container.UseMyDockerLogin);
   }
   await action(log,work,ct);
   log.Finish(run.Artifacts.Any(a=>a.Result is PublishResult.Failed or PublishResult.Cancelled)||run.Stages.Any(s=>s.Result==PublishResult.Failed)?PublishResult.Failed:PublishResult.Success);
   return run;
  } catch(OperationCanceledException) {
   if(log!=null) {foreach(var a in log.Run.Artifacts.Where(a=>a.Result==PublishResult.Running))a.Result=PublishResult.Cancelled;log.Stage(operation,PublishResult.Cancelled);log.Finish(PublishResult.Cancelled);}throw;
  } catch(Exception ex) {if(log!=null) {log.Line(ex.Message);log.Stage(operation,PublishResult.Failed,ex.Message);log.Finish(PublishResult.Failed);}throw;}
  finally {try {if(auth!=null&&Directory.Exists(auth)) {PublishPaths.ValidateTree(auth);Directory.Delete(auth,true);}_process.DefaultEnvironment=[];work?.Dispose();}finally {_dockerConfig=null;_operationTargets=null;_effective=[];foreach(var id in reserved)Busy.TryRemove(id,out _);Busy.TryRemove(p.Id,out _);}}
 }
 public async Task Prepare(PublishProfile profile,CancellationToken ct) {
  var p=profile.Clone();Prepared=null;
  await Execute(p,"Prepare","",async(log,work,token)=> {
   var prepared=new PreparedPublish {Profile=p,BuildFingerprint=Fingerprint(p),Connection=targets.Scope,RunId=log.Run.Id};
   if(p.Kind==PublishKind.NuGet)await Pack(p,prepared,log,work,token);
   else if(p.Container!.Mode==ContainerMode.Set) {
    foreach(var step in p.Container.Set.Steps) {
     var child=profiles.Load(step.ProfileId).Clone();prepared.SetProfiles[child.Id]=child;Mask(child,log);
     if(!step.Build) {
      if(child.Container!.Mode==ContainerMode.LocalImage)await Build(child,prepared,log,work,[],null,token);
      else {
       var previous=PublishLog.History(settings.Logs).SelectMany(h=>h.Run.Artifacts).FirstOrDefault(a=>a.ProfileId==child.Id&&a.Result==PublishResult.Success&&a.ImageId.Length>0)??throw new InvalidDataException("No existing prepared/published image for build-disabled Set step: "+child.Name);
       var reused=JsonSerializer.Deserialize<PublishArtifact>(ProfileJson.Write(previous),ProfileJson.Options)!;reused.Result=PublishResult.NotRun;reused.Selected=true;prepared.Artifacts.Add(reused);log.Stage(child.Name,PublishResult.Skipped,"Reused image "+reused.ImageId);
      }continue;
     }
     if(child.Container!.Template.UseSetBase&&child.Container.Template.BaseSelection==BaseSelection.FromThisRun) {log.Stage(child.Name,PublishResult.NotRun,"App build deferred until Base push supplies its immutable digest.");continue;}
     await Build(child,prepared,log,work,p.Container.Set.FileLists,null,token,p.Container.Set.SharePublishOutput);
    }
   } else await Build(p,prepared,log,work,[],null,token);
   log.Run.Artifacts=prepared.Artifacts;log.Save();Prepared=prepared;
  },ct);
 }
 private async Task Pack(PublishProfile p,PreparedPublish prepared,PublishLog log,RunWorkspace work,CancellationToken ct) {
  var n=p.NuGet!;if(n.Sources.Count==0)throw new InvalidDataException("Add a project or solution source.");var reader=new ProjectReader(_process);var ordinal=0;
  foreach(var source in n.Sources) {
   var projects=await reader.Projects(p.Resolve(source.Path),ct);var selected=source.Projects.Count>0||!source.SelectAllPackable?source.Projects.Select(p.Resolve).ToArray():projects;
   foreach(var project in selected) {
    if(!projects.Contains(project,StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("Selected project is outside the source solution.");
    var info=await reader.Read(project,n.Configuration,ct:ct);if(!info.IsPackable) {log.Stage(project,PublishResult.Skipped,info.Reason);continue;}
    var output=work.PathFor("pack-"+ordinal++);Directory.CreateDirectory(output);
    var args=new List<string> {"pack",project,"--configuration",n.Configuration,"--output",output};
    if(n.VersionOverride.Length>0)args.Add("-p:PackageVersion="+n.VersionOverride);
    foreach(var prop in n.MsbuildProperties) {
     if(prop.Key.Equals("PackageOutputPath",StringComparison.OrdinalIgnoreCase)||prop.Key.Equals("OutputPath",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Output properties are controlled by the run workspace.");
     args.Add("-p:"+prop.Key+"="+prop.Value);
    }
    await _process.RequireAsync("dotnet",args,p.Workspace,log,ct);
    foreach(var file in Directory.EnumerateFiles(output,"*.nupkg")) {
     var folder=PublishPaths.Inside(log.Directory,"packages/"+ordinal);Directory.CreateDirectory(folder);var copy=Path.Combine(folder,Path.GetFileName(file));File.Copy(file,copy);
     prepared.Artifacts.Add(ReadPackage(copy,p.Id));
    }
   }
  }
  if(prepared.Artifacts.Count==0)throw new InvalidDataException("Pack produced no .nupkg artifacts.");
 }
 public static PublishArtifact ReadPackage(string file,string profileId) {
  using var reader=new PackageArchiveReader(file);var identity=reader.GetIdentity();
  using var stream=File.OpenRead(file);return new() {ProfileId=profileId,File=file,Id=identity.Id,Version=identity.Version.ToNormalizedString(),Hash=Convert.ToBase64String(SHA512.HashData(stream))};
 }
 public void AddPackages(PublishProfile p,IEnumerable<string> files) {
  if(p.Kind!=PublishKind.NuGet)throw new InvalidOperationException("Existing packages belong to NuGet profiles.");
  Prepared??=new() {Profile=p.Clone(),BuildFingerprint=Fingerprint(p),Connection=targets.Scope};
  foreach(var file in files) {if(!file.EndsWith(".nupkg",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Only .nupkg packages are supported.");Prepared.Artifacts.Add(ReadPackage(file,p.Id));}
 }
 private void Mask(PublishProfile p,PublishLog log) {foreach(var credential in p.Credentials)log.Masker.AddCredential(credential.Username,secrets.Get(credential,credential.ScopeHost));}
 private async Task<string> PublishOutput(PublishProfile p,PreparedPublish prepared,PublishLog log,RunWorkspace work,CancellationToken ct,bool share) {
  var t=p.Container!.Template;
  var information=await new ProjectReader(_process).Read(p.Resolve(t.Project),t.Configuration,t.Framework,ct);
  if(!information.IsHost)throw new InvalidDataException("Select an executable/Web project for Template publish. "+information.Reason);
  var key=p.Resolve(t.Project)+"|"+t.Configuration+"|"+t.PublishSource+"|"+t.PublishProfile+"|"+t.Runtime+"|"+t.Framework+"|"+t.SelfContained;
  if(share&&prepared.PublishOutputs.TryGetValue(key,out var cached))return cached;
  var folder=work.PathFor("publish-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  var args=new List<string> {"publish",p.Resolve(t.Project),"-c",t.Configuration};
  if(t.PublishSource==PublishSource.PublishProfile) {if(t.PublishProfile.Length==0)throw new InvalidDataException("Select a publish profile.");args.Add("-p:PublishProfile="+(File.Exists(p.Resolve(t.PublishProfile))?p.Resolve(t.PublishProfile):t.PublishProfile));log.Line("PublishDir from the publish profile is overridden by the run workspace.");}
  else {if(t.Runtime.Length>0)args.AddRange(["-r",t.Runtime]);if(t.Framework.Length>0)args.AddRange(["-f",t.Framework]);args.Add("--self-contained");args.Add(t.SelfContained?"true":"false");}
  args.Add("-p:PublishDir="+folder+Path.DirectorySeparatorChar);
  await _process.RequireAsync("dotnet",args,p.Workspace,log,ct);
  // Persist prepared output so a deferred App can reuse one publish after the Prepare workspace is removed.
  var stored=PublishPaths.Inside(log.Directory,"publish/"+Guid.NewGuid().ToString("N"));TemplateBuilder.Stage(folder,stored,new() {ExcludeDebugSymbols=false},[]);
  prepared.PublishOutputs[key]=stored;return stored;
 }
 private string ResolveBase(TemplateProfile t,string? current) {
  if(!t.UseSetBase)return t.BaseImage;
  if(t.BaseSelection==BaseSelection.ExplicitReference&&!string.IsNullOrWhiteSpace(t.ExplicitBaseReference))return t.ExplicitBaseReference;
  if(t.BaseSelection==BaseSelection.FromThisRun)return current??throw new InvalidDataException("Push the Base in this Set before building App.");
  var artifact=PublishLog.History(settings.Logs).Where(x=>x.Run.Result==PublishResult.Success).SelectMany(x=>x.Run.Artifacts).FirstOrDefault(x=>x.ProfileId==t.BaseProfileId&&x.Result==PublishResult.Success&&x.Digest.Length>0);
  if(artifact==null)throw new InvalidDataException("No successful Base publish log with a digest.");return Repository(artifact.Target)+"@"+artifact.Digest;
 }
 private static string Repository(string target)=>target[..target.LastIndexOf(':')];
 private async Task Build(PublishProfile p,PreparedPublish prepared,PublishLog log,RunWorkspace work,IReadOnlyList<NamedFileList> lists,string? currentBase,CancellationToken ct,bool share=true) {
  var c=p.Container!;DockerEnvironment(c.UseMyDockerLogin);var target=await ContainerTarget(p,ct);log.Run.Target=target;
  if(!c.UseMyDockerLogin&&_process.DefaultEnvironment.ContainsKey("DOCKER_CONFIG")) {
   var credential=p.Credentials.FirstOrDefault(x=>x.Purpose=="push"&&PublishSecretStore.Host(x.ScopeHost)==target.Split('/')[0]);
   if(credential!=null&&secrets.Get(credential,target.Split('/')[0]) is {Length:>0} secret) {
    log.Masker.Add(secret);await _process.RequireAsync("docker",["login",target.Split('/')[0],"--username",credential.Username,"--password-stdin"],p.Workspace,log,ct,stdin:secret+"\n");
   }
  }
  var image="em-publish:"+Guid.NewGuid().ToString("N");
  if(c.Mode==ContainerMode.LocalImage) {
   if(string.IsNullOrWhiteSpace(c.LocalImage))throw new InvalidDataException("Select a local image.");image=c.LocalImage;
  } else if(c.Mode==ContainerMode.Compose) {
   var prefix=new List<string> {"compose","-f",p.Resolve(c.Compose.File),"--project-directory",p.Resolve(c.Compose.ProjectDirectory)};
   var configResult=await _process.RunAsync("docker",[..prefix,"config","--format","json"],p.Workspace,ct:ct);
   log.Stage("Compose config",configResult.ExitCode==0?PublishResult.Success:PublishResult.Failed,"",configResult.ExitCode);
   if(configResult.ExitCode!=0)throw new IOException("Compose config failed. Check the Compose source and Tools.");
   var config=configResult.Output;
   using var json=JsonDocument.Parse(config);var services=json.RootElement.GetProperty("services");
   if(c.Compose.Services.Count==0)throw new InvalidDataException("Select Compose build services and targets.");
   foreach(var mapping in c.Compose.Services)if(!services.TryGetProperty(mapping.Service,out var svc)||!svc.TryGetProperty("build",out _))throw new InvalidDataException("Compose service has no build: "+mapping.Service);
   await _process.RequireAsync("docker",[..prefix,"build",..c.Compose.Services.Select(s=>s.Service)],p.Workspace,log,ct);
   foreach(var mapping in c.Compose.Services) {
    var service=services.GetProperty(mapping.Service);var local=service.TryGetProperty("image",out var named)?named.GetString()!:json.RootElement.GetProperty("name").GetString()+"-"+mapping.Service;
    PublishTargets.ValidateTag(mapping.VersionTag);var repo=mapping.Repository;if(string.IsNullOrWhiteSpace(repo))throw new InvalidDataException("Compose target repository is required.");
    prepared.Artifacts.Add(new() {ProfileId=p.Id,Source=mapping.Service,Id=repo,Version=mapping.VersionTag,ImageId=await ImageId(local,p,log,ct),Target=target.Split('/')[0]+"/"+repo+":"+mapping.VersionTag});
   }return;
  } else {
   var docker=c.Dockerfile;string context,file;
   if(c.Mode==ContainerMode.Template) {
    var t=c.Template;var output=await PublishOutput(p,prepared,log,work,ct,share);context=work.PathFor("staging-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(context);
    var staging=t.StagingSubfolder.Length==0?context:PublishPaths.Inside(context,t.StagingSubfolder);
    TemplateBuilder.Stage(output,staging,t.FileSet,lists,log.Line);
    file=Path.Combine(context,".em-publish.Dockerfile");
    if(t.DockerfileSource==DockerfileSource.ExistingFile)File.Copy(p.Resolve(t.ExistingDockerfile),file);else File.WriteAllText(file,TemplateBuilder.Dockerfile(t));
    var baseImage=ResolveBase(t,currentBase);if(t.UseSetBase) {log.Line("Effective Base: "+baseImage);log.Stage("Base reference",PublishResult.Success,baseImage);}
    docker=new() {Context=context,File=file,Target=docker.Target,Platform=docker.Platform,BuildArgs=[..docker.BuildArgs],NamedContexts=docker.NamedContexts,Secrets=docker.Secrets};
    if(t.UseSetBase)docker.BuildArgs.Add(new() {Key="BASE_IMAGE",Value=baseImage});
   } else {context=p.Resolve(docker.Context);file=p.Resolve(docker.File);}
   var args=new List<string>{"build","-t",image,"-f",file};
   if(docker.Target.Length>0)args.AddRange(["--target",docker.Target]);if(docker.Platform.Length>0)args.AddRange(["--platform",docker.Platform]);
   foreach(var arg in docker.BuildArgs)args.AddRange(["--build-arg",arg.Key+"="+arg.Value]);
   foreach(var named in docker.NamedContexts)args.AddRange(["--build-context",named.Key+"="+p.Resolve(named.Value)]);
   var env=new Dictionary<string,string>{{"DOCKER_BUILDKIT","1"}};
   foreach(var secret in docker.Secrets) {
    var credential=p.Credentials.SingleOrDefault(x=>x.Id==secret.CredentialRef)??throw new InvalidDataException("Build credential missing.");
    var value=secrets.Get(credential,credential.ScopeHost)??throw new InvalidDataException("Build secret is unavailable.");var name="EM_PUBLISH_SECRET_"+env.Count;env[name]=value;log.Masker.Add(value);args.AddRange(["--secret","id="+secret.Id+",env="+name]);
   }
   args.Add(context);await _process.RequireAsync("docker",args,p.Workspace,log,ct,env);
  }
  prepared.Artifacts.Add(new() {ProfileId=p.Id,Id=Repository(target),Version=c.Target.VersionTag,ImageId=await ImageId(image,p,log,ct),Target=target});
 }
 private async Task<string> ImageId(string image,PublishProfile p,PublishLog log,CancellationToken ct)=> (await _process.RequireAsync("docker",["image","inspect","--format","{{.Id}}",image],p.Workspace,log,ct)).Trim();
 public void ValidateReleaseNotes(PublishProfile p,string notes) {
  if(!string.IsNullOrWhiteSpace(notes))return;
  if(p.RequireReleaseNotes)throw new InvalidDataException("Release notes are required.");
  if(p.Container?.Mode==ContainerMode.Set)foreach(var step in p.Container.Set.Steps)if(profiles.Load(step.ProfileId).RequireReleaseNotes)throw new InvalidDataException("Release notes are required by a Set step.");
 }
 public async Task Push(PublishProfile profile,string notes,CancellationToken ct) {
  var prepared=Prepared??throw new InvalidOperationException("Prepare or add packages first.");
  if(Fingerprint(profile)!=prepared.BuildFingerprint)throw new InvalidOperationException("Source/build settings changed; Prepare ulang.");
  if(profile.Container?.Mode==ContainerMode.Set)foreach(var step in profile.Container.Set.Steps) {
   var current=profiles.Load(step.ProfileId).Clone();
   if(prepared.SetProfiles.TryGetValue(step.ProfileId,out var previous)&&Fingerprint(current)!=Fingerprint(previous))throw new InvalidOperationException("Set step source/build settings changed; Prepare ulang: "+current.Name);
   prepared.SetProfiles[step.ProfileId]=current;
  }
  ValidateReleaseNotes(profile,notes);
  var p=profile.Clone();
  await Execute(p,"Push",notes,async(log,work,token)=> {
   log.Run.Artifacts=prepared.Artifacts;
   if(p.Container?.Mode==ContainerMode.Set) {
    string? baseReference=null;bool failed=false;
    foreach(var step in p.Container.Set.Steps) {
     var child=prepared.SetProfiles.GetValueOrDefault(step.ProfileId)??profiles.Load(step.ProfileId).Clone();Mask(child,log);
     if(child.RequireReleaseNotes&&string.IsNullOrWhiteSpace(notes))throw new InvalidDataException("Release notes required by "+child.Name);
     if(failed) {log.Stage(child.Name,PublishResult.NotRun,"Previous Set step failed.");continue;}
     try {
      if(step.Build&&!prepared.Artifacts.Any(a=>a.ProfileId==child.Id))await Build(child,prepared,log,work,p.Container.Set.FileLists,baseReference,token,p.Container.Set.SharePublishOutput);
      if(step.Push)foreach(var artifact in prepared.Artifacts.Where(a=>a.ProfileId==child.Id&&a.Selected&&a.Result!=PublishResult.Success))await PushContainer(child,artifact,log,work,token);
      failed=prepared.Artifacts.Any(a=>a.ProfileId==child.Id&&a.Result==PublishResult.Failed);
      var pushed=prepared.Artifacts.LastOrDefault(a=>a.ProfileId==child.Id&&a.Result==PublishResult.Success&&a.Digest.Length>0);
      if(pushed!=null)baseReference=Repository(pushed.Target)+"@"+pushed.Digest;
     }catch(OperationCanceledException){throw;}catch(Exception ex){failed=true;log.Stage(child.Name,PublishResult.Failed,ex.Message);}
    }
   } else if(p.Kind==PublishKind.NuGet) {
    var target=await Targets.ResolveNuGet(p,token);log.Run.Target=target;var credential=Targets.Credential(p,target);var repository=await Targets.NuGetRepository(target,credential,token);
    var update=await repository.GetResourceAsync<PackageUpdateResource>(token);var find=await repository.GetResourceAsync<FindPackageByIdResource>(token);
    foreach(var artifact in prepared.Artifacts.Where(a=>a.Selected&&a.Result!=PublishResult.Success)) {
     token.ThrowIfCancellationRequested();artifact.Target=target;artifact.Result=PublishResult.Running;log.Save();
     try {
      using var stream=File.OpenRead(artifact.File);if(Convert.ToBase64String(SHA512.HashData(stream))!=artifact.Hash)throw new IOException("Prepared package changed; Prepare again.");
      using var cache=new SourceCacheContext {NoCache=true};
      var duplicate=await find.DoesPackageExistAsync(artifact.Id,NuGetVersion.Parse(artifact.Version),cache,log,token);
      if(duplicate) {artifact.Result=p.NuGet!.DuplicateHandling==DuplicateHandling.Skip?PublishResult.Duplicate:PublishResult.Failed;artifact.Message="Version already exists.";}
      else {var pushFolder=work.PathFor("package-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(pushFolder);var pushFile=Path.Combine(pushFolder,Path.GetFileName(artifact.File));File.Copy(artifact.File,pushFile);token.ThrowIfCancellationRequested();await update.Push([pushFile],null,300,false,_=>credential.Secret,_=>null,false,false,null,new Uri(target).Scheme=="http",log);token.ThrowIfCancellationRequested();artifact.Result=PublishResult.Success;}
     }catch(OperationCanceledException){artifact.Result=PublishResult.Cancelled;throw;}
     catch(Exception ex) {artifact.Result=PublishResult.Failed;artifact.Message=log.Masker.Mask(ex.Message)+" A recycled NuPak version also conflicts; restore/purge it manually in NuGet Manager.";}
     log.Save();
    }
   } else foreach(var artifact in prepared.Artifacts.Where(a=>a.Selected&&a.Result!=PublishResult.Success))await PushContainer(p,artifact,log,work,token);
   log.Save();
  },ct,prepared.RunId);
 }
 private async Task PushContainer(PublishProfile p,PublishArtifact artifact,PublishLog log,RunWorkspace work,CancellationToken ct) {
  var effective=await ContainerTarget(p,ct);var c=p.Container!;DockerEnvironment(c.UseMyDockerLogin);
  if(c.Mode!=ContainerMode.Compose) {artifact.Target=effective;artifact.Version=c.Target.VersionTag;artifact.Id=Repository(effective);}
  else {
   var mapping=c.Compose.Services.SingleOrDefault(s=>s.Service==artifact.Source)??throw new InvalidDataException("Prepared Compose service mapping is missing.");
   PublishTargets.ValidateTag(mapping.VersionTag);
   artifact.Target=effective.Split('/')[0]+"/"+mapping.Repository+":"+mapping.VersionTag;artifact.Version=mapping.VersionTag;artifact.Id=mapping.Repository;
  }
  var host=artifact.Target.Split('/')[0];var env=new Dictionary<string,string>();
  var auth=work.PathFor("docker-auth-"+Guid.NewGuid().ToString("N"));
  artifact.Result=PublishResult.Running;log.Save();
  try {
   if(!c.UseMyDockerLogin) {
    Directory.CreateDirectory(auth);env["DOCKER_CONFIG"]=auth;var credential=Targets.Credential(p,host);log.Masker.Add(credential.Secret);
    await _process.RequireAsync("docker",["login",host,"--username",credential.Credential.Username,"--password-stdin"],p.Workspace,log,ct,env,credential.Secret+"\n");
   }
   await _process.RequireAsync("docker",["tag",artifact.ImageId,artifact.Target],p.Workspace,log,ct,env);
   await _process.RequireAsync("docker",["push",artifact.Target],p.Workspace,log,ct,env);
   var digests=await _process.RequireAsync("docker",["image","inspect","--format","{{json .RepoDigests}}",artifact.ImageId],p.Workspace,log,ct,env);
   var digest=JsonSerializer.Deserialize<string[]>(digests)?.FirstOrDefault(x=>x.StartsWith(Repository(artifact.Target)+"@",StringComparison.Ordinal));
   if(digest==null)throw new IOException("Push succeeded but matching repository digest could not be read.");artifact.Digest=digest[(digest.IndexOf('@')+1)..];
   artifact.Result=PublishResult.Success;log.Stage(artifact.Target,PublishResult.Success,artifact.Digest);
   foreach(var extra in c.Target.ExtraTags.Distinct().Where(t=>t!=artifact.Version)) {
    ct.ThrowIfCancellationRequested();var tag=Repository(artifact.Target)+":"+extra;
    try {await _process.RequireAsync("docker",["tag",artifact.ImageId,tag],p.Workspace,log,ct,env);await _process.RequireAsync("docker",["push",tag],p.Workspace,log,ct,env);log.Stage(tag,PublishResult.Success);}
    catch(OperationCanceledException){throw;}catch(Exception ex) {artifact.Result=PublishResult.Failed;artifact.Message="Version pushed; additional tag failed: "+extra;log.Stage(tag,PublishResult.Failed,ex.Message);break;}
   }
  }catch(OperationCanceledException){artifact.Result=PublishResult.Cancelled;throw;}
  catch(Exception ex){artifact.Result=PublishResult.Failed;artifact.Message=log.Masker.Mask(ex.Message);}
  finally {if(Directory.Exists(auth)) {PublishPaths.RejectLinks(auth);foreach(var file in Directory.EnumerateFiles(auth))File.Delete(file);Directory.Delete(auth);}log.Save();}
 }
 public async Task Verify(PublishProfile profile,CancellationToken ct) {
  var prepared=Prepared??throw new InvalidOperationException("No artifacts to verify.");var p=profile.Clone();
  await Execute(p,"Verify","",async(log,work,token)=> {
   log.Run.Artifacts=prepared.Artifacts;
   foreach(var a in prepared.Artifacts.Where(a=>a.Selected)) {
    token.ThrowIfCancellationRequested();
    try {
     if(p.Kind==PublishKind.NuGet) {
      var target=a.Target.Length>0?a.Target:await Targets.ResolveNuGet(p,token);var repo=await Targets.NuGetRepository(target,Targets.Credential(p,target),token);
      var find=await repo.GetResourceAsync<FindPackageByIdResource>(token);using var cache=new SourceCacheContext {NoCache=true};using var content=new MemoryStream();
      if(await find.CopyNupkgToStreamAsync(a.Id,NuGetVersion.Parse(a.Version),content,cache,log,token)) {
       content.Position=0;a.Verification=Convert.ToBase64String(SHA512.HashData(content))==a.Hash?"SHA-512 matched":"SHA-512 mismatch";
       if(a.Verification.EndsWith("mismatch"))throw new IOException(a.Verification);
      }else {a.Verification=await find.DoesPackageExistAsync(a.Id,NuGetVersion.Parse(a.Version),cache,log,token)?"Identity exists (hash unavailable)":"Not found";if(a.Verification=="Not found")throw new IOException(a.Verification);}
     } else {
      var child=p.Container!.Mode==ContainerMode.Set?prepared.SetProfiles[a.ProfileId]:p;
      var target=a.Target;var credential=await Targets.OciCredential(child,target.Split('/')[0],token);
      if(credential is {} auth)log.Masker.AddCredential(auth.Credential.Username,auth.Secret);
      var digest=await new OciClient(Targets).Request(target,"manifests/"+a.Version,HttpMethod.Head,credential,token);
      a.Verification=a.Digest.Length>0&&digest==a.Digest?"Manifest digest matched":"Manifest digest mismatch or local digest unavailable";
      if(a.Verification!="Manifest digest matched")throw new IOException(a.Verification);
     }log.Stage("Verify "+a.Id,PublishResult.Success,a.Verification);
    }catch(OperationCanceledException){throw;}catch(Exception ex) {a.Verification=log.Masker.Mask(ex.Message);log.Stage("Verify "+a.Id,PublishResult.Failed,ex.Message);}
    log.Save();
   }
  },ct,prepared.RunId);
 }
}
