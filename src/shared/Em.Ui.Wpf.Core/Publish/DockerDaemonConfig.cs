using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Em.Ui.Wpf.Publish;

/// <summary>
/// Edits the <c>insecure-registries</c> list of Docker's <c>daemon.json</c> (the file Docker Desktop's "Docker Engine"
/// settings page edits). Docker decides http or https for a push itself, so a registry served over plain HTTP has to
/// be listed there. The file is backed up before every change and every other setting is kept as it was.
/// </summary>
public static class DockerDaemonConfig {
 private const string Key="insecure-registries";
 private static readonly Regex HostPattern=new(@"^(\[[0-9A-Fa-f:]+\]|[A-Za-z0-9]([A-Za-z0-9.\-]*[A-Za-z0-9])?)(:\d{1,5})?$",RegexOptions.CultureInvariant);
 private static readonly JsonSerializerOptions Output=new() {WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
 private static readonly JsonDocumentOptions Input=new() {CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true};

 /// <summary>Where Docker Desktop keeps its engine settings: <c>%USERPROFILE%\.docker\daemon.json</c>.</summary>
 public static string DefaultPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".docker","daemon.json");

 /// <summary>Whether <paramref name="host"/> (<c>name</c> or <c>name:port</c>) is already in the list.</summary>
 public static bool IsListed(string path,string host) {
  Validate(host);
  return Read(path)?[Key] is JsonArray list&&list.Any(x=>string.Equals(x?.GetValue<string>(),host,StringComparison.OrdinalIgnoreCase));
 }

 /// <summary>
 /// Adds <paramref name="host"/> to the list. Returns the backup file name, <c>""</c> when there was no file to back
 /// up, or <c>null</c> when the host was already listed and nothing was written. Docker has to be restarted to apply it.
 /// </summary>
 public static string? AddInsecureRegistry(string path,string host) {
  Validate(host);
  var root=Read(path)??new JsonObject();
  var list=root[Key] switch {null=>new JsonArray(),JsonArray existing=>existing,_=>throw new InvalidDataException($"'{Key}' in {Path.GetFileName(path)} is not a list; fix the file by hand.")};
  if(list.Any(x=>string.Equals(x?.GetValue<string>(),host,StringComparison.OrdinalIgnoreCase)))return null;
  list.Add(host);root[Key]=list;
  var backup="";
  if(File.Exists(path)) {backup=path+".em-backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");File.Copy(path,backup,false);}
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
  var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try {File.WriteAllText(temp,root.ToJsonString(Output),new UTF8Encoding(false));File.Move(temp,path,true);}
  finally {if(File.Exists(temp))File.Delete(temp);}
  return backup.Length>0?Path.GetFileName(backup):"";
 }

 private static JsonObject? Read(string path) {
  if(!File.Exists(path))return null;
  var text=File.ReadAllText(path);if(string.IsNullOrWhiteSpace(text))return new JsonObject();
  try {return JsonNode.Parse(text,documentOptions:Input) as JsonObject??throw new InvalidDataException($"{Path.GetFileName(path)} is not a JSON object; fix the file by hand.");}
  catch(JsonException ex) {throw new InvalidDataException($"{Path.GetFileName(path)} is not valid JSON ({ex.Message}); fix the file by hand.");}
 }

 private static void Validate(string host) {
  if(string.IsNullOrWhiteSpace(host)||!HostPattern.IsMatch(host))throw new InvalidDataException($"'{host}' is not a registry host (use host or host:port, without a scheme or path).");
 }
}
