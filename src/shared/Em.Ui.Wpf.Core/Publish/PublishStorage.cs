using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using Em.Ui.Wpf.Core;

namespace Em.Ui.Wpf.Publish;

public sealed class PublisherSettings(EmApp? app = null) {
 public static string DefaultProfiles => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Em","Publish","Profiles");
 public static string DefaultLogs => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Em","Publish","Logs");
 public static string DefaultWork => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Em","Publish","Work");
 private readonly Dictionary<string,string> _memory=[];
 private string Read(string key,string fallback) { if(app==null)return _memory.GetValueOrDefault(key,fallback);using var reg=app.BaseRegKey.OpenSubKey("Publisher");return reg?.GetValue(key) as string is {Length:>0} s?s:fallback; }
 private void Write(string key,string value) { if(app==null) {_memory[key]=value;return;}using var reg=app.BaseRegKey.CreateSubKey("Publisher");reg.SetValue(key,value,RegistryValueKind.String); }
 public string Profiles { get=>Read(nameof(Profiles),DefaultProfiles);set=>Write(nameof(Profiles),Path.GetFullPath(value)); }
 public string Logs { get=>Read(nameof(Logs),DefaultLogs);set=>Write(nameof(Logs),Path.GetFullPath(value)); }
 public string Work { get=>Read(nameof(Work),DefaultWork);set=>Write(nameof(Work),Path.GetFullPath(value)); }
 public string LastProfile { get=>Read(nameof(LastProfile),"");set=>Write(nameof(LastProfile),value); }
}
public static class PublishPaths {
 public static string Inside(string root,string relative) {
  var full=Path.GetFullPath(Path.Combine(root,relative));var parent=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
  if(!full.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Path must stay below the publisher directory.");
  RejectLinks(full);return full;
 }
 public static void RejectLinks(string path) {
  for(string? p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p)) {
   try { if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) throw new IOException("Publisher paths must not contain symbolic links/reparse points."); }
   catch(FileNotFoundException) {} catch(DirectoryNotFoundException) {}
  }
 }
 public static void ValidateTree(string root) {
  RejectLinks(root);var pending=new Stack<string>();pending.Push(root);
  while(pending.Count>0)foreach(var entry in System.IO.Directory.EnumerateFileSystemEntries(pending.Pop())) {
   RejectLinks(entry);if((File.GetAttributes(entry)&FileAttributes.Directory)!=0)pending.Push(entry);
  }
 }
 public static void Atomic(string file,string text) {
  RejectLinks(file);Directory.CreateDirectory(Path.GetDirectoryName(file)!);var temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
  try { using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { var bytes=Encoding.UTF8.GetBytes(text);stream.Write(bytes);stream.Flush(true); } File.Move(temp,file,true); }
  finally { if(File.Exists(temp))File.Delete(temp); }
 }
 public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
