using System.Collections.ObjectModel;
using System.Globalization;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Navigations
{
   /// <summary>The robot expiry state.</summary>
   public enum RobotExpiryState
   {
      /// <summary>The token never expires.</summary>
      Never,
      /// <summary>The token is valid.</summary>
      Valid,
      /// <summary>The token expires soon.</summary>
      Soon,
      /// <summary>The token has expired.</summary>
      Expired,
   }
   /// <summary>One access right of a robot on a resource.</summary>
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
      /// <summary>The robot.</summary>
      public RobotItem Robot { get; }
      /// <summary>The manager id.</summary>
      public string ManagerId { get; }
      /// <summary>The resource id.</summary>
      public string ResourceId { get; }
      /// <summary>The resource caption.</summary>
      public string ResourceCaption { get; }
      /// <summary>The options.</summary>
      public RobotAccessOptionInfo[] Options { get; }
      /// <summary>The committed access.</summary>
      public RobotAccessOptionInfo CommittedAccess => _committed;
      /// <summary>The selected access.</summary>
      public RobotAccessOptionInfo SelectedAccess {
         get => Get<RobotAccessOptionInfo>() ?? _committed;
         set => Set(value, _ => { if (!_applying) _owner.OnAccessSelected(this); });
      }
      /// <summary>Indicates saving.</summary>
      public bool IsSaving { get => Get<bool>(); internal set => Set(value, _ => NotifyChanged(nameof(IsNotSaving))); }
      /// <summary>Indicates not saving.</summary>
      public bool IsNotSaving => !IsSaving;
      internal void Commit(RobotAccessOptionInfo access) => _committed = access;
      internal void Rollback() { _applying = true; try { SelectedAccess = _committed; } finally { _applying = false; } }
   }
   /// <summary>One robot in the list.</summary>
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
      /// <summary>The info.</summary>
      public RobotInfo Info { get; }
      /// <summary>The accesses.</summary>
      public ObservableCollection<RobotAccessItem> Accesses { get; } = [];
      /// <summary>Id robot.</summary>
      public string Id => Info.Id;

      /// <summary>The robot's name, used as the <c>docker login</c> user name.</summary>
      public string Name => Info.Name;
      /// <summary>The owner caption.</summary>
      public string OwnerCaption => Info.OwnerAccount ?? Info.OwnerUserId ?? "No owner";

      /// <summary>The description; an empty string when there is none.</summary>
      public string Description => Info.Description ?? "";

      /// <summary><c>true</c> when there is a description.</summary>
      public bool HasDescription => !string.IsNullOrWhiteSpace(Info.Description);

      /// <summary>The active status; an inactive robot cannot sign in.</summary>
      public bool IsActive => Info.IsActive;

      /// <summary>The first few characters of the token, to recognize which token is in use.</summary>
      public string TokenPrefixCaption => Info.TokenPrefix.Length == 0 ? "—" : Info.TokenPrefix + "…";

      /// <summary>The state of the token's validity.</summary>
      public RobotExpiryState ExpiryState {
         get {
            if (Info.TokenExpiry is not { } expiry) return RobotExpiryState.Never;

            var remaining = CtnInput.AsUtc(expiry) - DateTime.UtcNow;
            return remaining <= TimeSpan.Zero ? RobotExpiryState.Expired
               : remaining < SoonWindow ? RobotExpiryState.Soon
               : RobotExpiryState.Valid;
         }
      }

      /// <summary>The text of the validity chip: <c>Expired</c>, <c>Expires dd MMM yyyy</c>, or <c>No expiry</c>.</summary>
      public string ExpiryCaption => Info.TokenExpiry is not { } expiry ? "No expiry"
         : ExpiryState == RobotExpiryState.Expired ? "Expired"
         : "Expires " + expiry.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.CurrentCulture);

      /// <summary>The full validity date, or a sentence saying the token does not expire.</summary>
      public string ExpiryDetail => Info.TokenExpiry is { } expiry
         ? RobotManagerVm.LocalTime(expiry)
         : "Never expires";

      /// <summary>When the token was last used to sign in, or <c>never</c>.</summary>
      public string LastUsedCaption => Info.TokenLastUsed is { } used ? RobotManagerVm.LocalTime(used) : "never";

      /// <summary>A summary of the rights, e.g. <c>"W on acme · R on server"</c>; <c>"no access"</c> when it has none.</summary>
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
