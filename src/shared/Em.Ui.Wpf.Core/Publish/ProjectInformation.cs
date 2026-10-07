using System.IO;
using System.Text.Json;
using System.Xml.Linq;

namespace Em.Ui.Wpf.Publish;

/// <summary>What the publisher knows about one project: its package id, version, frameworks, and whether it can be packed.</summary>
public sealed record ProjectInformation(string Path,string PackageId,string Version,string Frameworks,bool IsPackable,bool IsHost,string Reason,string? Dockerfile);
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
