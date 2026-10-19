using Em.Api.Core.Models;
using Em.Api.Shared;
using Em.Shared;

namespace Em.Api.Core.Log.Audit;

/// <summary>The engine's own audit groups (<c>System.*</c>), registered by <c>AddLogServer</c> and always on.</summary>
internal static class SystemAuditCatalog
{
   public const string Auth = "System.Auth";
   public const string Password = "System.Password";
   public const string User = "System.User";
   public const string Role = "System.Role";
   public const string Robot = "System.Robot";
   public const string Settings = "System.Settings";
   public const string Registry = "System.Registry";
   public const string NuPak = "System.NuPak";
   public const string ObjectStorage = "System.ObjectStorage";

   private const string Admin = Defaults.AdministrativeToolsModuleName + ":";
   private const string LogClaims = Defaults.LogModuleName + ":";

   /// <summary>Groups whose reading also needs <c>Log Security Read</c> in the Log Manager.</summary>
   public static readonly string[] SecurityGroups = [Auth, Password];

   public static IEnumerable<AuditGroupDefinition> Create() {
      yield return Group(Auth, "Sign-in and sessions", [Admin + "User Manager Access", LogClaims + ILogServices.SecurityReadClaim], g => g
         .Entry("signin.success", "Signed in")
         .Entry("signin.failed", "Sign-in failed")
         .Entry("signout", "Signed out")
         .Entry("signout.all", "Signed out of every device")
         .Entry("session.revoked", "Sessions revoked")
         .Entry("session.expired", "Session expired")
         .Entry("token.rejected", "Access token rejected")
         .Entry("debug.accepted", "Debug token accepted")
         .Entry("debug.impersonate", "Debug token acted as another account"));
      yield return Group(Password, "Passwords", [Admin + "User Manager Access", LogClaims + ILogServices.SecurityReadClaim], g => g
         .Entry("changed.self", "Changed own password")
         .Entry("reset.byAdmin", "Password reset by an administrator")
         .Entry("admin.changed", "Changed the administrator password")
         .Entry("admin.reset", "Reset the administrator password"));
      yield return Group(User, "Users", [Admin + "User Manager Access", LogClaims + ILogServices.AuditReadClaim], g => g
         .Entry("created", "Created user")
         .Entry("updated", "Updated user")
         .Entry("deleted", "Deleted user")
         .Entry("state.changed", "Changed user state")
         .Entry("admin.toggled", "Changed administrator right")
         .Entry("role.granted", "Granted role")
         .Entry("role.updated", "Changed role period")
         .Entry("role.revoked", "Revoked role")
         .Entry("claim.granted", "Granted claim")
         .Entry("claim.revoked", "Revoked claim"));
      yield return Group(Role, "Roles", [Admin + "Role Manager Access", Admin + "User Manager Access", LogClaims + ILogServices.AuditReadClaim], g => g
         .Entry("created", "Created role")
         .Entry("updated", "Updated role")
         .Entry("deleted", "Deleted role")
         .Entry("claim.added", "Added claim to role")
         .Entry("claim.removed", "Removed claim from role"));
      yield return Group(Robot, "Robots", [Admin + IRobotServices.RobotClaim, LogClaims + ILogServices.AuditReadClaim], g => g
         .Entry("created", "Created robot")
         .Entry("updated", "Updated robot")
         .Entry("deleted", "Deleted robot")
         .Entry("regenerated", "Regenerated robot token")
         .Entry("access.set", "Changed robot access"));
      yield return Group(Settings, "Settings", [LogClaims + ILogServices.SettingsClaim, LogClaims + ILogServices.AuditReadClaim], g => g
         .Entry("smtp.changed", "Changed SMTP settings")
         .Entry("tenant.created", "Created a tenant")
         .Entry("tenant.changed", "Changed a tenant")
         .Entry("tenant.dept.created", "Created a department")
         .Entry("tenant.dept.changed", "Changed a department")
         .Entry("tenant.emp.created", "Created an employee")
         .Entry("tenant.emp.changed", "Changed an employee")
         .Entry("storage.changed", "Changed storage settings")
         .Entry("logstore.changed", "Changed log stores")
         .Entry("logstore.prepared", "Prepared a log store")
         .Entry("retention.changed", "Changed log retention")
         .Entry("retention.run", "Log retention ran"));
      yield return Group(Registry, "Container registry", [Admin + ICtnServices.CtnClaim, LogClaims + ILogServices.AuditReadClaim], g => g
         .Entry("tag.deleted", "Deleted tag")
         .Entry("manifest.deleted", "Deleted manifest")
         .Entry("gc.run", "Ran garbage collection")
         .Entry("deploy.target.deleted", "Deleted deploy target")
         .Entry("deploy.run", "Deployed"));
      yield return Group(NuPak, "NuGet packages", [Admin + INuPakServices.ManagerClaim, LogClaims + ILogServices.AuditReadClaim], g => g
         .Entry("feed.deleted", "Deleted feed")
         .Entry("prefix.deleted", "Deleted prefix")
         .Entry("version.purged", "Purged package version"));
      yield return Group(ObjectStorage, "Object storage", [Admin + IObjectStorageServices.ManagerClaim, LogClaims + ILogServices.AuditReadClaim], g => g
         .Entry("bucket.deleted", "Deleted bucket")
         .Entry("bucket.provisioned", "Created managed bucket")
         .Entry("bucket.adopted", "Adopted bucket as managed")
         .Entry("bucket.access.enforced", "Enforced managed bucket access")
         .Entry("bucket.released", "Released managed bucket")
         .Entry("object.deleted", "Deleted object")
         .Entry("version.purged", "Purged object version")
         .Entry("policy.deleted", "Deleted policy")
         .Entry("volume.deleted", "Deleted volume"));
   }

