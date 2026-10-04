using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Em.Ui.Wpf.Publish;

public enum PublishKind { NuGet, Container }
public enum SensitiveDataStorage { Separate, Plaintext }
public enum TargetType { BuiltIn, Custom }
public enum ContainerMode { Dockerfile, LocalImage, Template, Compose, Set }
public enum DuplicateHandling { Skip, Fail }
public enum BaseSelection { FromThisRun, LastPublished, ExplicitReference }
public enum MissingEntry { Error, Warning }
public enum FileListMode { IncludeOnly, Exclude }
public enum DockerfileSource { Generated, ExistingFile }
public enum PublishSource { Fields, PublishProfile }
public sealed class PublishProfile
{
 public int FormatVersion { get; set; } = 1;
 public string Id { get; set; } = Guid.NewGuid().ToString("N");
 public string Name { get; set; } = "New profile";
 public PublishKind Kind { get; set; }
 public string Description { get; set; } = "";
 public string Workspace { get; set; } = "";
 public SensitiveDataStorage SensitiveDataStorage { get; set; }
 public NuGetProfile? NuGet { get; set; }
 public ContainerProfile? Container { get; set; }
 public List<PublishCredential> Credentials { get; set; } = [];
 public bool KeepWorkspace { get; set; }
 public bool RequireReleaseNotes { get; set; }
 public static PublishProfile Create(PublishKind kind) => new() { Kind=kind, NuGet=kind==PublishKind.NuGet?new():null, Container=kind==PublishKind.Container?new():null };
 public PublishProfile Clone() => ProfileJson.Read(ProfileJson.Write(this));
 public string Resolve(string path) => Path.GetFullPath(path,Path.GetFullPath(Workspace));
 public void Validate() {
  if(FormatVersion!=1) throw new InvalidDataException("Unsupported profile format version.");
  if(!Guid.TryParse(Id,out _)) throw new InvalidDataException("Profile ID must be a GUID.");
  if(string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Profile name is required.");
  if(!Enum.IsDefined(Kind)||!Enum.IsDefined(SensitiveDataStorage)) throw new InvalidDataException("Use NuGet/Container and Separate/Plaintext; encrypted JSON is not supported.");
  if(Kind==PublishKind.NuGet && NuGet is null || Kind==PublishKind.Container && Container is null) throw new InvalidDataException("Missing profile settings.");
  foreach(var c in Credentials) { if(!Guid.TryParse(c.Id,out _)) throw new InvalidDataException("Credential ID must be a GUID."); }
 }
}
public sealed class PublishCredential {
 public string Id { get; set; } = Guid.NewGuid().ToString("N");
 public string Purpose { get; set; } = "push";
 public string ScopeHost { get; set; } = "";
 public string Username { get; set; } = "";
 public string? SecretRef { get; set; }
 public string? Secret { get; set; }
 [JsonIgnore] public bool Remember { get; set; }
 [JsonIgnore] public bool NeedsSecret { get; set; }
}
public sealed class BuildProperty { public string Key { get; set; }=""; public string Value { get; set; }=""; }
public sealed class NuGetSource { public string Path { get; set; }=""; public bool SelectAllPackable { get; set; }=true; public List<string> Projects { get; set; }=[]; }
public sealed class NuGetTarget {
 public TargetType Type { get; set; }
 public string Server { get; set; }="";
 public string Feed { get; set; }="";
 public string ServiceIndex { get; set; }="";
}
public sealed class NuGetProfile {
 public List<NuGetSource> Sources { get; set; }=[];
 public string Configuration { get; set; }="Release";
 public string VersionOverride { get; set; }="";
 public List<BuildProperty> MsbuildProperties { get; set; }=[];
 public NuGetTarget Target { get; set; }=new();
 public DuplicateHandling DuplicateHandling { get; set; }
}
public sealed class ContainerTarget {
 public TargetType Type { get; set; }
 public string Server { get; set; }="";
 public string Root { get; set; }="";
 public string Container { get; set; }="";
 public string Host { get; set; }="";
 public string Repository { get; set; }="";
 public string VersionTag { get; set; }="";
 public List<string> ExtraTags { get; set; }=[];
}
public sealed class BuildSecret { public string Id { get; set; }=""; public string CredentialRef { get; set; }=""; }
public sealed class DockerfileProfile {
 public string Context { get; set; }=".";
 public string File { get; set; }="Dockerfile";
 public string Target { get; set; }="";
 public string Platform { get; set; }="";
 public List<BuildProperty> BuildArgs { get; set; }=[];
 public List<BuildProperty> NamedContexts { get; set; }=[];
 public List<BuildSecret> Secrets { get; set; }=[];
}
public sealed class FileSet {
 public List<string> Include { get; set; }=["**/*"];
 public List<string> Exclude { get; set; }=[];
 public bool ExcludeDebugSymbols { get; set; }=true;
 public List<string> RequiredFiles { get; set; }=[];
 public string NamedList { get; set; }="";
 public FileListMode ListMode { get; set; }
}
public sealed class NamedFileList {
 public string Name { get; set; }="Module files";
 public List<string> Entries { get; set; }=[];
 public MissingEntry MissingEntry { get; set; }
}
public sealed class TemplateProfile {
 public string Project { get; set; }="";
 public string Configuration { get; set; }="Release";
 public PublishSource PublishSource { get; set; }
 public string PublishProfile { get; set; }="";
 public string Runtime { get; set; }="linux-x64";
 public string Framework { get; set; }="";
 public bool SelfContained { get; set; }
 public string BaseImage { get; set; }="mcr.microsoft.com/dotnet/aspnet:10.0";
 public bool UseSetBase { get; set; }
 public BaseSelection BaseSelection { get; set; }
 public string BaseProfileId { get; set; }="";
 public string ExplicitBaseReference { get; set; }="";
 public List<string> Entrypoint { get; set; }=[];
 public string WorkingDirectory { get; set; }="/app";
 public List<string> Ports { get; set; }=[];
 public List<BuildProperty> Environment { get; set; }=[];
 public FileSet FileSet { get; set; }=new();
 public DockerfileSource DockerfileSource { get; set; }
 public string ExistingDockerfile { get; set; }="";
 public string StagingSubfolder { get; set; }="";
 public List<string> RunBeforeCopy { get; set; }=[];
 public List<string> RunAfterCopy { get; set; }=[];
 public bool MarkEntrypointExecutable { get; set; }
 public string TimeZone { get; set; }="";
}
public sealed class ComposeService {
 public string Service { get; set; }="";
 public string Repository { get; set; }="";
 public string VersionTag { get; set; }="";
}
public sealed class ComposeProfile {
 public string File { get; set; }="compose.yml";
 public string ProjectDirectory { get; set; }=".";
 public List<ComposeService> Services { get; set; }=[];
}
public sealed class SetStep {
 public string ProfileId { get; set; }="";
 public bool Build { get; set; }=true;
 public bool Push { get; set; }=true;
}
public sealed class PublishSet {
 public List<SetStep> Steps { get; set; }=[];
 public List<NamedFileList> FileLists { get; set; }=[];
 public bool SharePublishOutput { get; set; }=true;
}
public sealed class ContainerProfile {
 public ContainerMode Mode { get; set; }
 public DockerfileProfile Dockerfile { get; set; }=new();
 public string LocalImage { get; set; }="";
 public TemplateProfile Template { get; set; }=new();
 public ComposeProfile Compose { get; set; }=new();
 public PublishSet Set { get; set; }=new();
 public ContainerTarget Target { get; set; }=new();
 public bool UseMyDockerLogin { get; set; }
}
public static class ProfileJson {
 public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy=JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive=true, WriteIndented=true, Converters={new JsonStringEnumConverter(allowIntegerValues:false)} };
 public static string Write<T>(T value) => JsonSerializer.Serialize(value,Options);
 public static PublishProfile Read(string json) { try { var p=JsonSerializer.Deserialize<PublishProfile>(json,Options)??throw new InvalidDataException("Profile is null.");p.Validate();return p; } catch(JsonException ex) {throw new InvalidDataException("Invalid profile JSON. Use supported enum values (Separate/Plaintext) and format version 1. "+ex.Message,ex);} }
}
