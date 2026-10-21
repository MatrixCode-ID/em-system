using System.IO;
using System.Text.Json;
using System.Xml.Linq;

namespace Em.Ui.Wpf.Publish;

/// <summary>What the publisher knows about one project: its package id, version, frameworks, and whether it can be packed.</summary>
public sealed record ProjectInformation(string Path,string PackageId,string Version,string Frameworks,bool IsPackable,bool IsHost,string Reason,string? Dockerfile);
/// <summary>
/// The values a project offers for the Configuration, Runtime, and Framework fields of a Template publish. Runtimes
/// lists the project's own runtime identifiers first, then the Linux ones a container can run on.
/// </summary>
public sealed record ProjectOptions(string[] Configurations,string[] Runtimes,string[] Frameworks) {
 /// <summary>The Linux runtime identifiers a container image is built for.</summary>
 public static readonly string[] LinuxRuntimes=["linux-x64","linux-arm64","linux-musl-x64","linux-musl-arm64"];
 /// <summary>What is offered when the project cannot be read: the default configurations and the Linux runtimes.</summary>
 public static ProjectOptions Fallback { get; }=new(["Debug","Release"],LinuxRuntimes,[]);
 /// <summary>Builds the options from MSBuild property values (semicolon lists), filling the gaps from <see cref="Fallback"/>.</summary>
 public static ProjectOptions From(string configurations,string runtimes,string frameworks) {
  static string[] Split(string value)=>value.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
  var configs=Split(configurations);
  return new(configs.Length>0?configs:Fallback.Configurations,Split(runtimes).Concat(LinuxRuntimes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),Split(frameworks));
 }
}
/// <summary>Reads project information through the .NET tools.</summary>
public sealed class ProjectReader(PublishProcessRunner runner) {
 /// <summary>Lists the projects of a solution or folder.</summary>
 public async Task<string[]> Projects(string source,CancellationToken ct=default) {
  source=Path.GetFullPath(source);var dir=Path.GetDirectoryName(source)!;
  if(source.EndsWith(".csproj",StringComparison.OrdinalIgnoreCase))return [source];
  if(source.EndsWith(".slnx",StringComparison.OrdinalIgnoreCase))return XDocument.Load(source).Descendants().Where(e=>e.Name.LocalName=="Project").Select(e=>Path.GetFullPath(e.Attribute("Path")!.Value,dir)).Where(p=>p.EndsWith(".csproj",StringComparison.OrdinalIgnoreCase)).ToArray();
  if(!source.EndsWith(".sln",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Select a .csproj, .sln or .slnx.");
  var result=await runner.RunAsync("dotnet",["sln",source,"list"],dir,ct:ct);if(result.ExitCode!=0)throw new IOException("Cannot read solution projects.");
  return result.Output.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).Where(s=>s.EndsWith(".csproj",StringComparison.OrdinalIgnoreCase)).Select(s=>Path.GetFullPath(s,dir)).ToArray();
 }
 /// <summary>Reads the configurations, runtime identifiers, and target frameworks a project declares.</summary>
 public async Task<ProjectOptions> Options(string project,CancellationToken ct=default) {
  try {
   var result=await runner.RunAsync("dotnet",["msbuild",project,"-nologo","-getProperty:Configurations,RuntimeIdentifiers,RuntimeIdentifier,TargetFrameworks,TargetFramework"],Path.GetDirectoryName(project)!,ct:ct);
   if(result.ExitCode!=0)return ProjectOptions.Fallback;
   using var json=JsonDocument.Parse(result.Output);var props=json.RootElement.GetProperty("Properties");
   string Get(string key)=>props.TryGetProperty(key,out var value)?value.GetString()??"":"";
   return ProjectOptions.From(Get("Configurations"),Get("RuntimeIdentifiers")+";"+Get("RuntimeIdentifier"),Get("TargetFrameworks") is {Length:>0} f?f:Get("TargetFramework"));
  } catch(Exception ex) when(ex is IOException or JsonException or KeyNotFoundException or System.ComponentModel.Win32Exception) {return ProjectOptions.Fallback;}
 }
 /// <summary>Reads the information of one project.</summary>
 public async Task<ProjectInformation> Read(string project,string configuration="Release",string framework="",CancellationToken ct=default) {
  var args=new List<string> {"msbuild",project,"-nologo","-p:Configuration="+configuration,"-getProperty:IsPackable,PackageId,PackageVersion,Version,TargetFramework,TargetFrameworks,OutputType,UsingMicrosoftNETSdkWeb,UseWPF,UseWindowsForms"};
  if(framework.Length>0)args.Add("-p:TargetFramework="+framework);
  try {
   var result=await runner.RunAsync("dotnet",args,Path.GetDirectoryName(project)!,ct:ct);if(result.ExitCode!=0)throw new IOException("MSBuild evaluation failed.");
   using var json=JsonDocument.Parse(result.Output);var props=json.RootElement.GetProperty("Properties");
   string Get(string key)=>props.TryGetProperty(key,out var value)?value.GetString()??"":"";
   var desktop=Get("UseWPF").Equals("true",StringComparison.OrdinalIgnoreCase)||Get("UseWindowsForms").Equals("true",StringComparison.OrdinalIgnoreCase);
   var pack=Get("IsPackable").Equals("true",StringComparison.OrdinalIgnoreCase);
   var host=!desktop&&(Get("OutputType").Equals("Exe",StringComparison.OrdinalIgnoreCase)||Get("UsingMicrosoftNETSdkWeb").Equals("true",StringComparison.OrdinalIgnoreCase));
   var dockerfile=Path.Combine(Path.GetDirectoryName(project)!,"Dockerfile");
   return new(project,Get("PackageId"),Get("PackageVersion") is {Length:>0} v?v:Get("Version"),Get("TargetFrameworks") is {Length:>0} f?f:Get("TargetFramework"),pack,host,desktop?"Desktop project; not a Linux container host":!pack?"IsPackable=false":"",File.Exists(dockerfile)?dockerfile:null);
  } catch(Exception ex) when(ex is IOException or JsonException or System.ComponentModel.Win32Exception) {return new(project,"unknown","unknown","unknown",false,false,ex.Message,null);}
 }
}
