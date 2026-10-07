using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Em.Ui.Wpf.Publish;

/// <summary>The publish kind.</summary>
public enum PublishKind
{
   /// <summary>Publishing NuGet packages.</summary>
   NuGet,
   /// <summary>Publishing container images.</summary>
   Container,
}
/// <summary>The sensitive data storage.</summary>
public enum SensitiveDataStorage
{
   /// <summary>Secrets are kept apart from the profile, in the secret store.</summary>
   Separate,
   /// <summary>Secrets are stored inline in the profile file as plain text.</summary>
   Plaintext,
}
/// <summary>The target type.</summary>
public enum TargetType
{
   /// <summary>The built-in registry or feed of the connected server.</summary>
   BuiltIn,
   /// <summary>An address typed by the user.</summary>
   Custom,
}
/// <summary>The container mode.</summary>
public enum ContainerMode
{
   /// <summary>Build from a Dockerfile.</summary>
   Dockerfile,
   /// <summary>Push an image that already exists locally.</summary>
   LocalImage,
   /// <summary>Build from a template of files.</summary>
   Template,
   /// <summary>Build the services of a compose file.</summary>
   Compose,
   /// <summary>Publish a set of images.</summary>
   Set,
}
/// <summary>The duplicate handling.</summary>
public enum DuplicateHandling
{
   /// <summary>A version that already exists is skipped.</summary>
   Skip,
   /// <summary>A version that already exists fails the run.</summary>
   Fail,
}
/// <summary>The base selection.</summary>
public enum BaseSelection
{
   /// <summary>The base image comes from this run.</summary>
   FromThisRun,
   /// <summary>The base image is the last published one.</summary>
   LastPublished,
   /// <summary>The base image is an explicit reference.</summary>
   ExplicitReference,
}
/// <summary>The missing entry.</summary>
public enum MissingEntry
{
   /// <summary>A missing entry is an error.</summary>
   Error,
   /// <summary>A missing entry is only a warning.</summary>
   Warning,
}
/// <summary>The file list mode.</summary>
public enum FileListMode
{
   /// <summary>Only the listed files are included.</summary>
   IncludeOnly,
   /// <summary>The listed files are excluded.</summary>
   Exclude,
}
/// <summary>The dockerfile source.</summary>
public enum DockerfileSource
{
   /// <summary>The Dockerfile is generated.</summary>
   Generated,
   /// <summary>The Dockerfile is an existing file.</summary>
   ExistingFile,
}
/// <summary>The publish source.</summary>
public enum PublishSource
{
   /// <summary>The source is described by the fields of the profile.</summary>
   Fields,
   /// <summary>The source is a publish profile of the project.</summary>
   PublishProfile,
}
/// <summary>How a container profile names its image: Standard = A.B.C + channel with an automatic build number and
/// floating tags (container-naming convention); Manual = a free tag typed on the publish strip, no floating tags.</summary>
/// <summary>The tagging mode.</summary>
public enum TaggingMode
{
   /// <summary>A.B.C plus channel with an automatic build number and floating tags.</summary>
   Standard,
   /// <summary>A free tag typed on the publish strip, with no floating tags.</summary>
   Manual,
}
/// <summary>A publish profile: what to build, where to publish it, and how.</summary>
public sealed class PublishProfile
{
 /// <summary>The format version.</summary>
 public int FormatVersion { get; set; } = 1;
 /// <summary>The id.</summary>
 public string Id { get; set; } = Guid.NewGuid().ToString("N");
 /// <summary>The name.</summary>
 public string Name { get; set; } = "New profile";
 /// <summary>The kind.</summary>
 public PublishKind Kind { get; set; }
 /// <summary>The description.</summary>
 public string Description { get; set; } = "";
 /// <summary>The workspace.</summary>
 public string Workspace { get; set; } = "";
 /// <summary>The sensitive data storage.</summary>
 public SensitiveDataStorage SensitiveDataStorage { get; set; }
 /// <summary>The nu get.</summary>
 public NuGetProfile? NuGet { get; set; }
 /// <summary>The container.</summary>
 public ContainerProfile? Container { get; set; }
 /// <summary>The credentials.</summary>
 public List<PublishCredential> Credentials { get; set; } = [];
 /// <summary>Indicates keep workspace.</summary>
 public bool KeepWorkspace { get; set; }
 /// <summary>Indicates require release notes.</summary>
 public bool RequireReleaseNotes { get; set; }
 /// <summary>Creates a new profile of a kind with default values.</summary>
 public static PublishProfile Create(PublishKind kind) => new() { Kind=kind, NuGet=kind==PublishKind.NuGet?new():null, Container=kind==PublishKind.Container?new():null };
 /// <summary>Makes a deep copy of the profile.</summary>
 public PublishProfile Clone() => ProfileJson.Read(ProfileJson.Write(this));
 /// <summary>Resolves a path relative to the profile workspace.</summary>
 public string Resolve(string path) => Path.GetFullPath(path,Path.GetFullPath(Workspace));
 /// <summary>Validates the profile, throwing when a value is not acceptable.</summary>
 public void Validate() {
  if(FormatVersion!=1) throw new InvalidDataException("Unsupported profile format version.");
  if(!Guid.TryParse(Id,out _)) throw new InvalidDataException("Profile ID must be a GUID.");
  if(string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Profile name is required.");
  if(!Enum.IsDefined(Kind)||!Enum.IsDefined(SensitiveDataStorage)) throw new InvalidDataException("Use NuGet/Container and Separate/Plaintext; encrypted JSON is not supported.");
  if(Kind==PublishKind.NuGet && NuGet is null || Kind==PublishKind.Container && Container is null) throw new InvalidDataException("Missing profile settings.");
  foreach(var c in Credentials) { if(!Guid.TryParse(c.Id,out _)) throw new InvalidDataException("Credential ID must be a GUID."); }
 }
}
/// <summary>The publish credential.</summary>
public sealed class PublishCredential {
 /// <summary>The id.</summary>
 public string Id { get; set; } = Guid.NewGuid().ToString("N");
 /// <summary>The purpose.</summary>
 public string Purpose { get; set; } = "push";
 /// <summary>The scope host.</summary>
 public string ScopeHost { get; set; } = "";
 /// <summary>The username.</summary>
 public string Username { get; set; } = "";
 /// <summary>The secret ref.</summary>
 public string? SecretRef { get; set; }
 /// <summary>The secret.</summary>
 public string? Secret { get; set; }
 /// <summary>
 /// Plain HTTP (no TLS) for this host. Applies to the publisher's own registry requests (verify, tag discovery).
 /// Docker's own push is decided by the daemon: the host must also be listed under insecure-registries there.
 /// </summary>
 public bool AllowHttp { get; set; }
 /// <summary>Whether the user chose to remember the secret.</summary>
 [JsonIgnore] public bool Remember { get; set; }
 /// <summary>Whether a secret is still needed before this profile can be used.</summary>
 [JsonIgnore] public bool NeedsSecret { get; set; }
}
/// <summary>The build property.</summary>
public sealed class BuildProperty {
   /// <summary>The property name.</summary>
   public string Key { get; set; }="";
   /// <summary>The property value.</summary>
   public string Value { get; set; }=""; }
/// <summary>The NuGet source.</summary>
public sealed class NuGetSource {
   /// <summary>Path of the solution or folder to pack.</summary>
   public string Path { get; set; }="";
   /// <summary>Whether every packable project is included.</summary>
   public bool SelectAllPackable { get; set; }=true;
   /// <summary>The projects chosen when not every packable project is included.</summary>
   public List<string> Projects { get; set; }=[]; }
/// <summary>The nu get target.</summary>
public sealed class NuGetTarget {
 /// <summary>The type.</summary>
 public TargetType Type { get; set; }
 /// <summary>The server.</summary>
 public string Server { get; set; }="";
 /// <summary>The feed.</summary>
 public string Feed { get; set; }="";
 /// <summary>The service index.</summary>
 public string ServiceIndex { get; set; }="";
}
/// <summary>The nu get profile.</summary>
public sealed class NuGetProfile {
 /// <summary>The sources.</summary>
 public List<NuGetSource> Sources { get; set; }=[];
 /// <summary>The configuration.</summary>
 public string Configuration { get; set; }="Release";
 /// <summary>The version override.</summary>
 public string VersionOverride { get; set; }="";
 /// <summary>The msbuild properties.</summary>
 public List<BuildProperty> MsbuildProperties { get; set; }=[];
 /// <summary>The target.</summary>
 public NuGetTarget Target { get; set; }=new();
 /// <summary>The duplicate handling.</summary>
 public DuplicateHandling DuplicateHandling { get; set; }
}
/// <summary>The container target.</summary>
public sealed class ContainerTarget {
 /// <summary>The type.</summary>
 public TargetType Type { get; set; }
 /// <summary>The server.</summary>
 public string Server { get; set; }="";
 /// <summary>The root.</summary>
 public string Root { get; set; }="";
 /// <summary>The container.</summary>
 public string Container { get; set; }="";
 /// <summary>The host.</summary>
 public string Host { get; set; }="";
 /// <summary>The repository.</summary>
 public string Repository { get; set; }="";
 /// <summary>Chosen in the profile; decides what the publish strip shows (spin edits + channel, or one tag box).</summary>
 public TaggingMode Tagging { get; set; }
 /// <summary>The version tag, set from the publish strip (A.B.C + channel, or a manual tag), not from the profile dialog.</summary>
 public string VersionTag { get; set; }="";
}
/// <summary>The build secret.</summary>
public sealed class BuildSecret {
   /// <summary>The id of the secret as the build refers to it.</summary>
   public string Id { get; set; }="";
   /// <summary>The reference to the credential that holds its value.</summary>
   public string CredentialRef { get; set; }=""; }
/// <summary>The dockerfile profile.</summary>
public sealed class DockerfileProfile {
 /// <summary>The context.</summary>
 public string Context { get; set; }=".";
 /// <summary>The file.</summary>
 public string File { get; set; }="Dockerfile";
 /// <summary>The target.</summary>
 public string Target { get; set; }="";
 /// <summary>The platform.</summary>
 public string Platform { get; set; }="";
 /// <summary>The build args.</summary>
 public List<BuildProperty> BuildArgs { get; set; }=[];
 /// <summary>The named contexts.</summary>
 public List<BuildProperty> NamedContexts { get; set; }=[];
 /// <summary>The secrets.</summary>
 public List<BuildSecret> Secrets { get; set; }=[];
}
/// <summary>The file set.</summary>
public sealed class FileSet {
 /// <summary>The include.</summary>
 public List<string> Include { get; set; }=["**/*"];
 /// <summary>The exclude.</summary>
 public List<string> Exclude { get; set; }=[];
 /// <summary>Indicates exclude debug symbols.</summary>
 public bool ExcludeDebugSymbols { get; set; }=true;
 /// <summary>The required files.</summary>
 public List<string> RequiredFiles { get; set; }=[];
 /// <summary>The named list.</summary>
 public string NamedList { get; set; }="";
 /// <summary>The list mode.</summary>
 public FileListMode ListMode { get; set; }
}
/// <summary>The named file list.</summary>
public sealed class NamedFileList {
 /// <summary>The name.</summary>
 public string Name { get; set; }="Module files";
 /// <summary>The entries.</summary>
 public List<string> Entries { get; set; }=[];
 /// <summary>The missing entry.</summary>
 public MissingEntry MissingEntry { get; set; }
}
/// <summary>The template profile.</summary>
public sealed class TemplateProfile {
 /// <summary>The project.</summary>
 public string Project { get; set; }="";
 /// <summary>The configuration.</summary>
 public string Configuration { get; set; }="Release";
 /// <summary>The publish source.</summary>
 public PublishSource PublishSource { get; set; }
 /// <summary>The publish profile.</summary>
 public string PublishProfile { get; set; }="";
 /// <summary>The runtime.</summary>
 public string Runtime { get; set; }="linux-x64";
 /// <summary>The framework.</summary>
 public string Framework { get; set; }="";
 /// <summary>Indicates self contained.</summary>
 public bool SelfContained { get; set; }
 /// <summary>The base image.</summary>
 public string BaseImage { get; set; }="mcr.microsoft.com/dotnet/aspnet:10.0";
 /// <summary>Indicates use set base.</summary>
 public bool UseSetBase { get; set; }
 /// <summary>The base selection.</summary>
 public BaseSelection BaseSelection { get; set; }
 /// <summary>The base profile id.</summary>
 public string BaseProfileId { get; set; }="";
 /// <summary>The explicit base reference.</summary>
 public string ExplicitBaseReference { get; set; }="";
 /// <summary>The entrypoint.</summary>
 public List<string> Entrypoint { get; set; }=[];
 /// <summary>The working directory.</summary>
 public string WorkingDirectory { get; set; }="/app";
 /// <summary>The ports.</summary>
 public List<string> Ports { get; set; }=[];
 /// <summary>The environment.</summary>
 public List<BuildProperty> Environment { get; set; }=[];
 /// <summary>The file set.</summary>
 public FileSet FileSet { get; set; }=new();
 /// <summary>The dockerfile source.</summary>
 public DockerfileSource DockerfileSource { get; set; }
 /// <summary>The existing dockerfile.</summary>
 public string ExistingDockerfile { get; set; }="";
 /// <summary>The staging subfolder.</summary>
 public string StagingSubfolder { get; set; }="";
 /// <summary>The run before copy.</summary>
 public List<string> RunBeforeCopy { get; set; }=[];
 /// <summary>The run after copy.</summary>
 public List<string> RunAfterCopy { get; set; }=[];
 /// <summary>Indicates mark entrypoint executable.</summary>
 public bool MarkEntrypointExecutable { get; set; }
 /// <summary>The time zone.</summary>
 public string TimeZone { get; set; }="";
}
/// <summary>The compose service.</summary>
public sealed class ComposeService {
 /// <summary>The service.</summary>
 public string Service { get; set; }="";
 /// <summary>The repository.</summary>
 public string Repository { get; set; }="";
 /// <summary>The version tag.</summary>
 public string VersionTag { get; set; }="";
}
/// <summary>The compose profile.</summary>
public sealed class ComposeProfile {
 /// <summary>The file.</summary>
 public string File { get; set; }="compose.yml";
 /// <summary>The project directory.</summary>
 public string ProjectDirectory { get; set; }=".";
 /// <summary>The services.</summary>
 public List<ComposeService> Services { get; set; }=[];
}
/// <summary>The set step.</summary>
public sealed class SetStep {
 /// <summary>The profile id.</summary>
 public string ProfileId { get; set; }="";
 /// <summary>Indicates build.</summary>
 public bool Build { get; set; }=true;
 /// <summary>Indicates push.</summary>
 public bool Push { get; set; }=true;
}
/// <summary>The publish set.</summary>
public sealed class PublishSet {
 /// <summary>The steps.</summary>
 public List<SetStep> Steps { get; set; }=[];
 /// <summary>The file lists.</summary>
 public List<NamedFileList> FileLists { get; set; }=[];
 /// <summary>Indicates share publish output.</summary>
 public bool SharePublishOutput { get; set; }=true;
}
/// <summary>The container profile.</summary>
public sealed class ContainerProfile {
 /// <summary>The mode.</summary>
 public ContainerMode Mode { get; set; }
 /// <summary>The dockerfile.</summary>
 public DockerfileProfile Dockerfile { get; set; }=new();
 /// <summary>The local image.</summary>
 public string LocalImage { get; set; }="";
 /// <summary>The template.</summary>
 public TemplateProfile Template { get; set; }=new();
 /// <summary>The compose.</summary>
 public ComposeProfile Compose { get; set; }=new();
 /// <summary>The set.</summary>
 public PublishSet Set { get; set; }=new();
 /// <summary>The target.</summary>
 public ContainerTarget Target { get; set; }=new();
 /// <summary>Indicates use my docker login.</summary>
 public bool UseMyDockerLogin { get; set; }
 /// <summary>After a push to the Built-in registry, asks the server to deploy each pushed image to the container's deploy target
 /// (Container Manager). On by default, also for profiles saved before this option existed; a failed deploy does not fail the publish.</summary>
 public bool AutoDeploy { get; set; }=true;
}
/// <summary>The profile json.</summary>
public static class ProfileJson {
 /// <summary>The options.</summary>
 public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy=JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive=true, WriteIndented=true, Converters={new JsonStringEnumConverter(allowIntegerValues:false)} };
 /// <summary>Serializes a value with the profile JSON options.</summary>
 public static string Write<T>(T value) => JsonSerializer.Serialize(value,Options);
 /// <summary>Reads a profile from JSON.</summary>
 public static PublishProfile Read(string json) { try { var p=JsonSerializer.Deserialize<PublishProfile>(json,Options)??throw new InvalidDataException("Profile is null.");p.Validate();return p; } catch(JsonException ex) {throw new InvalidDataException("Invalid profile JSON. Use supported enum values (Separate/Plaintext) and format version 1. "+ex.Message,ex);} }
}
