using System.Text.RegularExpressions;

namespace Em.Ui.Wpf.Publish;

/// <summary>
/// The version tag of a container image as the publish strip edits it: <c>MAJOR.MINOR.PATCH</c> plus a channel
/// (doc/convention/container-naming.md). Release is the bare <c>A.B.C</c>; every other channel is
/// <c>A.B.C-channel.N</c>, and N is not typed but taken from the tags the registry already holds.
/// </summary>
public readonly record struct ContainerVersion(int Major,int Minor,int Patch,string Channel) {
 public const string Release="release";
 public static readonly string[] Channels=["prealpha","alpha","beta","rc",Release];
 /// <summary>The first version of a new image, as the convention asks (not 0.0.0).</summary>
 public static readonly ContainerVersion First=new(0,1,0,"prealpha");
 private static readonly Regex Pattern=new(@"^(\d{1,4})\.(\d{1,4})\.(\d{1,4})(?:-(prealpha|alpha|beta|rc)\.(\d+))?$",RegexOptions.CultureInvariant);
 public bool IsRelease=>Channel==Release;
 public string Core=>$"{Major}.{Minor}.{Patch}";
 /// <summary>False for tags the strip does not manage (a custom tag such as <c>dev</c>); those are left alone.</summary>
 public static bool TryParse(string? tag,out ContainerVersion version) {
  var m=Pattern.Match(tag??"");
  version=m.Success?new(int.Parse(m.Groups[1].Value),int.Parse(m.Groups[2].Value),int.Parse(m.Groups[3].Value),m.Groups[4].Success?m.Groups[4].Value:Release):First;
  return m.Success;
 }
 public string Tag(int number)=>IsRelease?Core:$"{Core}-{Channel}.{number}";
 /// <summary>One above the highest N already taken for this target version and channel; 1 when there is none.</summary>
 public int NextNumber(IEnumerable<string> existing) {
  if(IsRelease)return 0;
  var prefix=$"{Core}-{Channel}.";var highest=0;
  foreach(var tag in existing)if(tag.StartsWith(prefix,StringComparison.Ordinal)&&int.TryParse(tag.AsSpan(prefix.Length),out var n)&&n>highest)highest=n;
  return highest+1;
 }
}
