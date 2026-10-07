using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Em.Ui.Wpf.Publish;

/// <summary>The template builder.</summary>
public static class TemplateBuilder {
 /// <summary>Selects the files of a source folder that belong to a file set.</summary>
 public static string[] SelectFiles(string source,FileSet set,IReadOnlyList<NamedFileList> lists,Action<string>? warning=null) {
  PublishPaths.ValidateTree(source);
  var matcher=new Matcher(StringComparison.OrdinalIgnoreCase);matcher.AddIncludePatterns(set.Include.Count>0?set.Include:["**/*"]);matcher.AddExcludePatterns(set.Exclude);
  if(set.ExcludeDebugSymbols)matcher.AddExclude("**/*.pdb");
  var files=matcher.GetResultsInFullPath(source).Select(f=>Path.GetRelativePath(source,f).Replace('\\','/')).ToArray();
  if(set.NamedList.Length>0) {
   var list=lists.SingleOrDefault(l=>l.Name==set.NamedList)??throw new InvalidDataException("Named file list not found: "+set.NamedList);
   var selected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var entry in list.Entries) {
    var relative=entry.Replace('\\','/').Trim('/');var path=PublishPaths.Inside(source,relative);
    if(File.Exists(path))selected.Add(relative);
    else if(Directory.Exists(path))foreach(var f in Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories))selected.Add(Path.GetRelativePath(source,f).Replace('\\','/'));
    else if(list.MissingEntry==MissingEntry.Error)throw new FileNotFoundException("Named list entry missing: "+entry);
    else warning?.Invoke("Warning: named list entry missing: "+entry);
   }
   files=files.Where(f=>set.ListMode==FileListMode.IncludeOnly?selected.Contains(f):!selected.Contains(f)).ToArray();
  }
  foreach(var required in set.RequiredFiles) {var path=PublishPaths.Inside(source,required);var rel=Path.GetRelativePath(source,path).Replace('\\','/');if(!files.Contains(rel,StringComparer.OrdinalIgnoreCase))throw new FileNotFoundException("Required staged file missing: "+required);}
  return files;
 }
 /// <summary>Copies the selected files to a staging folder.</summary>
 public static string[] Stage(string source,string destination,FileSet set,IReadOnlyList<NamedFileList> lists,Action<string>? warning=null) {
  var files=SelectFiles(source,set,lists,warning);Directory.CreateDirectory(destination);
  foreach(var relative in files) {var target=PublishPaths.Inside(destination,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(PublishPaths.Inside(source,relative),target);}
  return files;
 }
 /// <summary>Composes the Dockerfile of a template profile.</summary>
 public static string Dockerfile(TemplateProfile p) {
  static string Line(string value) {if(value.Contains('\r')||value.Contains('\n')||value.Contains('\0'))throw new InvalidDataException("Dockerfile fields must use one line.");return value;}
  static string Shell(string value)=>"'"+value.Replace("'","'\"'\"'")+"'";
  var text=new StringBuilder();
  if(p.UseSetBase)text.AppendLine("ARG BASE_IMAGE").AppendLine("FROM ${BASE_IMAGE}");else text.AppendLine("FROM "+Line(p.BaseImage));
  text.AppendLine("WORKDIR "+Line(p.WorkingDirectory));
  foreach(var run in p.RunBeforeCopy)text.AppendLine("RUN "+Line(run));
  text.AppendLine("COPY "+ProfileJson.Write(new[]{p.StagingSubfolder.Length>0?"./"+Line(p.StagingSubfolder)+"/":"./","./"}).Replace("\r","").Replace("\n",""));
  foreach(var pair in p.Environment) {if(!Regex.IsMatch(pair.Key,"^[A-Za-z_][A-Za-z0-9_]*$"))throw new InvalidDataException("Invalid environment variable name.");text.AppendLine("ENV "+pair.Key+"="+System.Text.Json.JsonSerializer.Serialize(Line(pair.Value)));}
  foreach(var port in p.Ports) {if(!Regex.IsMatch(port,@"^\d+(?:/(?:tcp|udp))?$"))throw new InvalidDataException("Invalid exposed port.");text.AppendLine("EXPOSE "+port);}
  if(p.TimeZone.Length>0) {if(!Regex.IsMatch(p.TimeZone,@"^[A-Za-z0-9_+/-]+$")||p.TimeZone.Contains(".."))throw new InvalidDataException("Invalid time zone.");text.AppendLine("ENV TZ="+p.TimeZone).AppendLine("RUN ln -snf "+Shell("/usr/share/zoneinfo/"+p.TimeZone)+" /etc/localtime");}
  if(p.MarkEntrypointExecutable) {if(p.Entrypoint.Count==0)throw new InvalidDataException("Entrypoint is required for chmod.");text.AppendLine("RUN chmod +x "+Shell(Line(p.Entrypoint[0])));}
  foreach(var run in p.RunAfterCopy)text.AppendLine("RUN "+Line(run));
  if(p.Entrypoint.Count>0)text.AppendLine("ENTRYPOINT "+System.Text.Json.JsonSerializer.Serialize(p.Entrypoint));
  return text.ToString();
 }
}
