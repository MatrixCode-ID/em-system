using System.Collections.ObjectModel;
using System.Globalization;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Navigations
{
   public enum RobotExpiryState { Never, Valid, Soon, Expired }
   public class RobotAccessItem : NotifyPropertyBase
   {
      private readonly RobotManagerVm _owner;
      private RobotAccessOptionInfo _committed;
      private bool _applying;
      internal RobotAccessItem(RobotManagerVm owner, RobotItem robot, RobotAccessManagerInfo manager, RobotAccessResourceInfo resource, string access) {
         _owner = owner; Robot = robot; ManagerId = manager.Id; ResourceId = resource.Id;
         ResourceCaption = manager.Name + " / " + resource.Name;
         Options = new[] { new RobotAccessOptionInfo { Code = "", Label = "No access" } }.Concat(manager.Options).ToArray();
         _committed = Options.FirstOrDefault(o => o.Code == access) ?? Options[0];
         _applying = true; SelectedAccess = _committed; _applying = false;
      }
      public RobotItem Robot { get; }
      public string ManagerId { get; }
      public string ResourceId { get; }
      public string ResourceCaption { get; }
      public RobotAccessOptionInfo[] Options { get; }
      public RobotAccessOptionInfo CommittedAccess => _committed;
      public RobotAccessOptionInfo SelectedAccess {
         get => Get<RobotAccessOptionInfo>() ?? _committed;
         set => Set(value, _ => { if (!_applying) _owner.OnAccessSelected(this); });
      }
      public bool IsSaving { get => Get<bool>(); internal set => Set(value, _ => NotifyChanged(nameof(IsNotSaving))); }
      public bool IsNotSaving => !IsSaving;
      internal void Commit(RobotAccessOptionInfo access) => _committed = access;
      internal void Rollback() { _applying = true; try { SelectedAccess = _committed; } finally { _applying = false; } }
   }
   public class RobotItem : NotifyPropertyBase
   {
      private static readonly TimeSpan SoonWindow = TimeSpan.FromDays(14);
      internal RobotItem(RobotInfo info, IEnumerable<RobotAccessManagerInfo> managers, RobotManagerVm owner) {
         Info = info;
         foreach (var manager in managers)
            foreach (var resource in manager.Resources) {
               var access = info.Accesses.FirstOrDefault(a => a.ManagerId == manager.Id && a.ResourceId == resource.Id)?.Access ?? "";
               Accesses.Add(new RobotAccessItem(owner, this, manager, resource, access));
            }
      }
      public RobotInfo Info { get; }
      public ObservableCollection<RobotAccessItem> Accesses { get; } = [];
      /// <summary>Id robot.</summary>
      public string Id => Info.Id;

      /// <summary>Nama robot, dipakai sebagai username <c>docker login</c>.</summary>
      public string Name => Info.Name;
      public string OwnerCaption => Info.OwnerAccount ?? Info.OwnerUserId ?? "No owner";

      /// <summary>Deskripsi; string kosong kalau tidak ada.</summary>
      public string Description => Info.Description ?? "";

      /// <summary><c>true</c> kalau deskripsi ada.</summary>
      public bool HasDescription => !string.IsNullOrWhiteSpace(Info.Description);

      /// <summary>Status aktif; robot nonaktif tidak bisa login.</summary>
      public bool IsActive => Info.IsActive;

      /// <summary>Beberapa karakter awal token, untuk mengenali token mana yang dipakai.</summary>
      public string TokenPrefixCaption => Info.TokenPrefix.Length == 0 ? "—" : Info.TokenPrefix + "…";

      /// <summary>Keadaan masa berlaku token.</summary>
      public RobotExpiryState ExpiryState {
         get {
            if (Info.TokenExpiry is not { } expiry) return RobotExpiryState.Never;

            var remaining = CtnInput.AsUtc(expiry) - DateTime.UtcNow;
            return remaining <= TimeSpan.Zero ? RobotExpiryState.Expired
               : remaining < SoonWindow ? RobotExpiryState.Soon
               : RobotExpiryState.Valid;
         }
      }

      /// <summary>Tulisan chip masa berlaku: <c>Expired</c>, <c>Expires dd MMM yyyy</c>, atau <c>No expiry</c>.</summary>
      public string ExpiryCaption => Info.TokenExpiry is not { } expiry ? "No expiry"
         : ExpiryState == RobotExpiryState.Expired ? "Expired"
         : "Expires " + expiry.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.CurrentCulture);

      /// <summary>Tanggal lengkap masa berlaku, atau kalimat bahwa token tidak kedaluwarsa.</summary>
      public string ExpiryDetail => Info.TokenExpiry is { } expiry
         ? RobotManagerVm.LocalTime(expiry)
         : "Never expires";

      /// <summary>Kapan token terakhir dipakai login, atau <c>never</c>.</summary>
      public string LastUsedCaption => Info.TokenLastUsed is { } used ? RobotManagerVm.LocalTime(used) : "never";

      /// <summary>Ringkasan hak, mis. <c>"W on acme · R on server"</c>; <c>"no access"</c> kalau belum punya.</summary>
      public string AccessSummary {
         get {
            var parts = Accesses.Where(r => r.CommittedAccess.Code.Length > 0)
               .Select(r => $"{r.CommittedAccess.Code} on {r.ResourceCaption}").ToList();
            return parts.Count == 0 ? "no access" : string.Join(" · ", parts);
         }
      }

      internal void RefreshAccessSummary() => NotifyChanged(nameof(AccessSummary));
   }

}
