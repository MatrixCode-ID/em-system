using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using NuGet.Common;

namespace Em.Ui.Wpf.Publish;

/// <summary>The publish result.</summary>
public enum PublishResult
{
   /// <summary>The run is in progress.</summary>
   Running,
   /// <summary>The run succeeded.</summary>
   Success,
   /// <summary>The step was skipped.</summary>
   Skipped,
   /// <summary>The version already exists.</summary>
   Duplicate,
   /// <summary>The run failed.</summary>
   Failed,
   /// <summary>The run was cancelled.</summary>
   Cancelled,
   /// <summary>The step was not run.</summary>
   NotRun,
   /// <summary>The run was interrupted before it finished.</summary>
   Interrupted,
}
/// <summary>The publish artifact.</summary>
public sealed class PublishArtifact {
 /// <summary>Indicates selected.</summary>
 public bool Selected { get; set; }=true;
 /// <summary>The profile id.</summary>
 public string ProfileId { get; set; }="";
 /// <summary>The source.</summary>
 public string Source { get; set; }="";
 /// <summary>The file.</summary>
 public string File { get; set; }="";
 /// <summary>The id.</summary>
 public string Id { get; set; }="";
 /// <summary>The version.</summary>
 public string Version { get; set; }="";
 /// <summary>The image id.</summary>
 public string ImageId { get; set; }="";
 /// <summary>The digest.</summary>
 public string Digest { get; set; }="";
 /// <summary>The hash.</summary>
 public string Hash { get; set; }="";
 /// <summary>The target.</summary>
 public string Target { get; set; }="";
 /// <summary>The result.</summary>
 public PublishResult Result { get; set; }=PublishResult.NotRun;
 /// <summary>The exit code.</summary>
 public int? ExitCode { get; set; }
 /// <summary>The message.</summary>
 public string Message { get; set; }="";
 /// <summary>The verification.</summary>
 public string Verification { get; set; }="Not checked";
 /// <summary>Tags pushed for <see cref="Digest"/> in this run: the version tag and each floating tag that succeeded.</summary>
 public List<string> PushedTags { get; set; }=[];
 /// <inheritdoc />
 public override string ToString()=>$"{Id} {Version} · {Result} · {Verification}";
}
/// <summary>The publish run.</summary>
public sealed class PublishRun {
 /// <summary>The id.</summary>
 public string Id { get; set; }=Guid.NewGuid().ToString("N");
 /// <summary>The profile id.</summary>
 public string ProfileId { get; set; }="";
 /// <summary>The profile name.</summary>
 public string ProfileName { get; set; }="";
 /// <summary>The kind.</summary>
 public PublishKind Kind { get; set; }
 /// <summary>The operation.</summary>
 public string Operation { get; set; }="";
 /// <summary>The started.</summary>
 public DateTimeOffset Started { get; set; }=DateTimeOffset.Now;
 /// <summary>The finished.</summary>
 public DateTimeOffset? Finished { get; set; }
 /// <summary>The duration seconds.</summary>
 public double? DurationSeconds { get; set; }
 /// <summary>The windows user.</summary>
 public string WindowsUser { get; set; }=Environment.UserName;
 /// <summary>The release notes.</summary>
 public string ReleaseNotes { get; set; }="";
 /// <summary>The target.</summary>
 public string Target { get; set; }="";
 /// <summary>The retry of.</summary>
 public string? RetryOf { get; set; }
 /// <summary>The result.</summary>
 public PublishResult Result { get; set; }=PublishResult.Running;
 /// <summary>The artifacts.</summary>
 public List<PublishArtifact> Artifacts { get; set; }=[];
 /// <summary>The stages.</summary>
 public List<PublishStage> Stages { get; set; }=[];
 /// <summary>Deploys the server ran after the push (Built-in registry, Auto deploy on). Kept apart from <see cref="Stages"/>: a failed
 /// deploy does not make the publish run fail.</summary>
 public List<PublishDeployment> Deployments { get; set; }=[];
 /// <summary>The settings.</summary>
 public JsonElement? Settings { get; set; }
 /// <inheritdoc />
 public override string ToString()=>$"{Started:g} · {ProfileName} · {Operation} · {Result}";
}
/// <summary>One deploy after a push: what was deployed and how it ended, as reported by the server.</summary>
public sealed class PublishDeployment {
 /// <summary>Pull name without host: root/name.</summary>
 public string Repository { get; set; }="";
 /// <summary>The image id.</summary>
 public string ImageId { get; set; }="";
 /// <summary>The tag.</summary>
 public string Tag { get; set; }="";
 /// <summary>The digest.</summary>
 public string Digest { get; set; }="";
 /// <summary>The result.</summary>
 public Em.Api.Core.Models.CtnDeployResult Result { get; set; }
 /// <summary>The message.</summary>
 public string Message { get; set; }="";
 /// <summary>Server run id; empty when the deploy was skipped or the server could not be reached.</summary>
 public string RunId { get; set; }="";
 /// <inheritdoc />
 public override string ToString()=>$"{Repository} {Tag} · {Result} · {Message}";
}
/// <summary>One stage of a publish run and its result.</summary>
public sealed record PublishStage(string Name,PublishResult Result,string Message,int? ExitCode=null);
/// <summary>One entry of the publish history.</summary>
public sealed record HistoryEntry(string Directory,PublishRun Run);
/// <summary>The secret masker.</summary>
public sealed class SecretMasker {
 private readonly HashSet<string> _values=[];
 /// <summary>Adds a value to be masked in logs.</summary>
 public void Add(string? value) {if(!string.IsNullOrEmpty(value)) { _values.Add(value);_values.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));_values.Add(JsonSerializer.Serialize(value)[1..^1]);_values.Add(Uri.EscapeDataString(value)); }}
 /// <summary>Adds a credential whose secret is masked in logs.</summary>
 public void AddCredential(string username,string? secret) {Add(secret);if(!string.IsNullOrEmpty(secret))_values.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(username+":"+secret)));}
 /// <summary>Replaces every known secret in a text with asterisks.</summary>
 public string Mask(string text) {foreach(var value in _values.OrderByDescending(v=>v.Length))text=text.Replace(value,"***",StringComparison.Ordinal);return text;}
}
/// <summary>The log of one publish run, written to its history folder.</summary>
public sealed class PublishLog : LoggerBase {
 private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,byte> Active=[];
 private readonly object _gate=new();
 /// <summary>The run.</summary>
 public PublishRun Run { get; }
 /// <summary>The masker.</summary>
 public SecretMasker Masker { get; }
 /// <summary>The directory.</summary>
 public string Directory { get; }
 /// <summary>Raised for output.</summary>
 public event Action<string>? Output;
 /// <summary>Creates a new instance of <see cref="PublishLog"/>.</summary>
 public PublishLog(string root,PublishRun run,SecretMasker masker) {
  Run=run;Masker=masker;
  Directory=PublishPaths.Inside(root,Path.Combine(run.ProfileId,run.Started.ToString("yyyyMMdd-HHmmss")+"-"+run.Id));
  System.IO.Directory.CreateDirectory(Directory);Save();Active.TryAdd(run.Id,0);
 }
 /// <summary>Saves the result file of the run.</summary>
 public void Save() {lock(_gate)PublishPaths.Atomic(Path.Combine(Directory,"result.json"),Masker.Mask(ProfileJson.Write(Run)));}
 /// <summary>Writes one masked line to the log.</summary>
 public void Line(string line) {line=Masker.Mask(line);lock(_gate)File.AppendAllText(Path.Combine(Directory,"output.log"),line+Environment.NewLine);Output?.Invoke(line);}
 /// <summary>Records the result of one stage.</summary>
 public void Stage(string name,PublishResult result,string message="",int? code=null) {Run.Stages.Add(new(name,result,Masker.Mask(message),code));Save();}
 /// <summary>Finishes the run with a result.</summary>
 public void Finish(PublishResult result) {Run.Result=result;Run.Finished=DateTimeOffset.Now;Run.DurationSeconds=(Run.Finished.Value-Run.Started).TotalSeconds;try {Save();}finally {Active.TryRemove(Run.Id,out _);}}
 /// <inheritdoc />
 public override void Log(ILogMessage message)=>Line(message.Message);
 /// <inheritdoc />
 public override Task LogAsync(ILogMessage message) {Log(message);return Task.CompletedTask;}
 /// <summary>Lists the stored history, newest first.</summary>
 public static IReadOnlyList<HistoryEntry> History(string root,string? profileId=null) {
  if(!System.IO.Directory.Exists(root))return [];
  PublishPaths.ValidateTree(root);var rows=new List<HistoryEntry>();
  foreach(var file in System.IO.Directory.EnumerateFiles(root,"result.json",SearchOption.AllDirectories)) {
   try {PublishPaths.RejectLinks(file);var run=JsonSerializer.Deserialize<PublishRun>(File.ReadAllText(file),ProfileJson.Options)!;if(profileId!=null&&run.ProfileId!=profileId)continue;if(run.Finished==null)run.Result=PublishResult.Interrupted;rows.Add(new(Path.GetDirectoryName(file)!,run));}
   catch(Exception ex) when(ex is IOException or JsonException) { }
  }
  return rows.OrderByDescending(r=>r.Run.Started).ToArray();
 }
 /// <summary>Deletes one history entry.</summary>
 public static void Delete(string root,HistoryEntry entry) {
  if(Active.ContainsKey(entry.Run.Id))throw new IOException("Wait for this active run to finish before deleting its log.");
  var path=PublishPaths.Inside(root,Path.GetRelativePath(root,entry.Directory));
  if(!File.Exists(Path.Combine(path,"result.json")))throw new IOException("Not a publish log directory.");
  PublishPaths.ValidateTree(path);
  System.IO.Directory.Delete(path,true);
 }
}
/// <summary>The result of running a process.</summary>
public sealed record ProcessResult(int ExitCode,string Output);
/// <summary>The publish process runner.</summary>
public sealed class PublishProcessRunner {
 /// <summary>The default environment.</summary>
 public Dictionary<string,string> DefaultEnvironment { get; set; }=[];
 /// <summary>Runs a process and returns its exit code and output.</summary>
 public async Task<ProcessResult> RunAsync(string executable,IEnumerable<string> args,string cwd,Action<string>? output=null,
  CancellationToken ct=default,Dictionary<string,string>? environment=null,string? stdin=null) {
  var info=new ProcessStartInfo(executable) {WorkingDirectory=cwd,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=stdin!=null};
  foreach(var arg in args)info.ArgumentList.Add(arg);
  foreach(var pair in DefaultEnvironment)info.Environment[pair.Key]=pair.Value;
  if(environment!=null)foreach(var pair in environment)info.Environment[pair.Key]=pair.Value;
  using var process=new Process {StartInfo=info};var captured=new StringBuilder();var gate=new object();
  ct.ThrowIfCancellationRequested();process.Start();
  using var registration=ct.Register(()=> {try {if(!process.HasExited)process.Kill(entireProcessTree:true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){} });
  async Task Drain(StreamReader reader) {while(await reader.ReadLineAsync() is {} line) {lock(gate)captured.AppendLine(line);output?.Invoke(line);}}
  var stdout=Drain(process.StandardOutput);var stderr=Drain(process.StandardError);
  try {
   if(stdin!=null) {await process.StandardInput.WriteAsync(stdin.AsMemory(),ct);process.StandardInput.Close();}
   await process.WaitForExitAsync(CancellationToken.None);await Task.WhenAll(stdout,stderr);ct.ThrowIfCancellationRequested();
   return new(process.ExitCode,captured.ToString());
  } catch {
   try {if(!process.HasExited)process.Kill(entireProcessTree:true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
   await process.WaitForExitAsync(CancellationToken.None);
   try {await Task.WhenAll(stdout,stderr);}catch { }
   throw;
  }
 }
 /// <summary>Runs a process and throws when it fails.</summary>
 public async Task<string> RequireAsync(string executable,IEnumerable<string> args,string cwd,PublishLog log,CancellationToken ct,Dictionary<string,string>? environment=null,string? stdin=null) {
  var result=await RunAsync(executable,args,cwd,log.Line,ct,environment,stdin);
  log.Stage(executable, result.ExitCode==0?PublishResult.Success:PublishResult.Failed,"",result.ExitCode);
  if(result.ExitCode!=0)throw new IOException($"{executable} exited with code {result.ExitCode}. See the masked output log.");return result.Output;
 }
 /// <summary>One line per tool: "name: version" when it works, otherwise the repair hint (which starts with Install/Start).</summary>
 public async Task<string[]> CheckTools(PublishProfile p,CancellationToken ct)=>
  (await CheckToolsDetailed(p,ct)).Select(x=>x.Ok?$"{x.Name}: {x.Detail}":x.Detail).ToArray();
 /// <summary>Runs the local tools the profile needs and returns a short result per tool, for the Tools dialog and Check.</summary>
 public async Task<ToolCheck[]> CheckToolsDetailed(PublishProfile p,CancellationToken ct) {
  var checks=new List<(string Name,string Tool,string[] Args,string Purpose,string Repair)>();
  if(p.Kind==PublishKind.NuGet||p.Container?.Mode is ContainerMode.Template or ContainerMode.Set)
   checks.Add((".NET SDK","dotnet",["--version"],p.Kind==PublishKind.NuGet?"Packs the projects.":"Publishes the .NET project for Template mode.","Install the .NET SDK required by global.json."));
  if(p.Kind==PublishKind.Container) {
   checks.Add(("Docker","docker",["version","--format","json"],"Builds, tags and pushes the image.","Start Docker Desktop/daemon and select the correct Docker context."));
   if(p.Container?.Mode==ContainerMode.Compose)checks.Add(("Docker Compose","docker",["compose","version","--short"],"Builds the selected Compose services.","Install Docker Compose v2."));
   if(!string.IsNullOrWhiteSpace(p.Container?.Dockerfile.Platform))checks.Add(("Docker Buildx","docker",["buildx","version"],"Builds for the platform "+p.Container!.Dockerfile.Platform+".","Install Docker Buildx for the selected platform."));
  }
  var rows=new List<ToolCheck>();
  foreach(var item in checks) {
   try {var result=await RunAsync(item.Tool,item.Args,p.Workspace,ct:ct);rows.Add(result.ExitCode==0?new(item.Name,true,Summarize(item.Tool,item.Args,result.Output),item.Purpose):new(item.Name,false,item.Repair,item.Purpose));}
   catch(System.ComponentModel.Win32Exception) {rows.Add(new(item.Name,false,item.Repair,item.Purpose));}
  }
  return rows.ToArray();
 }
 // `docker version --format json` prints the whole client and server description; only the versions are useful here.
 private static string Summarize(string tool,string[] args,string output) {
  if(tool=="docker"&&args[0]=="version") {
   try {
    using var json=JsonDocument.Parse(output);var root=json.RootElement;
    string? Get(string section,string name)=>root.TryGetProperty(section,out var s)&&s.ValueKind==JsonValueKind.Object&&s.TryGetProperty(name,out var v)?v.GetString():null;
    var server=Get("Server","Version");var platform=Get("Server","Os") is {} os?os+"/"+Get("Server","Arch"):null;
    return "client "+(Get("Client","Version")??"?")+" · server "+(server??"not reachable")+(platform!=null?" ("+platform+")":"");
   } catch(JsonException) {}
  }
  var first=output.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).FirstOrDefault()??"";
  return first.Length>120?first[..120]+"…":first;
 }
}
/// <summary>The result of one local tool check: whether it works, its version or the repair hint, and why the profile needs it.</summary>
public sealed record ToolCheck(string Name,bool Ok,string Detail,string Purpose);