   private static AuditGroupDefinition Group(string name, string title, string[] readClaims, Action<AuditGroupBuilder> configure) {
      var builder = new AuditGroupBuilder(name).Title(title);
      var claims = readClaims.Select(ClaimAction.FromKey).ToArray();
      builder.CanRead(r => r.Claims.Any(c => claims.Any(k => string.Equals(k.ModuleName, c.ModuleName, StringComparison.OrdinalIgnoreCase) &&
                                                             string.Equals(k.Name, c.Name, StringComparison.OrdinalIgnoreCase))));
      configure(builder);
      return builder.Group;
   }
}

/// <summary>Engine audit of sign-in, sign-out and sessions (<c>System.Auth</c>).</summary>
internal sealed class AuthAudit(IAuditWriter audit)
{
   public Task SignedInAsync(string userId, string? account, string sessionId, DateTime expiry, string method) =>
      audit.WriteNowAsync(new AuditEntryDraft {
         Group = SystemAuditCatalog.Auth, Key = userId, Kind = "signin.success", TargetType = "user", ActorType = AuditActorTypes.User,
         ActorUserId = userId, ActorAccount = account, SessionId = sessionId, Summary = $"Signed in as {account ?? userId}",
         Detail = new { method, sessionExpiry = expiry }
      });

   /// <summary>A refused sign-in; <paramref name="userId"/> is set when the tried account exists.</summary>
   public Task SignInFailedAsync(string accountTried, string? userId, string reason) =>
      audit.WriteNowAsync(new AuditEntryDraft {
         Group = SystemAuditCatalog.Auth, Key = userId ?? accountTried, Kind = "signin.failed", TargetType = userId is null ? "account" : "user",
         ActorType = AuditActorTypes.Anonymous, Summary = $"Sign-in failed for '{accountTried}' ({reason})",
         Detail = new { accountTried, reason }
      });

   public Task SignedOutAsync(string userId, string sessionId) =>
      audit.WriteNowAsync(new AuditEntryDraft {
         Group = SystemAuditCatalog.Auth, Key = userId, Kind = "signout", TargetType = "user", SessionId = sessionId, Summary = "Signed out"
      });

   public Task SignedOutAllAsync(string userId, int sessions) =>
      audit.WriteNowAsync(new AuditEntryDraft {
         Group = SystemAuditCatalog.Auth, Key = userId, Kind = "signout.all", TargetType = "user",
         Summary = $"Signed out of every device ({sessions} session(s))", Detail = new { sessions }
      });

   public Task SessionsRevokedAsync(string userId, string reason, int sessions) =>
      audit.WriteNowAsync(new AuditEntryDraft {
         Group = SystemAuditCatalog.Auth, Key = userId, Kind = "session.revoked", TargetType = "user", Reason = reason,
         Summary = $"{sessions} session(s) revoked ({reason})", Detail = new { sessions }
      });

   public Task SessionExpiredAsync(string userId, string sessionId) =>
      audit.WriteNowAsync(new AuditEntryDraft {
         Group = SystemAuditCatalog.Auth, Key = userId, Kind = "session.expired", TargetType = "user", ActorType = AuditActorTypes.System,
         SessionId = sessionId, Summary = "Session expired (refresh refused)"
      });

   public Task TokenRejectedAsync(string reason) =>
      audit.WriteNowAsync(new AuditEntryDraft {
         Group = SystemAuditCatalog.Auth, Key = "token", Kind = "token.rejected", TargetType = "token", ActorType = AuditActorTypes.Anonymous,
         Summary = "Access token rejected", Detail = new { reason }
      });

   public Task DebugAcceptedAsync(string keyName, string userId, string? account, bool impersonating) =>
      audit.WriteNowAsync(new AuditEntryDraft {
         Group = SystemAuditCatalog.Auth, Key = userId, Kind = impersonating ? "debug.impersonate" : "debug.accepted", TargetType = "user",
         Summary = impersonating ? $"Debug token '{keyName}' acted as '{account ?? userId}'" : $"Debug token '{keyName}' accepted",
         Detail = new { keyName }
      });
}

/// <summary>Engine audit of password changes (<c>System.Password</c>).</summary>
internal sealed class PasswordAudit(IAuditWriter audit)
{
   public Task ChangedAsync(string userId, string kind, string summary) =>
      audit.WriteNowAsync(new AuditEntryDraft { Group = SystemAuditCatalog.Password, Key = userId, Kind = kind, TargetType = "user", Summary = summary });
}

