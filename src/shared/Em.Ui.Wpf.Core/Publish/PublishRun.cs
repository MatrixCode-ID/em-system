using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using NuGet.Common;

namespace Em.Ui.Wpf.Publish;

public enum PublishResult { Running, Success, Skipped, Duplicate, Failed, Cancelled, NotRun, Interrupted }
public sealed class PublishArtifact {
 public bool Selected { get; set; }=true;
 public string ProfileId { get; set; }="";
 public string Source { get; set; }="";
 public string File { get; set; }="";
 public string Id { get; set; }="";
 public string Version { get; set; }="";
 public string ImageId { get; set; }="";
 public string Digest { get; set; }="";
 public string Hash { get; set; }="";
 public string Target { get; set; }="";
 public PublishResult Result { get; set; }=PublishResult.NotRun;
 public int? ExitCode { get; set; }
 public string Message { get; set; }="";
 public string Verification { get; set; }="Not checked";
 public override string ToString()=>$"{Id} {Version} · {Result} · {Verification}";
}
public sealed class PublishRun {
 public string Id { get; set; }=Guid.NewGuid().ToString("N");
 public string ProfileId { get; set; }="";
 public string ProfileName { get; set; }="";
 public PublishKind Kind { get; set; }
 public string Operation { get; set; }="";
 public DateTimeOffset Started { get; set; }=DateTimeOffset.Now;
 public DateTimeOffset? Finished { get; set; }
 public double? DurationSeconds { get; set; }
 public string WindowsUser { get; set; }=Environment.UserName;
 public string ReleaseNotes { get; set; }="";
 public string Target { get; set; }="";
 public string? RetryOf { get; set; }
 public PublishResult Result { get; set; }=PublishResult.Running;
 public List<PublishArtifact> Artifacts { get; set; }=[];
 public List<PublishStage> Stages { get; set; }=[];
 public JsonElement? Settings { get; set; }
 public override string ToString()=>$"{Started:g} · {ProfileName} · {Operation} · {Result}";
}
public sealed record PublishStage(string Name,PublishResult Result,string Message,int? ExitCode=null);
public sealed record HistoryEntry(string Directory,PublishRun Run);
public sealed class SecretMasker {
 private readonly HashSet<string> _values=[];
 public void Add(string? value) {if(!string.IsNullOrEmpty(value)) { _values.Add(value);_values.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));_values.Add(JsonSerializer.Serialize(value)[1..^1]);_values.Add(Uri.EscapeDataString(value)); }}
 public void AddCredential(string username,string? secret) {Add(secret);if(!string.IsNullOrEmpty(secret))_values.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(username+":"+secret)));}
 public string Mask(string text) {foreach(var value in _values.OrderByDescending(v=>v.Length))text=text.Replace(value,"***",StringComparison.Ordinal);return text;}
}
public sealed class PublishLog : LoggerBase {
 private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,byte> Active=[];
 private readonly object _gate=new();
 public PublishRun Run { get; }
 public SecretMasker Masker { get; }
 public string Directory { get; }
 public event Action<string>? Output;
 public PublishLog(string root,PublishRun run,SecretMasker masker) {
  Run=run;Masker=masker;
  Directory=PublishPaths.Inside(root,Path.Combine(run.ProfileId,run.Started.ToString("yyyyMMdd-HHmmss")+"-"+run.Id));
  System.IO.Directory.CreateDirectory(Directory);Save();Active.TryAdd(run.Id,0);
 }
 public void Save() {lock(_gate)PublishPaths.Atomic(Path.Combine(Directory,"result.json"),Masker.Mask(ProfileJson.Write(Run)));}
 public void Line(string line) {line=Masker.Mask(line);lock(_gate)File.AppendAllText(Path.Combine(Directory,"output.log"),line+Environment.NewLine);Output?.Invoke(line);}
 public void Stage(string name,PublishResult result,string message="",int? code=null) {Run.Stages.Add(new(name,result,Masker.Mask(message),code));Save();}
 public void Finish(PublishResult result) {Run.Result=result;Run.Finished=DateTimeOffset.Now;Run.DurationSeconds=(Run.Finished.Value-Run.Started).TotalSeconds;try {Save();}finally {Active.TryRemove(Run.Id,out _);}}
 public override void Log(ILogMessage message)=>Line(message.Message);
 public override Task LogAsync(ILogMessage message) {Log(message);return Task.CompletedTask;}
 public static IReadOnlyList<HistoryEntry> History(string root,string? profileId=null) {
  if(!System.IO.Directory.Exists(root))return [];
  PublishPaths.ValidateTree(root);var rows=new List<HistoryEntry>();
  foreach(var file in System.IO.Directory.EnumerateFiles(root,"result.json",SearchOption.AllDirectories)) {
   try {PublishPaths.RejectLinks(file);var run=JsonSerializer.Deserialize<PublishRun>(File.ReadAllText(file),ProfileJson.Options)!;if(profileId!=null&&run.ProfileId!=profileId)continue;if(run.Finished==null)run.Result=PublishResult.Interrupted;rows.Add(new(Path.GetDirectoryName(file)!,run));}
   catch(Exception ex) when(ex is IOException or JsonException) { }
  }
  return rows.OrderByDescending(r=>r.Run.Started).ToArray();
 }
 public static void Delete(string root,HistoryEntry entry) {
  if(Active.ContainsKey(entry.Run.Id))throw new IOException("Wait for this active run to finish before deleting its log.");
  var path=PublishPaths.Inside(root,Path.GetRelativePath(root,entry.Directory));
  if(!File.Exists(Path.Combine(path,"result.json")))throw new IOException("Not a publish log directory.");
  PublishPaths.ValidateTree(path);
  System.IO.Directory.Delete(path,true);
 }
}
public sealed record ProcessResult(int ExitCode,string Output);
public sealed class PublishProcessRunner {
 public Dictionary<string,string> DefaultEnvironment { get; set; }=[];
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
