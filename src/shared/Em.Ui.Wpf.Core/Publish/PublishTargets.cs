using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Em.Api.Core.Models;
using Em.Ui.Wpf.Core;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace Em.Ui.Wpf.Publish;

public sealed class PublishTargets(EmApp? app,PublishSecretStore secrets,string? frozenConnection=null) {
 public Task<NuPakFeedInfo[]> Feeds()=>app!.ServiceProvider.GetRequiredService<INuPakServices>().GetMeta_NuPakFeeds();
 public Task<CtnRootInfo[]> Roots()=>app!.ServiceProvider.GetRequiredService<ICtnServices>().GetMeta_CtnRoots();
 public async Task<CtnImageInfo[]> Images(string rootId)=>(await app!.ServiceProvider.GetRequiredService<ICtnServices>().GetMeta_CtnTree(rootId)).Images;
 public string Connection=>frozenConnection??app?.ActiveConnection?.Host.TrimEnd('/')??"";
 public PublishTargets Freeze()=>new(app,secrets,Connection);
 public string Scope=>Connection;
 public async Task<string> ResolveNuGet(PublishProfile p,CancellationToken ct) {
  var target=p.NuGet!.Target;
  if(target.Type==TargetType.Custom) {ValidateHttp(target.ServiceIndex);return target.ServiceIndex;}
  if(Connection.Length==0||target.Server!=Scope)throw new InvalidDataException("Built-in connection changed. Select this server and Check again.");
  var service=app!.ServiceProvider.GetRequiredService<INuPakServices>();
  var storage=await service.GetMeta_NuPakStorageStatus();if(!storage.ActiveEnabled)throw new InvalidDataException("NuGet storage is disabled; open NuGet Settings.");
  var feeds=await service.GetMeta_NuPakFeeds();var feed=feeds.SingleOrDefault(f=>f.Id==target.Feed||f.Slug==target.Feed)??throw new InvalidDataException("Feed missing. Open NuGet Manager and create/select a feed, then refresh.");
  if(!feed.EffectiveEnabled)throw new InvalidDataException("Feed is not effectively enabled. Open NuGet Manager.");
  var prefixes=await service.GetMeta_NuPakPrefixes(feed.Id);
  var reader=new ProjectReader(new PublishProcessRunner());
  foreach(var source in p.NuGet.Sources)foreach(var project in source.Projects.Count>0||!source.SelectAllPackable?source.Projects.Select(p.Resolve):await reader.Projects(p.Resolve(source.Path),ct)) {
   var info=await reader.Read(project,p.NuGet.Configuration,ct:ct);
   if(info.IsPackable&&!prefixes.Any(x=>x.Active&&info.PackageId.StartsWith(x.Name,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException($"No active prefix for {info.PackageId}. Open the Prefixes tab.");
  }
  return Connection+feed.ServiceIndex;
 }
 public async Task<string> ResolveContainer(PublishProfile p,CancellationToken ct) {
  var t=p.Container!.Target;string host,repo;
  if(t.Type==TargetType.Custom) {host=PublishSecretStore.Host(t.Host);repo=t.Repository;}
  else {
   if(Connection.Length==0||t.Server!=Scope)throw new InvalidDataException("Built-in connection changed. Select this server and Check again.");
   host=new Uri(Connection).Authority;
   var api=app!.ServiceProvider.GetRequiredService<ICtnServices>();if(!(await api.GetMeta_CtnStatus()).ActiveEnabled)throw new InvalidDataException("Registry storage disabled.");
   var roots=await api.GetMeta_CtnRoots();var root=roots.SingleOrDefault(x=>x.Name==t.Root)??throw new InvalidDataException("Root missing. Open Containers, then refresh.");
   if(!root.IsActive)throw new InvalidDataException("Registry root is inactive.");
   var tree=await api.GetMeta_CtnTree(root.Id);var image=tree.Images.SingleOrDefault(x=>x.Name==t.Container)??throw new InvalidDataException("Container missing. Open Containers, then refresh.");
   if(!image.IsActive)throw new InvalidDataException("Container is inactive.");repo=t.Root+"/"+t.Container;
  }
  if(host.Contains('/')||host.Length==0||!Regex.IsMatch(repo,@"^[a-z0-9]+(?:[._-][a-z0-9]+)*(?:/[a-z0-9]+(?:[._-][a-z0-9]+)*)*$"))throw new InvalidDataException("Registry host/repository is invalid.");
  ValidateTag(t.VersionTag);foreach(var tag in t.ExtraTags)ValidateTag(tag);return host+"/"+repo+":"+t.VersionTag;
 }
 public async Task ValidateComposeTarget(PublishProfile p,ComposeService mapping) {
  ValidateTag(mapping.VersionTag);
  if(!Regex.IsMatch(mapping.Repository,@"^[a-z0-9]+(?:[._-][a-z0-9]+)*(?:/[a-z0-9]+(?:[._-][a-z0-9]+)*)*$"))throw new InvalidDataException("Compose repository is invalid.");
  if(p.Container!.Target.Type==TargetType.BuiltIn) {
   var parts=mapping.Repository.Split('/');if(parts.Length!=2)throw new InvalidDataException("Built-in Compose repository must be root/container.");
   var root=(await Roots()).SingleOrDefault(x=>x.Name==parts[0]&&x.IsActive)??throw new InvalidDataException("Compose root missing/inactive. Open Containers.");
   if(!(await Images(root.Id)).Any(x=>x.Name==parts[1]&&x.IsActive))throw new InvalidDataException("Compose container missing/inactive. Open Containers.");
  }
 }
 public static void ValidateTag(string tag) {if(!Regex.IsMatch(tag,@"^[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,127}$",RegexOptions.CultureInvariant))throw new InvalidDataException("Version tag is required and must be a valid Docker tag.");}
 public static void ValidateHttp(string uri) {if(!Uri.TryCreate(uri,UriKind.Absolute,out var u)||u.Scheme is not ("http" or "https")||u.UserInfo.Length>0)throw new InvalidDataException("Use an HTTP(S) address without embedded credentials.");}
 public (PublishCredential Credential,string Secret) Credential(PublishProfile p,string target,string purpose="push") {
  var c=p.Credentials.FirstOrDefault(c=>c.Purpose==purpose&&PublishSecretStore.Host(c.ScopeHost)==PublishSecretStore.Host(target));
  var value=c==null?null:secrets.Get(c,target);if(c==null||string.IsNullOrEmpty(value))throw new InvalidDataException(MissingCredential(p,target,purpose,c!=null));return (c,value);
 }
 // Names the host the destination resolved to and the hosts the profile does have credentials for, because a built-in
 // destination follows the active connection and the usual cause is a credential saved for a different host.
 private static string MissingCredential(PublishProfile p,string target,string purpose,bool hasEntry) {
  var host=PublishSecretStore.Host(target);
  var others=p.Credentials.Where(x=>x.Purpose==purpose&&!string.IsNullOrWhiteSpace(x.ScopeHost)).Select(x=>PublishSecretStore.Host(x.ScopeHost)).Distinct().ToArray();
  var message=hasEntry?$"The credential for host '{host}' has no secret in this session. Enter its secret again (tick Remember to keep it)."
   :$"No credential for host '{host}'. Add one in the profile with Scope Host '{host}'.";
  return others.Length>0&&!others.Contains(host)?message+$" This profile only has credentials for: {string.Join(", ",others)}. A built-in destination follows the active connection; switch to that server, or add a credential for '{host}'.":message;
 }
 public async Task<SourceRepository> NuGetRepository(string index,(PublishCredential Credential,string Secret) credential,CancellationToken ct) {
  ValidateHttp(index);
  // Validate resource origins before handing the API key to NuGet.Protocol.
  using var handler=new HttpClientHandler {AllowAutoRedirect=false};using var http=new HttpClient(handler);
  using var request=new HttpRequestMessage(HttpMethod.Get,index);
  request.Headers.Add("X-NuGet-ApiKey",credential.Secret);
  if(credential.Credential.Username.Length>0)request.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.Credential.Username+":"+credential.Secret)));
  using var response=await http.SendAsync(request,ct);response.EnsureSuccessStatusCode();
  using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
  foreach(var resource in json.RootElement.GetProperty("resources").EnumerateArray()) {
   if(resource.TryGetProperty("@id",out var id)) {var url=id.GetString()!;ValidateHttp(url);if(PublishSecretStore.Host(url)!=PublishSecretStore.Host(index))throw new InvalidDataException("Feed resource host differs from credential scope. Use a credential scoped to that feed.");}
  }
  var source=new PackageSource(index) {AllowInsecureConnections=new Uri(index).Scheme=="http"};
  if(credential.Credential.Username.Length>0)source.Credentials=new PackageSourceCredential(index,credential.Credential.Username,credential.Secret,true,"basic");
  var providers=Repository.Provider.GetCoreV3().Where(p=>p.Value.ResourceType!=typeof(HttpHandlerResource)).ToList();
  providers.Insert(0,new Lazy<INuGetResourceProvider>(()=>new ScopedNuGetHandlerProvider(index,credential,ct)));
  return new SourceRepository(source,providers);
 }
 public string RegistryUrl(string target) {
  var host=target.Split('/')[0];return Connection.Length>0&&new Uri(Connection).Authority==host?Connection:"https://"+host;
 }
 public async Task<(PublishCredential Credential,string Secret)?> OciCredential(PublishProfile p,string host,CancellationToken ct) {
  if(!p.Container!.UseMyDockerLogin)return Credential(p,host);
  var configured=p.Credentials.FirstOrDefault(c=>c.Purpose=="push"&&PublishSecretStore.Host(c.ScopeHost)==host);
  if(configured!=null&&secrets.Get(configured,host) is {Length:>0} value)return (configured,value);
  return await DockerCredential(host,ct);
 }
 // Explicit Use my Docker login permits read-only access to the user's scoped Docker credential.
 public static async Task<(PublishCredential Credential,string Secret)?> DockerCredential(string host,CancellationToken ct,string? directory=null) {
  directory??=Environment.GetEnvironmentVariable("DOCKER_CONFIG")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".docker");
  var file=Path.Combine(directory,"config.json");if(!File.Exists(file))return null;
  using var config=JsonDocument.Parse(await File.ReadAllTextAsync(file,ct));var root=config.RootElement;
  string? helper=null;
  if(root.TryGetProperty("credHelpers",out var helpers))foreach(var item in helpers.EnumerateObject())if(PublishSecretStore.Host(item.Name)==host)helper=item.Value.GetString();
  if(helper==null&&root.TryGetProperty("credsStore",out var store))helper=store.GetString();
  string username,secret;
  if(!string.IsNullOrWhiteSpace(helper)) {
   if(!Regex.IsMatch(helper,@"^[a-zA-Z0-9_.-]+$"))throw new InvalidDataException("Invalid Docker credential helper name.");
   var result=await new PublishProcessRunner().RunAsync("docker-credential-"+helper,["get"],Environment.CurrentDirectory,ct:ct,stdin:host+"\n");
   if(result.ExitCode!=0)return null;
   using var data=JsonDocument.Parse(result.Output);username=data.RootElement.GetProperty("Username").GetString()??"";secret=data.RootElement.GetProperty("Secret").GetString()??"";
  } else {
   if(!root.TryGetProperty("auths",out var auths))return null;
   var item=auths.EnumerateObject().FirstOrDefault(x=>PublishSecretStore.Host(x.Name)==host);
   if(item.Value.ValueKind!=JsonValueKind.Object)return null;
   if(item.Value.TryGetProperty("identitytoken",out var identity)&&identity.GetString() is {Length:>0} token)return (new PublishCredential {ScopeHost=host,Username="<token>",Purpose="identity-token"},token);
   if(!item.Value.TryGetProperty("auth",out var auth))return null;
   var decoded=Encoding.UTF8.GetString(Convert.FromBase64String(auth.GetString()??""));var split=decoded.IndexOf(':');if(split<0)return null;username=decoded[..split];secret=decoded[(split+1)..];
  }
  if(secret.Length==0)return null;
  return (new PublishCredential {ScopeHost=host,Username=username,Purpose=username=="<token>"?"identity-token":"push"},secret);
 }
}
// NuGet's public Push API has no cancellation parameter. A per-repository handler
// binds every transfer (including Push) to this operation's token and host scope.
internal sealed class ScopedNuGetHandlerProvider(string index,(PublishCredential Credential,string Secret) credential,CancellationToken operation)
 : ResourceProvider(typeof(HttpHandlerResource),nameof(ScopedNuGetHandlerProvider)) {
 public override Task<Tuple<bool,INuGetResource?>> TryCreate(SourceRepository source,CancellationToken token) {
  var client=new HttpClientHandler {AllowAutoRedirect=false};
  if(credential.Credential.Username.Length>0)client.Credentials=new NetworkCredential(credential.Credential.Username,credential.Secret);
  var scope=new ScopeHandler(PublishSecretStore.Host(index),credential,operation) {InnerHandler=client};
  return Task.FromResult(Tuple.Create(true,(INuGetResource?)new HttpHandlerResourceV3(client,scope)));
 }
 private sealed class ScopeHandler(string host,(PublishCredential Credential,string Secret) credential,CancellationToken operation):DelegatingHandler {
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
   if(request.RequestUri==null||PublishSecretStore.Host(request.RequestUri.AbsoluteUri)!=host)throw new HttpRequestException("NuGet request host differs from credential scope. Credential was not sent.");
   if(credential.Credential.Username.Length>0)request.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.Credential.Username+":"+credential.Secret)));
   request.Headers.TryAddWithoutValidation("X-NuGet-ApiKey",credential.Secret);
   using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,operation);
   return await base.SendAsync(request,linked.Token);
  }
 }
}
public sealed class OciClient(PublishTargets targets) {
 public async Task<string> Request(string reference,string relative,HttpMethod method,(PublishCredential Credential,string Secret)? credential,CancellationToken ct) {
  var slash=reference.IndexOf('/');var host=reference[..slash];var path=reference[(slash+1)..];var repo=path[..path.LastIndexOf(':')];
  var baseUrl=credential is {Credential.AllowHttp:true}?"http://"+host:targets.RegistryUrl(reference);
  var url=baseUrl+"/v2/"+repo+"/"+relative;
  using var handler=new HttpClientHandler {AllowAutoRedirect=false};using var http=new HttpClient(handler);
  AuthenticationHeaderValue? auth=null;
  if(credential is {} c)auth=c.Credential.Purpose=="identity-token"?new("Bearer",c.Secret):new("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(c.Credential.Username+":"+c.Secret)));
  async Task<HttpResponseMessage> Send() {using var request=new HttpRequestMessage(method,url);request.Headers.Authorization=auth;request.Headers.TryAddWithoutValidation("Accept","application/vnd.oci.image.index.v1+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.docker.distribution.manifest.v2+json");return await http.SendAsync(request,ct);}
  using var first=await Send();HttpResponseMessage? second=null;
  try {
   if(first.StatusCode==HttpStatusCode.Unauthorized&&first.Headers.WwwAuthenticate.FirstOrDefault(x=>x.Scheme.Equals("Bearer",StringComparison.OrdinalIgnoreCase)) is {} challenge) {
    var fields=Regex.Matches(challenge.Parameter??"","([a-z]+)=\"([^\"]*)\"").ToDictionary(m=>m.Groups[1].Value,m=>m.Groups[2].Value);
    if(!fields.TryGetValue("realm",out var realm))throw new IOException("Bearer challenge has no realm.");
    PublishTargets.ValidateHttp(realm);if(PublishSecretStore.Host(realm)!=host)throw new IOException("Bearer credential host differs from registry. Credential was not sent.");
    var tokenUrl=realm+(realm.Contains('?')?"&":"?")+"scope="+Uri.EscapeDataString(fields.GetValueOrDefault("scope","repository:"+repo+":pull"))+"&service="+Uri.EscapeDataString(fields.GetValueOrDefault("service",""));
    using var req=new HttpRequestMessage(HttpMethod.Get,tokenUrl);req.Headers.Authorization=auth;using var tokenResponse=await http.SendAsync(req,ct);tokenResponse.EnsureSuccessStatusCode();
    using var token=JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(ct));auth=new("Bearer",token.RootElement.TryGetProperty("token",out var t)?t.GetString():token.RootElement.GetProperty("access_token").GetString());
    second=await Send();
   }
   var response=second??first;response.EnsureSuccessStatusCode();
   return method==HttpMethod.Head?response.Headers.TryGetValues("Docker-Content-Digest",out var digest)?digest.Single():throw new IOException("Registry did not return a manifest digest."):await response.Content.ReadAsStringAsync(ct);
  }finally {second?.Dispose();}
 }
 public async Task<string[]> Tags(string reference,(PublishCredential Credential,string Secret)? credential,CancellationToken ct) {
  using var tags=JsonDocument.Parse(await Request(reference,"tags/list",HttpMethod.Get,credential,ct));
  var list=tags.RootElement.GetProperty("tags");
  return list.ValueKind==JsonValueKind.Array?list.EnumerateArray().Select(x=>x.GetString()!).ToArray():[];
 }
 public async Task<string> LatestTag(string reference,(PublishCredential Credential,string Secret)? credential,CancellationToken ct) {
  var values=(await Tags(reference,credential,ct)).Where(x=>x!="latest").ToArray();
  return values.OrderByDescending(v=>NuGet.Versioning.NuGetVersion.TryParse(v,out var version)?version:null).ThenByDescending(v=>v,StringComparer.Ordinal).FirstOrDefault()??"not available";
 }
}
