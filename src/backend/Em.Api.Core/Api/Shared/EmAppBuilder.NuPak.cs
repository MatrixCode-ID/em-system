using Em.Api.Core.Models;
using Em.Api.Core.NuPak;
using Em.Shared;
namespace Em.Api.Shared;
public partial class EmAppBuilder {
 internal bool NuPakRegistered { get; private set; }
 internal bool NuPakManaged { get; private set; }
 internal string? NuPakPath { get; private set; }
 internal int NuPakMaxPackageMb { get; private set; } = 250;
 public void AddNuPak() => RegisterNuPak(null,250,true);
 public void AddNuPak(string localStorePath,int maxPackageMb=250) {
  ArgumentException.ThrowIfNullOrWhiteSpace(localStorePath);
  RegisterNuPak(localStorePath,maxPackageMb,false);
 }
 private void RegisterNuPak(string? path,int limit,bool managed) {
  if(NuPakRegistered) throw new InvalidOperationException("AddNuPak may only be called once.");
  if(limit is <1 or >4096) throw new ArgumentOutOfRangeException(nameof(limit));
  NuPakRegistered=true; NuPakManaged=managed; NuPakPath=path; NuPakMaxPackageMb=limit;
  AddDbContext<NuPakDbContext>(); AddSingleton(_=>new NuPakSettings());
  AddService<INuPakServices,NuPakServices>();
  AddClaims(ClaimAction.Create<NuPakServices>(INuPakServices.ManagerClaim),ClaimAction.Create<NuPakServices>(INuPakServices.SettingsClaim));
  AddRobotAccessManager<NuPakRobotAccessManager>(); AddHostedService<NuPakStartup>();
 }
}
