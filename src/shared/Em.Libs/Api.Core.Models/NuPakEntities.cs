using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace Em.Api.Core.Models;
[Table("ta_NuPakPrefix")]
public class ta_NuPakPrefix {
 public string cNuPakFeedId { get; set; } = "";
 [Key] public string cNuPakPrefixId { get; set; } = "";
 public string cNuPakPrefixName { get; set; } = "";
 public int cNuPakPrefixState { get; set; }
 public string? cNuPakPrefixDescription { get; set; }
 public DateTime ustamp { get; set; }
 public DateTime datestamp { get; set; }
 public string? json_object { get; set; }
}
[Table("vi_NuPakPrefix")]
public class vi_NuPakPrefix {
 public string cNuPakFeedId { get; set; } = "";
 [Key] public string cNuPakPrefixId { get; set; } = "";
 public string cNuPakPrefixName { get; set; } = "";
 public int cNuPakPrefixState { get; set; }
 public string? cNuPakPrefixDescription { get; set; }
 public DateTime ustamp { get; set; }
 public DateTime datestamp { get; set; }
 public string? json_object { get; set; }
}
[Table("ta_NuPakPackage")]
public class ta_NuPakPackage {
 public string cNuPakFeedId { get; set; } = "";
 [Key] public string cNuPakPackageId { get; set; } = "";
 public string cNuPakPrefixId { get; set; } = "";
 public string cNuPakPackageName { get; set; } = "";
 public int cNuPakPackageState { get; set; }
 public DateTime ustamp { get; set; }
 public DateTime datestamp { get; set; }
 public string? json_object { get; set; }
}
[Table("vi_NuPakPackage")]
public class vi_NuPakPackage {
 public string cNuPakFeedId { get; set; } = "";
 [Key] public string cNuPakPackageId { get; set; } = "";
 public string cNuPakPrefixId { get; set; } = "";
 public string cNuPakPackageName { get; set; } = "";
 public int cNuPakPackageState { get; set; }
 public DateTime ustamp { get; set; }
 public DateTime datestamp { get; set; }
 public string? json_object { get; set; }
 public string cNuPakPrefixName { get; set; } = "";
}
[Table("ta_NuPakVersion")]
public class ta_NuPakVersion {
 [Key] public string cNuPakVersionId { get; set; } = "";
 public string cNuPakPackageId { get; set; } = "";
 public string cNuPakVersionNumber { get; set; } = "";
 public string cNuPakVersionOriginal { get; set; } = "";
 public bool cNuPakVersionPrerelease { get; set; }
 public int cNuPakVersionState { get; set; }
 public DateTime? cNuPakVersionRecycledAt { get; set; }
 public long cNuPakVersionSize { get; set; }
 public string cNuPakVersionHash { get; set; } = "";
 public string cNuPakVersionNuspec { get; set; } = "";
 public string? cNuPakVersionTitle { get; set; }
 public string? cNuPakVersionDescription { get; set; }
 public string? cNuPakVersionAuthors { get; set; }
 public string? cNuPakVersionTags { get; set; }
 public string? cNuPakVersionPushedBy_cRobotId { get; set; }
 public DateTime ustamp { get; set; }
 public DateTime datestamp { get; set; }
 public string? json_object { get; set; }
}
[Table("vi_NuPakVersion")]
public class vi_NuPakVersion {
 [Key] public string cNuPakVersionId { get; set; } = "";
 public string cNuPakPackageId { get; set; } = "";
 public string cNuPakVersionNumber { get; set; } = "";
 public string cNuPakVersionOriginal { get; set; } = "";
 public bool cNuPakVersionPrerelease { get; set; }
 public int cNuPakVersionState { get; set; }
 public DateTime? cNuPakVersionRecycledAt { get; set; }
 public long cNuPakVersionSize { get; set; }
 public string cNuPakVersionHash { get; set; } = "";
 public string? cNuPakVersionTitle { get; set; }
 public string? cNuPakVersionDescription { get; set; }
 public string? cNuPakVersionAuthors { get; set; }
 public string? cNuPakVersionTags { get; set; }
 public string? cNuPakVersionPushedBy_cRobotId { get; set; }
 public DateTime ustamp { get; set; }
 public DateTime datestamp { get; set; }
 public string? json_object { get; set; }
}
[Table("ta_NuPakPrefixRobot")]
public class ta_NuPakPrefixRobot {
 public string cRobotId { get; set; } = "";
 public string cNuPakPrefixId { get; set; } = "";
 public string cNuPakPrefixRobotAccess { get; set; } = "";
 public DateTime ustamp { get; set; }
 public DateTime datestamp { get; set; }
 public string? json_object { get; set; }
}
[Table("ta_NuPakAudit")]
public class ta_NuPakAudit {
 public string? cNuPakFeedId { get; set; }
 public string? cNuPakAuditFeedSlug { get; set; }
 public string? cNuPakAuditFeedName { get; set; }
 [Key] public string cNuPakAuditId { get; set; } = "";
 public DateTime cNuPakAuditAt { get; set; }
 public string cNuPakAuditAction { get; set; } = "";
 public string? cNuPakAuditPackage { get; set; }
 public string? cNuPakAuditVersion { get; set; }
 public string cNuPakAuditActorKind { get; set; } = "";
 public string? cNuPakAuditActorId { get; set; }
 public string cNuPakAuditActorName { get; set; } = "";
 public string cNuPakAuditResult { get; set; } = "";
 public string? cNuPakAuditDetail { get; set; }
 public string? cNuPakAuditAddress { get; set; }
 public DateTime ustamp { get; set; }
 public DateTime datestamp { get; set; }
 public string? json_object { get; set; }
}
[Table("vi_NuPakAudit")]
public class vi_NuPakAudit {
 public string? cNuPakFeedId { get; set; }
 public string? cNuPakAuditFeedSlug { get; set; }
 public string? cNuPakAuditFeedName { get; set; }
 [Key] public string cNuPakAuditId { get; set; } = "";
 public DateTime cNuPakAuditAt { get; set; }
 public string cNuPakAuditAction { get; set; } = "";
 public string? cNuPakAuditPackage { get; set; }
 public string? cNuPakAuditVersion { get; set; }
 public string cNuPakAuditActorKind { get; set; } = "";
 public string? cNuPakAuditActorId { get; set; }
 public string cNuPakAuditActorName { get; set; } = "";
 public string cNuPakAuditResult { get; set; } = "";
 public string? cNuPakAuditDetail { get; set; }
 public string? cNuPakAuditAddress { get; set; }
 public DateTime ustamp { get; set; }
 public DateTime datestamp { get; set; }
 public string? json_object { get; set; }
}
[Table("ta_NuPakFeed")]
public class ta_NuPakFeed {
 [Key] public string cNuPakFeedId { get; set; } = "";
 public string cNuPakFeedSlug { get; set; } = "";
 public string cNuPakFeedName { get; set; } = "";
 public string? cNuPakFeedDescription { get; set; }
 public bool cNuPakFeedEnabled { get; set; }
 public bool cNuPakFeedAnonymousRead { get; set; }
 public DateTime ustamp { get; set; }
 public DateTime datestamp { get; set; }
 public string? json_object { get; set; }
}
[Table("vi_NuPakFeed")]
public class vi_NuPakFeed : ta_NuPakFeed { }