/// <summary>Engine audit of users, their roles and claims (<c>System.User</c>).</summary>
internal sealed class UserAudit(IAuditWriter audit)
{
   private static readonly string[] Secret = ["cCredentialSecret", "cCredentialKey", "cUserSessionHash"];

   public IAuditWriter Writer => audit;

   public void Created(ta_User user) =>
      audit.AddEntry(new AuditEntryDraft {
         Group = SystemAuditCatalog.User, Key = user.cUserId, Kind = "created", TargetType = "user", DocNo = user.cUserAccount,
         Summary = $"Created user {user.cUserAccount}", Changes = AuditDiff.Of(null, user, secret: Secret)
      });

   /// <summary>Adds <c>updated</c> plus <c>state.changed</c>/<c>admin.toggled</c> when those fields changed.</summary>
   public void Updated(ta_User? before, ta_User after) {
      var changes = AuditDiff.Of(before, after, secret: Secret);
      if (changes.Length == 0) return;
      audit.AddEntry(new AuditEntryDraft {
         Group = SystemAuditCatalog.User, Key = after.cUserId, Kind = "updated", TargetType = "user", DocNo = after.cUserAccount,
         Summary = $"Updated user {after.cUserAccount} ({changes.Length} field(s))", Changes = changes
      });
      if (before is not null && before.cUserState != after.cUserState)
         audit.AddEntry(new AuditEntryDraft {
            Group = SystemAuditCatalog.User, Key = after.cUserId, Kind = "state.changed", TargetType = "user", DocNo = after.cUserAccount,
            Summary = $"State {before.cUserState} -> {after.cUserState}"
         });
      if (before is not null && before.cUserIsAdmin != after.cUserIsAdmin)
         audit.AddEntry(new AuditEntryDraft {
            Group = SystemAuditCatalog.User, Key = after.cUserId, Kind = "admin.toggled", TargetType = "user", DocNo = after.cUserAccount,
            Summary = after.cUserIsAdmin ? "Administrator right granted" : "Administrator right revoked"
         });
   }

   public void Deleted(string userId, string? account) =>
      audit.AddEntry(new AuditEntryDraft {
         Group = SystemAuditCatalog.User, Key = userId, Kind = "deleted", TargetType = "user", DocNo = account, Summary = $"Deleted user {account ?? userId}"
      });

   public void Role(string kind, ta_UserRole row, ta_UserRole? before = null) =>
      audit.AddEntry(new AuditEntryDraft {
         Group = SystemAuditCatalog.User, Key = row.cUserId, Kind = kind, TargetType = "user",
         Summary = kind switch { "role.granted" => $"Granted role {row.cRoleId}", "role.revoked" => $"Revoked role {row.cRoleId}", _ => $"Changed role {row.cRoleId}" },
         Changes = kind == "role.updated" ? AuditDiff.Of(before, row, row: row.cRoleId) : null, Detail = new { roleId = row.cRoleId }
      });

   public void Claim(string kind, string userId, string claim) =>
      audit.AddEntry(new AuditEntryDraft {
         Group = SystemAuditCatalog.User, Key = userId, Kind = kind, TargetType = "user",
         Summary = kind == "claim.granted" ? $"Granted claim {claim}" : $"Revoked claim {claim}", Detail = new { claim }
      });
}

/// <summary>Engine audit of roles (<c>System.Role</c>).</summary>
internal sealed class RoleAudit(IAuditWriter audit)
{
   public void Changed(string kind, ta_Role? before, ta_Role after) =>
      audit.AddEntry(new AuditEntryDraft {
         Group = SystemAuditCatalog.Role, Key = after.cRoleId, Kind = kind, TargetType = "role", DocNo = after.cRoleName,
         Summary = kind switch { "created" => $"Created role {after.cRoleName}", "deleted" => $"Deleted role {after.cRoleName}", _ => $"Updated role {after.cRoleName}" },
         Changes = kind == "deleted" ? null : AuditDiff.Of(before, after)
      });

   public void Claim(string kind, string roleId, string claim) =>
      audit.AddEntry(new AuditEntryDraft {
         Group = SystemAuditCatalog.Role, Key = roleId, Kind = kind, TargetType = "role",
         Summary = kind == "claim.added" ? $"Added claim {claim}" : $"Removed claim {claim}", Detail = new { claim }
      });
}

/// <summary>Engine audit written directly (own transaction) for areas whose writes do not go through an engine context.</summary>
internal sealed class SystemAudit(IAuditWriter audit)
{
   public Task WriteAsync(string group, string key, string kind, string summary, string? targetType = null, object? detail = null,
      IReadOnlyList<AuditFieldChange>? changes = null, CancellationToken ct = default) =>
      audit.WriteNowAsync(new AuditEntryDraft {
         Group = group, Key = key, Kind = kind, TargetType = targetType, Summary = summary, Detail = detail, Changes = changes
      }, ct);
}
