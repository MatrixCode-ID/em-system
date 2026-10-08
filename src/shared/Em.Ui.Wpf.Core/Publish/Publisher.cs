using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using NuGet.Packaging;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Em.Ui.Wpf.Publish;

/// <summary>The prepared publish.</summary>
public sealed class PreparedPublish {
 /// <summary>The profile.</summary>
 public required PublishProfile Profile { get; init; }
 /// <summary>The build fingerprint.</summary>
 public required string BuildFingerprint { get; init; }
 /// <summary>The connection.</summary>
 public required string Connection { get; init; }
 /// <summary>The artifacts.</summary>
 public List<PublishArtifact> Artifacts { get; init; }=[];
 /// <summary>The run id.</summary>
 public string RunId { get; init; }="";
 /// <summary>The publish outputs.</summary>
 public Dictionary<string,string> PublishOutputs { get; init; }=[];
 /// <summary>The set profiles.</summary>
 public Dictionary<string,PublishProfile> SetProfiles { get; init; }=[];
}
/// <summary>Runs publish: checks the profile, prepares the artifacts, pushes them, and verifies the result.</summary>
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
 /// <summary>The prepared.</summary>
 public PreparedPublish? Prepared { get; private set; }
 /// <summary>The run of the last operation and its log folder, for Retry deploy after Push.</summary>
 public PublishRun? LastRun { get; private set; }
 /// <summary>The last run directory.</summary>
 public string? LastRunDirectory { get; private set; }
 // Artifacts pushed by the Push operation in progress; only these are deployed afterwards.
 private List<(PublishProfile Profile,PublishArtifact Artifact)> _pushedNow=[];
 /// <summary>Raised for output.</summary>
 public event Action<string>? Output;
 /// <summary>Computes a fingerprint of the parts of a profile that decide what is built.</summary>
 public static string Fingerprint(PublishProfile profile) {
  var p=profile.Clone();p.Credentials=[];p.SensitiveDataStorage=SensitiveDataStorage.Separate;p.Name="";p.Description="";p.KeepWorkspace=false;p.RequireReleaseNotes=false;
  if(p.NuGet!=null)p.NuGet.Target=new();if(p.Container!=null) {p.Container.Target=new();p.Container.UseMyDockerLogin=false;p.Container.AutoDeploy=true;foreach(var service in p.Container.Compose.Services){service.Repository="";service.VersionTag="";}}
  return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ProfileJson.Write(p))));
 }
 /// <summary>Checks a profile and returns the problems found.</summary>
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
  var warnings=profile.Container is {} container&&container.Mode!=ContainerMode.Set?await DockerRegistryWarnings(profile,target,ct):[];
  return [..tools,..warnings,"Target: "+target];
 }
 // Docker's daemon, not this application, decides http or https for a push, so Check reads the daemon's registry
 // settings and says up front what would make `docker login`/push fail (a plain-HTTP host that is not insecure, or a
 // loopback host that a VM-based Docker Desktop cannot reach). Warnings only: an unreadable `docker info` adds nothing.
 private async Task<string[]> DockerRegistryWarnings(PublishProfile profile,string target,CancellationToken ct) {
  var host=target.Split('/')[0];var bare=Uri.TryCreate("http://"+host,UriKind.Absolute,out var uri)?uri.Host.Trim('[',']'):host;
  var isAddress=IPAddress.TryParse(bare,out var address);
  var loopback=bare.Equals("localhost",StringComparison.OrdinalIgnoreCase)||isAddress&&IPAddress.IsLoopback(address!);
  var plainHttp=profile.Credentials.Any(c=>c.Purpose=="push"&&c.AllowHttp&&PublishSecretStore.Host(c.ScopeHost)==PublishSecretStore.Host(host))
   ||Targets.RegistryUrl(target).StartsWith("http://",StringComparison.OrdinalIgnoreCase);
  ProcessResult info;
  try {info=await _process.RunAsync("docker",["info","--format","{{.OperatingSystem}}|{{json .RegistryConfig}}"],profile.Workspace,ct:ct);}
  catch(Win32Exception) {return [];}
  var text=info.Output.Trim();var bar=text.IndexOf('|');if(info.ExitCode!=0||bar<0)return [];
  var insecure=loopback;
  try {
   using var config=JsonDocument.Parse(text[(bar+1)..]);var root=config.RootElement;
   if(root.TryGetProperty("IndexConfigs",out var indexes)&&indexes.TryGetProperty(host,out var entry)&&entry.TryGetProperty("Secure",out var secure)&&secure.ValueKind==JsonValueKind.False)insecure=true;
   if(!insecure&&isAddress&&root.TryGetProperty("InsecureRegistryCIDRs",out var cidrs))
    foreach(var cidr in cidrs.EnumerateArray())if(IPNetwork.TryParse(cidr.GetString()??"",out var network)&&network.Contains(address!))insecure=true;
  } catch(JsonException) {return [];}
  var warnings=new List<string>();
  if(plainHttp&&!insecure)warnings.Add($"Warning: this registry speaks plain HTTP but Docker contacts '{host}' over https first and refuses it. Add '{host}' to insecure-registries (More > Add registry to Docker insecure-registries, then restart Docker Desktop), or serve the registry over https.");
  if(loopback&&text[..bar].Contains("Docker Desktop",StringComparison.OrdinalIgnoreCase))warnings.Add($"Warning: '{host}' is this computer's loopback, but Docker Desktop's engine runs in a VM where localhost is the VM, not Windows, so login and push time out. Use this computer's LAN address (bind the server to 0.0.0.0), or run the registry as a container.");
  return [..warnings];
 }
 private async Task<PublishRun> Execute(PublishProfile p,string operation,string notes,Func<PublishLog,RunWorkspace,CancellationToken,Task> action,CancellationToken ct,string? retry=null) {
  if(!Busy.TryAdd(p.Id,0))throw new InvalidOperationException("An operation is already active for this profile.");
  PublishLog? log=null;RunWorkspace? work=null;string? auth=null;var reserved=new List<string>();
  try {
   _operationTargets=targets.Freeze();_effective=[];
   var mask=new SecretMasker();foreach(var c in p.Credentials)mask.AddCredential(c.Username,secrets.Get(c,c.ScopeHost));
   var snapshot=p.Clone();snapshot.Credentials=[];
   var run=new PublishRun {ProfileId=p.Id,ProfileName=p.Name,Kind=p.Kind,Operation=operation,ReleaseNotes=notes,RetryOf=retry,Settings=JsonSerializer.SerializeToElement(snapshot,ProfileJson.Options)};
   log=new(settings.Logs,run,mask);log.Output+=s=>Output?.Invoke(s);LastRun=run;LastRunDirectory=log.Directory;
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
 /// <summary>Builds the artifacts of a profile.</summary>
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
 /// <summary>Reads a package file as a publish artifact.</summary>
 public static PublishArtifact ReadPackage(string file,string profileId) {
  using var reader=new PackageArchiveReader(file);var identity=reader.GetIdentity();
  using var stream=File.OpenRead(file);return new() {ProfileId=profileId,File=file,Id=identity.Id,Version=identity.Version.ToNormalizedString(),Hash=Convert.ToBase64String(SHA512.HashData(stream))};
 }
 /// <summary>Adds package files as artifacts of a profile.</summary>
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
  var hasVersion=Em.Shared.AppVersion.TryFromTag(p.Container.Target.VersionTag,out var version);
  var key=p.Resolve(t.Project)+"|"+t.Configuration+"|"+t.PublishSource+"|"+t.PublishProfile+"|"+t.Runtime+"|"+t.Framework+"|"+t.SelfContained+"|"+version;
  if(share&&prepared.PublishOutputs.TryGetValue(key,out var cached))return cached;
  var folder=work.PathFor("publish-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  var args=new List<string> {"publish",p.Resolve(t.Project),"-c",t.Configuration};
  if(t.PublishSource==PublishSource.PublishProfile) {if(t.PublishProfile.Length==0)throw new InvalidDataException("Select a publish profile.");args.Add("-p:PublishProfile="+(File.Exists(p.Resolve(t.PublishProfile))?p.Resolve(t.PublishProfile):t.PublishProfile));log.Line("PublishDir from the publish profile is overridden by the run workspace.");}
  else {if(t.Runtime.Length>0)args.AddRange(["-r",t.Runtime]);if(t.Framework.Length>0)args.AddRange(["-f",t.Framework]);args.Add("--self-contained");args.Add(t.SelfContained?"true":"false");}
  args.Add("-p:PublishDir="+folder+Path.DirectorySeparatorChar);
  // The version tag becomes the product version of the app; see doc/engine/build.md#product-version.
  if(hasVersion)args.Add("-p:Version="+version);
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
 /// <summary>The build argument that carries the version tag into a Dockerfile; see doc/engine/build.md#product-version.</summary>
 public const string AppVersionBuildArg="APP_VERSION";
 /// <summary>
 /// Adds <see cref="AppVersionBuildArg"/> from a version tag to the build arguments, unless the tag is not a
 /// version (such as <c>latest</c>) or the profile already sets the argument itself.
 /// </summary>
 public static List<BuildProperty> WithAppVersion(IEnumerable<BuildProperty> buildArgs,string versionTag) {
  var list=buildArgs.ToList();
  if(Em.Shared.AppVersion.TryFromTag(versionTag,out var version)&&!list.Any(a=>a.Key.Equals(AppVersionBuildArg,StringComparison.Ordinal)))list.Add(new() {Key=AppVersionBuildArg,Value=version});
  return list;
 }
 /// <summary>
 /// The <c>docker compose build</c> arguments after <c>build</c>, one call per version: services that share a
 /// version tag build together with <see cref="AppVersionBuildArg"/>; services whose tag is not a version
 /// build without it.
 /// </summary>
 public static List<List<string>> ComposeBuilds(IEnumerable<ComposeService> services)=>
  services.GroupBy(s=>Em.Shared.AppVersion.TryFromTag(s.VersionTag,out var v)?v:"")
   .Select(g=>(g.Key.Length>0?new List<string> {"--build-arg",AppVersionBuildArg+"="+g.Key}:[]).Concat(g.Select(s=>s.Service)).ToList()).ToList();
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
   foreach(var group in ComposeBuilds(c.Compose.Services))await _process.RequireAsync("docker",[..prefix,"build",..group],p.Workspace,log,ct);
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
   foreach(var arg in WithAppVersion(docker.BuildArgs,c.Mode==ContainerMode.Template?"":c.Target.VersionTag))args.AddRange(["--build-arg",arg.Key+"="+arg.Value]);
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
 /// <summary>Validates the release notes of a profile.</summary>
 public void ValidateReleaseNotes(PublishProfile p,string notes) {
  if(!string.IsNullOrWhiteSpace(notes))return;
  if(p.RequireReleaseNotes)throw new InvalidDataException("Release notes are required.");
  if(p.Container?.Mode==ContainerMode.Set)foreach(var step in p.Container.Set.Steps)if(profiles.Load(step.ProfileId).RequireReleaseNotes)throw new InvalidDataException("Release notes are required by a Set step.");
 }
 /// <summary>Pushes the prepared artifacts to the target.</summary>
 public async Task Push(PublishProfile profile,string notes,CancellationToken ct) {
  var prepared=Prepared??throw new InvalidOperationException("Prepare or add packages first.");
  if(Fingerprint(profile)!=prepared.BuildFingerprint)throw new InvalidOperationException("Source/build settings changed; Prepare again.");
  if(profile.Container?.Mode==ContainerMode.Set)foreach(var step in profile.Container.Set.Steps) {
   var current=profiles.Load(step.ProfileId).Clone();
   if(prepared.SetProfiles.TryGetValue(step.ProfileId,out var previous)&&Fingerprint(current)!=Fingerprint(previous))throw new InvalidOperationException("Set step source/build settings changed; Prepare again: "+current.Name);
   prepared.SetProfiles[step.ProfileId]=current;
  }
  ValidateReleaseNotes(profile,notes);
  var p=profile.Clone();
  await Execute(p,"Push",notes,async(log,work,token)=> {
   log.Run.Artifacts=prepared.Artifacts;_pushedNow=[];
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
   if(p.Container?.AutoDeploy==true)await DeployPushed(log);
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
  artifact.Result=PublishResult.Running;artifact.PushedTags=[];log.Save();
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
   artifact.Result=PublishResult.Success;artifact.PushedTags.Add(artifact.Version);_pushedNow.Add((p,artifact));log.Stage(artifact.Target,PublishResult.Success,artifact.Digest);
   // Floating tags follow the channel of the version tag (container-naming convention); a manual tag moves none.
   foreach(var extra in ContainerVersion.FloatingTagsOf(artifact.Version).Where(t=>t!=artifact.Version)) {
    ct.ThrowIfCancellationRequested();var tag=Repository(artifact.Target)+":"+extra;
    try {await _process.RequireAsync("docker",["tag",artifact.ImageId,tag],p.Workspace,log,ct,env);await _process.RequireAsync("docker",["push",tag],p.Workspace,log,ct,env);artifact.PushedTags.Add(extra);log.Stage(tag,PublishResult.Success);}
    catch(OperationCanceledException){throw;}catch(Exception ex) {artifact.Result=PublishResult.Failed;artifact.Message="Version pushed; floating tag failed: "+extra;log.Stage(tag,PublishResult.Failed,ex.Message);break;}
   }
  }catch(OperationCanceledException){artifact.Result=PublishResult.Cancelled;throw;}
  catch(Exception ex){artifact.Result=PublishResult.Failed;artifact.Message=log.Masker.Mask(ex.Message);}
  finally {if(Directory.Exists(auth)) {PublishPaths.RejectLinks(auth);foreach(var file in Directory.EnumerateFiles(auth))File.Delete(file);Directory.Delete(auth);}log.Save();}
 }
 // Auto deploy (Container Manager deploy target): one server call per image pushed to the Built-in registry in this run. The server
 // skips containers without an active target or whose tag filter does not match. Results go to Run.Deployments, never to Stages, so a
 // failed deploy does not fail the publish; Retry deploy in the Publish view runs the failed ones again.
 private async Task DeployPushed(PublishLog log) {
  foreach(var (profile,artifact) in _pushedNow) {
   if(profile.Container?.Target.Type!=TargetType.BuiltIn||artifact.Digest.Length==0||artifact.Result!=PublishResult.Success)continue;
   var reference=Repository(artifact.Target);var slash=reference.IndexOf('/');
   var host=reference[..slash];var repository=reference[(slash+1)..];
   var deployment=new PublishDeployment {Repository=repository,Tag=artifact.Version,Digest=artifact.Digest};
   try {
    if(Targets.Connection.Length==0||new Uri(Targets.Connection).Authority!=host)throw new InvalidDataException("The active connection is not the registry this image was pushed to.");
    log.Line($"Deploy {repository}@{artifact.Digest}...");
    var run=await Targets.DeployAfterPush(new() {Repository=repository,Tags=[..artifact.PushedTags],Digest=artifact.Digest});
    deployment.ImageId=run.ImageId;deployment.RunId=run.Id;deployment.Result=run.Result;deployment.Tag=run.Tag??artifact.Version;
    deployment.Message=run.Result switch {
     Em.Api.Core.Models.CtnDeployResult.Success=>"Deployed.",
     Em.Api.Core.Models.CtnDeployResult.Skipped=>run.Output??"Skipped.",
     _=>LastLine(run.Output)??"Deploy failed."
    };
    if(!string.IsNullOrEmpty(run.Output)&&run.Result!=Em.Api.Core.Models.CtnDeployResult.Skipped)foreach(var line in run.Output.Split('\n'))log.Line("  "+line.TrimEnd('\r'));
   } catch(Exception ex) {
    deployment.Result=Em.Api.Core.Models.CtnDeployResult.Failed;deployment.Message=log.Masker.Mask(ex.Message);
   }
   log.Line($"Deploy {repository}: {deployment.Result}. {deployment.Message}");
   log.Run.Deployments.Add(deployment);log.Save();
  }
 }
 private static string? LastLine(string? text)=>text?.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).LastOrDefault();
 /// <summary>Runs the failed deploys of <paramref name="run"/> again (manual deploy on the server) and rewrites its result.json.</summary>
 public async Task<int> RetryDeployments(PublishRun run,string directory) {
  var failed=0;
  foreach(var deployment in run.Deployments.Where(d=>d.Result==Em.Api.Core.Models.CtnDeployResult.Failed)) {
   try {
    if(deployment.ImageId.Length==0)deployment.ImageId=await Targets.ImageId(deployment.Repository);
    var result=await Targets.DeployRun(deployment.ImageId,deployment.Digest,deployment.Tag.Length==0?null:deployment.Tag);
    deployment.RunId=result.Id;deployment.Result=result.Result;deployment.Message=result.Result==Em.Api.Core.Models.CtnDeployResult.Success?"Deployed (retry).":LastLine(result.Output)??"Deploy failed.";
   } catch(Exception ex) {deployment.Message=ex.Message;}
   if(deployment.Result==Em.Api.Core.Models.CtnDeployResult.Failed)failed++;
  }
  // The stored result.json is masked; only the deployments are replaced in it, so nothing unmasked is written.
  var file=Path.Combine(directory,"result.json");PublishPaths.RejectLinks(file);
  var stored=JsonSerializer.Deserialize<PublishRun>(File.ReadAllText(file),ProfileJson.Options)!;stored.Deployments=run.Deployments;
  PublishPaths.Atomic(file,ProfileJson.Write(stored));
  return failed;
 }
 /// <summary>Checks that what was published is really there.</summary>
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