public sealed record ProfileEntry(string File,PublishProfile? Profile,string? Hash,string? Error) {
 public override string ToString()=>Profile?.Name??$"{Path.GetFileName(File)} · {Error}";
}
public sealed class ProfileConflictException() : IOException("Profile changed outside the application. Reload or explicitly overwrite.");
public sealed class ProfileStore(string root) {
 public string Root=>Path.GetFullPath(root);
 public IReadOnlyList<ProfileEntry> List(PublishKind kind) {
  var dir=PublishPaths.Inside(Root,kind.ToString());Directory.CreateDirectory(dir);
  return Directory.EnumerateFiles(dir,"*.json").Select(Read).OrderBy(x=>x.Profile?.Name??x.File).ToArray();
 }
 public ProfileEntry Read(string file)=>Read(file,null);
 /// <summary>Membaca profil; berkas bundle terenkripsi (<see cref="ProfileBundle"/>) dibuka dengan <paramref name="passphrase"/>.</summary>
 public ProfileEntry Read(string file,string? passphrase) {
  try { PublishPaths.RejectLinks(file);var text=File.ReadAllText(file);if(ProfileBundle.IsBundleFile(file))text=ProfileBundle.Decrypt(text,passphrase??"");var p=ProfileJson.Read(text);if(!Path.IsPathFullyQualified(p.Workspace)&&p.Workspace.Length>0)p.Workspace=Path.GetFullPath(p.Workspace,Path.GetDirectoryName(Path.GetFullPath(file))!);return new(file,p,PublishPaths.Hash(file),null); }
  catch(Exception ex) when(ex is IOException or System.Text.Json.JsonException or InvalidDataException or UnauthorizedAccessException) { return new(file,null,null,ex.Message); }
 }
 public ProfileEntry Save(PublishProfile profile,ProfileEntry? previous=null,bool overwrite=false) {
  profile.Validate();
  if(profile.SensitiveDataStorage==SensitiveDataStorage.Separate && profile.Credentials.Any(c=>!string.IsNullOrEmpty(c.Secret))) throw new InvalidDataException("Move secrets to separate storage before saving.");
  var file=PublishPaths.Inside(Root,Path.Combine(profile.Kind.ToString(),profile.Id+".json"));
  if(File.Exists(file)&&!overwrite&&(previous?.File!=file || previous.Hash!=PublishPaths.Hash(file)))throw new ProfileConflictException();
  PublishPaths.Atomic(file,ProfileJson.Write(profile));return Read(file);
 }
 public ProfileEntry Import(string file,bool newId=false)=>Import(file,newId,null,null);
 /// <summary>
 /// Mengimpor profil. Secret yang ikut dalam berkas (export plain text atau bundle terenkripsi) dipulihkan: profil
 /// Plaintext menyimpannya inline, profil Separate memasukkannya ke <paramref name="secrets"/> (dengan
 /// <paramref name="rememberSecrets"/>, terenkripsi DPAPI untuk akun ini) sehingga profil langsung bisa dipakai.
 /// </summary>
 public ProfileEntry Import(string file,bool newId,string? passphrase,PublishSecretStore? secrets,bool rememberSecrets=true) {
  var source=Read(file,passphrase);if(source.Profile is not {} p)throw new InvalidDataException(source.Error);
  if(newId)p.Id=Guid.NewGuid().ToString("N");
  // Checked before any secret is stored, so a conflicting import leaves nothing behind.
  if(File.Exists(PublishPaths.Inside(Root,Path.Combine(p.Kind.ToString(),p.Id+".json"))))throw new ProfileConflictException();
  // Imported references must never access another profile's remembered credentials.
  var refs=new Dictionary<string,string>();var restore=new List<(PublishCredential Credential,string Secret)>();
  foreach(var c in p.Credentials) {
   var old=c.Id;c.Id=Guid.NewGuid().ToString("N");refs[old]=c.Id;c.SecretRef=null;
   if(p.SensitiveDataStorage==SensitiveDataStorage.Separate) {
    if(secrets!=null&&!string.IsNullOrEmpty(c.Secret)&&!string.IsNullOrWhiteSpace(c.ScopeHost))restore.Add((c,c.Secret));
    c.Secret=null;
   }
  }
  if(p.Container!=null)foreach(var secret in p.Container.Dockerfile.Secrets)if(refs.TryGetValue(secret.CredentialRef,out var id))secret.CredentialRef=id;
  foreach(var (credential,secret) in restore)secrets!.Put(credential,secret,rememberSecrets);
  return Save(p);
 }
 public ProfileEntry Duplicate(PublishProfile source) {
  var p=source.Clone();p.Id=Guid.NewGuid().ToString("N");p.Name+=" copy";
  var refs=new Dictionary<string,string>();foreach(var c in p.Credentials) { var old=c.Id;c.Id=Guid.NewGuid().ToString("N");refs[old]=c.Id;c.SecretRef=null;if(p.SensitiveDataStorage==SensitiveDataStorage.Separate)c.Secret=null; }
  if(p.Container!=null)foreach(var secret in p.Container.Dockerfile.Secrets)if(refs.TryGetValue(secret.CredentialRef,out var id))secret.CredentialRef=id;
  return Save(p);
 }
 public PublishProfile Load(string id,PublishKind kind=PublishKind.Container) {
  if(!Guid.TryParse(id,out _))throw new InvalidDataException("Invalid profile ID.");return Read(PublishPaths.Inside(Root,Path.Combine(kind.ToString(),id+".json"))).Profile??throw new InvalidDataException("Profile is unavailable.");
 }
 public void Delete(ProfileEntry entry) {
  var relative=Path.GetRelativePath(Root,entry.File);File.Delete(PublishPaths.Inside(Root,relative));
 }
 private static PublishProfile MakePortable(PublishProfile profile) {
  var p=profile.Clone();var workspace=Path.GetFullPath(p.Workspace);
  string Portable(string value) {
   if(!Path.IsPathFullyQualified(value))return value;
   var rel=Path.GetRelativePath(workspace,value);return rel==".."||rel.StartsWith(".."+Path.DirectorySeparatorChar)?"":rel;
  }
  if(p.NuGet!=null)foreach(var source in p.NuGet.Sources) {source.Path=Portable(source.Path);source.Projects=source.Projects.Select(Portable).ToList();}
  if(p.Container is {} c) {c.Dockerfile.Context=Portable(c.Dockerfile.Context);c.Dockerfile.File=Portable(c.Dockerfile.File);foreach(var ctx in c.Dockerfile.NamedContexts)ctx.Value=Portable(ctx.Value);c.Template.Project=Portable(c.Template.Project);c.Template.ExistingDockerfile=Portable(c.Template.ExistingDockerfile);c.Template.PublishProfile=Portable(c.Template.PublishProfile);c.Compose.File=Portable(c.Compose.File);c.Compose.ProjectDirectory=Portable(c.Compose.ProjectDirectory);}
  p.Workspace=".";return p;
 }
 /// <summary>Export tanpa secret, atau dengan secret inline sebagai plain text.</summary>
 public void Export(PublishProfile profile,string file,bool sensitive=false)=>Export(profile,file,sensitive?ExportSecrets.PlainText:ExportSecrets.None);
 /// <summary>
 /// Menulis profil ke <paramref name="file"/> dengan path yang portabel. Dengan <see cref="ExportSecrets.PlainText"/> atau
 /// <see cref="ExportSecrets.Encrypted"/>, secret ikut: yang inline diambil dari profil, yang terpisah dibaca dari
 /// <paramref name="secrets"/> (sesi atau Remember). Pada mode Encrypted isi berkas dienkripsi dengan
 /// <paramref name="passphrase"/>; pada PlainText siapa pun yang membuka berkas bisa membaca secret-nya.
 /// Mengembalikan jumlah kredensial yang tidak punya secret tersimpan sehingga diekspor kosong.
 /// </summary>
 public int Export(PublishProfile profile,string file,ExportSecrets mode,PublishSecretStore? secrets=null,string? passphrase=null) {
  if(mode==ExportSecrets.Encrypted&&string.IsNullOrEmpty(passphrase))throw new InvalidDataException("A passphrase is required to encrypt the export.");
  var found=profile.Credentials.Select(c=>mode==ExportSecrets.None?null:c.Secret??secrets?.Get(c,c.ScopeHost)).ToList();
  var p=MakePortable(profile);var missing=0;
  for(var i=0;i<p.Credentials.Count;i++) {
   p.Credentials[i].SecretRef=null;p.Credentials[i].Secret=found[i];
   if(mode!=ExportSecrets.None&&found[i]==null)missing++;
  }
  var json=ProfileJson.Write(p);
  PublishPaths.Atomic(file,mode==ExportSecrets.Encrypted?ProfileBundle.Encrypt(json,passphrase!):json);
  return missing;
 }
}
/// <summary>Apakah secret ikut diekspor, dan bagaimana berkasnya dilindungi.</summary>
public enum ExportSecrets { None, PlainText, Encrypted }
public sealed class PublishSecretStore {
 private readonly Dictionary<string,(string Host,string Secret)> _session=new();
 private static string DirectoryPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Em","Publish","Secrets");
 private static string FilePath(string id) {if(!Guid.TryParse(id,out _))throw new InvalidDataException("Invalid credential reference.");return PublishPaths.Inside(DirectoryPath,id+".bin");}
 public static string Host(string target) { if(Uri.TryCreate(target,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https")return uri.Authority.ToLowerInvariant();return target.Trim().TrimEnd('/').ToLowerInvariant(); }
 public void Put(PublishCredential credential,string secret,bool remember) {
  if(string.IsNullOrWhiteSpace(credential.ScopeHost))throw new InvalidDataException("Credential host scope is required.");
  credential.SecretRef=credential.Id;_session[credential.Id]=(Host(credential.ScopeHost),secret);
  if(remember) {var file=FilePath(credential.Id);Directory.CreateDirectory(DirectoryPath);var data=Encoding.UTF8.GetBytes(ProfileJson.Write(new SecretRecord(Host(credential.ScopeHost),secret)));File.WriteAllBytes(file,ProtectedData.Protect(data,null,DataProtectionScope.CurrentUser));}
  else {var file=FilePath(credential.Id);if(File.Exists(file))File.Delete(file);}
 }
 public bool IsRemembered(PublishCredential credential)=>credential.SecretRef==credential.Id&&File.Exists(FilePath(credential.Id));
 public string? Get(PublishCredential credential,string target) {
  var host=Host(target);if(Host(credential.ScopeHost)!=host)return null;
  if(credential.Secret!=null)return credential.Secret;
  if(credential.SecretRef!=credential.Id)return null;
  if(_session.TryGetValue(credential.Id,out var item))return item.Host==host?item.Secret:null;
  try {var bytes=ProtectedData.Unprotect(File.ReadAllBytes(FilePath(credential.Id)),null,DataProtectionScope.CurrentUser);var data=System.Text.Json.JsonSerializer.Deserialize<SecretRecord>(bytes,ProfileJson.Options);return data?.Host==host?data.Secret:null;}
  catch(Exception ex) when(ex is IOException or CryptographicException or UnauthorizedAccessException or System.Text.Json.JsonException) {return null;}
 }
 public void ConvertMode(PublishProfile p,SensitiveDataStorage mode) {
  foreach(var c in p.Credentials) {
   if(mode==SensitiveDataStorage.Separate) {if(c.Secret!=null)Put(c,c.Secret,c.Remember);c.Secret=null;}
   else {c.Secret=Get(c,c.ScopeHost);c.NeedsSecret=c.Secret==null;c.SecretRef=null;}
  }
  p.SensitiveDataStorage=mode;
 }
 private sealed record SecretRecord(string Host,string Secret);
}
public sealed class RunWorkspace : IDisposable {
 public string Root { get; }
 public string Directory { get; }
 private readonly bool _keep;
 public RunWorkspace(string root,string runId,bool keep) {
  if(!Guid.TryParse(runId,out _))throw new ArgumentException("Invalid run ID.");Root=Path.GetFullPath(root);Directory=PublishPaths.Inside(Root,runId);_keep=keep;
  if(System.IO.Directory.Exists(Directory))throw new IOException("Run workspace already exists.");System.IO.Directory.CreateDirectory(Directory);File.WriteAllText(Path.Combine(Directory,".em-publish-run"),runId);
 }
 public string PathFor(string name)=>PublishPaths.Inside(Directory,name);
 public void Dispose() {
  if(_keep)return;var path=PublishPaths.Inside(Root,Path.GetFileName(Directory));
  if(!File.Exists(Path.Combine(path,".em-publish-run")))throw new IOException("Workspace ownership marker missing.");
  PublishPaths.ValidateTree(path);
  System.IO.Directory.Delete(path,true);
 }
}
