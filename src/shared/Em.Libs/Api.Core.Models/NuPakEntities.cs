using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace Em.Api.Core.Models;
/// <summary>Row of table <c>ta_NuPakPrefix</c>.</summary>
[Table("ta_NuPakPrefix")]
public class ta_NuPakPrefix {
 /// <summary>Key of the related <c>ta_NuPakFeed</c> row.</summary>
 public string cNuPakFeedId { get; set; } = "";
 /// <summary>Primary key (ULID).</summary>
 [Key] public string cNuPakPrefixId { get; set; } = "";
 /// <summary>Name.</summary>
 public string cNuPakPrefixName { get; set; } = "";
 /// <summary>Record state.</summary>
 public int cNuPakPrefixState { get; set; }
 /// <summary>Description.</summary>
 public string? cNuPakPrefixDescription { get; set; }
 /// <summary>Last update time.</summary>
 public DateTime ustamp { get; set; }
 /// <summary>Creation time.</summary>
 public DateTime datestamp { get; set; }
 /// <summary>Additional data as JSON.</summary>
 public string? json_object { get; set; }
}
/// <summary>Row of view <c>vi_NuPakPrefix</c>.</summary>
[Table("vi_NuPakPrefix")]
public class vi_NuPakPrefix {
 /// <summary>Key of the related <c>ta_NuPakFeed</c> row.</summary>
 public string cNuPakFeedId { get; set; } = "";
 /// <summary>Primary key (ULID).</summary>
 [Key] public string cNuPakPrefixId { get; set; } = "";
 /// <summary>Name.</summary>
 public string cNuPakPrefixName { get; set; } = "";
 /// <summary>Record state.</summary>
 public int cNuPakPrefixState { get; set; }
 /// <summary>Description.</summary>
 public string? cNuPakPrefixDescription { get; set; }
 /// <summary>Last update time.</summary>
 public DateTime ustamp { get; set; }
 /// <summary>Creation time.</summary>
 public DateTime datestamp { get; set; }
 /// <summary>Additional data as JSON.</summary>
 public string? json_object { get; set; }
}
/// <summary>Row of table <c>ta_NuPakPackage</c>.</summary>
[Table("ta_NuPakPackage")]
public class ta_NuPakPackage {
 /// <summary>Key of the related <c>ta_NuPakFeed</c> row.</summary>
 public string cNuPakFeedId { get; set; } = "";
 /// <summary>Primary key (ULID).</summary>
 [Key] public string cNuPakPackageId { get; set; } = "";
 /// <summary>Key of the related <c>ta_NuPakPrefix</c> row.</summary>
 public string cNuPakPrefixId { get; set; } = "";
 /// <summary>Name.</summary>
 public string cNuPakPackageName { get; set; } = "";
 /// <summary>Record state.</summary>
 public int cNuPakPackageState { get; set; }
 /// <summary>Last update time.</summary>
 public DateTime ustamp { get; set; }
 /// <summary>Creation time.</summary>
 public DateTime datestamp { get; set; }
 /// <summary>Additional data as JSON.</summary>
 public string? json_object { get; set; }
}
/// <summary>Row of view <c>vi_NuPakPackage</c>.</summary>
[Table("vi_NuPakPackage")]
public class vi_NuPakPackage {
 /// <summary>Key of the related <c>ta_NuPakFeed</c> row.</summary>
 public string cNuPakFeedId { get; set; } = "";
 /// <summary>Primary key (ULID).</summary>
 [Key] public string cNuPakPackageId { get; set; } = "";
 /// <summary>Key of the related <c>ta_NuPakPrefix</c> row.</summary>
 public string cNuPakPrefixId { get; set; } = "";
 /// <summary>Name.</summary>
 public string cNuPakPackageName { get; set; } = "";
 /// <summary>Record state.</summary>
 public int cNuPakPackageState { get; set; }
 /// <summary>Last update time.</summary>
 public DateTime ustamp { get; set; }
 /// <summary>Creation time.</summary>
 public DateTime datestamp { get; set; }
 /// <summary>Additional data as JSON.</summary>
 public string? json_object { get; set; }
 /// <summary>Nu pak prefix name.</summary>
 public string cNuPakPrefixName { get; set; } = "";
}
/// <summary>Row of table <c>ta_NuPakVersion</c>.</summary>
[Table("ta_NuPakVersion")]
public class ta_NuPakVersion {
 /// <summary>Primary key (ULID).</summary>
 [Key] public string cNuPakVersionId { get; set; } = "";
 /// <summary>Key of the related <c>ta_NuPakPackage</c> row.</summary>
 public string cNuPakPackageId { get; set; } = "";
 /// <summary>Number.</summary>
 public string cNuPakVersionNumber { get; set; } = "";
 /// <summary>Original.</summary>
 public string cNuPakVersionOriginal { get; set; } = "";
 /// <summary>Prerelease.</summary>
 public bool cNuPakVersionPrerelease { get; set; }
 /// <summary>Record state.</summary>
 public int cNuPakVersionState { get; set; }
 /// <summary>Recycled at.</summary>
 public DateTime? cNuPakVersionRecycledAt { get; set; }
 /// <summary>Size.</summary>
 public long cNuPakVersionSize { get; set; }
 /// <summary>Hash.</summary>
 public string cNuPakVersionHash { get; set; } = "";
 /// <summary>Nuspec.</summary>
 public string cNuPakVersionNuspec { get; set; } = "";
 /// <summary>Title.</summary>
 public string? cNuPakVersionTitle { get; set; }
 /// <summary>Description.</summary>
 public string? cNuPakVersionDescription { get; set; }
 /// <summary>Authors.</summary>
 public string? cNuPakVersionAuthors { get; set; }
 /// <summary>Tags.</summary>
 public string? cNuPakVersionTags { get; set; }
 /// <summary>Pushed by, referencing <c>cRobotId</c>.</summary>
 public string? cNuPakVersionPushedBy_cRobotId { get; set; }
 /// <summary>Last update time.</summary>
 public DateTime ustamp { get; set; }
 /// <summary>Creation time.</summary>
 public DateTime datestamp { get; set; }
 /// <summary>Additional data as JSON.</summary>
 public string? json_object { get; set; }
}
/// <summary>Row of view <c>vi_NuPakVersion</c>.</summary>
[Table("vi_NuPakVersion")]
public class vi_NuPakVersion {
 /// <summary>Primary key (ULID).</summary>
 [Key] public string cNuPakVersionId { get; set; } = "";
 /// <summary>Key of the related <c>ta_NuPakPackage</c> row.</summary>
 public string cNuPakPackageId { get; set; } = "";
 /// <summary>Number.</summary>
 public string cNuPakVersionNumber { get; set; } = "";
 /// <summary>Original.</summary>
 public string cNuPakVersionOriginal { get; set; } = "";
 /// <summary>Prerelease.</summary>
 public bool cNuPakVersionPrerelease { get; set; }
 /// <summary>Record state.</summary>
 public int cNuPakVersionState { get; set; }
 /// <summary>Recycled at.</summary>
 public DateTime? cNuPakVersionRecycledAt { get; set; }
 /// <summary>Size.</summary>
 public long cNuPakVersionSize { get; set; }
 /// <summary>Hash.</summary>
 public string cNuPakVersionHash { get; set; } = "";
 /// <summary>Title.</summary>
 public string? cNuPakVersionTitle { get; set; }
 /// <summary>Description.</summary>
 public string? cNuPakVersionDescription { get; set; }
 /// <summary>Authors.</summary>
 public string? cNuPakVersionAuthors { get; set; }
 /// <summary>Tags.</summary>
 public string? cNuPakVersionTags { get; set; }
 /// <summary>Pushed by, referencing <c>cRobotId</c>.</summary>
 public string? cNuPakVersionPushedBy_cRobotId { get; set; }
 /// <summary>Last update time.</summary>
 public DateTime ustamp { get; set; }
 /// <summary>Creation time.</summary>
 public DateTime datestamp { get; set; }
 /// <summary>Additional data as JSON.</summary>
 public string? json_object { get; set; }
}
/// <summary>Row of table <c>ta_NuPakPrefixRobot</c>.</summary>
[Table("ta_NuPakPrefixRobot")]
public class ta_NuPakPrefixRobot {
 /// <summary>Key of the related <c>ta_Robot</c> row.</summary>
 public string cRobotId { get; set; } = "";
 /// <summary>Key of the related <c>ta_NuPakPrefix</c> row.</summary>
 public string cNuPakPrefixId { get; set; } = "";
 /// <summary>Access.</summary>
 public string cNuPakPrefixRobotAccess { get; set; } = "";
 /// <summary>Last update time.</summary>
 public DateTime ustamp { get; set; }
 /// <summary>Creation time.</summary>
 public DateTime datestamp { get; set; }
 /// <summary>Additional data as JSON.</summary>
 public string? json_object { get; set; }
}
/// <summary>Row of table <c>ta_NuPakAudit</c>.</summary>
[Table("ta_NuPakAudit")]
public class ta_NuPakAudit {
 /// <summary>Key of the related <c>ta_NuPakFeed</c> row.</summary>
 public string? cNuPakFeedId { get; set; }
 /// <summary>Feed slug.</summary>
 public string? cNuPakAuditFeedSlug { get; set; }
 /// <summary>Feed name.</summary>
 public string? cNuPakAuditFeedName { get; set; }
 /// <summary>Primary key (ULID).</summary>
 [Key] public string cNuPakAuditId { get; set; } = "";
 /// <summary>At.</summary>
 public DateTime cNuPakAuditAt { get; set; }
 /// <summary>Action.</summary>
 public string cNuPakAuditAction { get; set; } = "";
 /// <summary>Package.</summary>
 public string? cNuPakAuditPackage { get; set; }
 /// <summary>Version.</summary>
 public string? cNuPakAuditVersion { get; set; }
 /// <summary>Actor kind.</summary>
 public string cNuPakAuditActorKind { get; set; } = "";
 /// <summary>Actor ID.</summary>
 public string? cNuPakAuditActorId { get; set; }
 /// <summary>Actor name.</summary>
 public string cNuPakAuditActorName { get; set; } = "";
 /// <summary>Result.</summary>
 public string cNuPakAuditResult { get; set; } = "";
 /// <summary>Detail.</summary>
 public string? cNuPakAuditDetail { get; set; }
 /// <summary>Address.</summary>
 public string? cNuPakAuditAddress { get; set; }
 /// <summary>Last update time.</summary>
 public DateTime ustamp { get; set; }
 /// <summary>Creation time.</summary>
 public DateTime datestamp { get; set; }
 /// <summary>Additional data as JSON.</summary>
 public string? json_object { get; set; }
}
/// <summary>Row of view <c>vi_NuPakAudit</c>.</summary>
[Table("vi_NuPakAudit")]
public class vi_NuPakAudit {
 /// <summary>Key of the related <c>ta_NuPakFeed</c> row.</summary>
 public string? cNuPakFeedId { get; set; }
 /// <summary>Feed slug.</summary>
 public string? cNuPakAuditFeedSlug { get; set; }
 /// <summary>Feed name.</summary>
 public string? cNuPakAuditFeedName { get; set; }
 /// <summary>Primary key (ULID).</summary>
 [Key] public string cNuPakAuditId { get; set; } = "";
 /// <summary>At.</summary>
 public DateTime cNuPakAuditAt { get; set; }
 /// <summary>Action.</summary>
 public string cNuPakAuditAction { get; set; } = "";
 /// <summary>Package.</summary>
 public string? cNuPakAuditPackage { get; set; }
 /// <summary>Version.</summary>
 public string? cNuPakAuditVersion { get; set; }
 /// <summary>Actor kind.</summary>
 public string cNuPakAuditActorKind { get; set; } = "";
 /// <summary>Actor ID.</summary>
 public string? cNuPakAuditActorId { get; set; }
 /// <summary>Actor name.</summary>
 public string cNuPakAuditActorName { get; set; } = "";
 /// <summary>Result.</summary>
 public string cNuPakAuditResult { get; set; } = "";
 /// <summary>Detail.</summary>
 public string? cNuPakAuditDetail { get; set; }
 /// <summary>Address.</summary>
 public string? cNuPakAuditAddress { get; set; }
 /// <summary>Last update time.</summary>
 public DateTime ustamp { get; set; }
 /// <summary>Creation time.</summary>
 public DateTime datestamp { get; set; }
 /// <summary>Additional data as JSON.</summary>
 public string? json_object { get; set; }
}
/// <summary>Row of table <c>ta_NuPakFeed</c>.</summary>
[Table("ta_NuPakFeed")]
public class ta_NuPakFeed {
 /// <summary>Primary key (ULID).</summary>
 [Key] public string cNuPakFeedId { get; set; } = "";
 /// <summary>Slug.</summary>
 public string cNuPakFeedSlug { get; set; } = "";
 /// <summary>Name.</summary>
 public string cNuPakFeedName { get; set; } = "";
 /// <summary>Description.</summary>
 public string? cNuPakFeedDescription { get; set; }
 /// <summary>Enabled.</summary>
 public bool cNuPakFeedEnabled { get; set; }
 /// <summary>Anonymous read.</summary>
 public bool cNuPakFeedAnonymousRead { get; set; }
 /// <summary>Last update time.</summary>
 public DateTime ustamp { get; set; }
 /// <summary>Creation time.</summary>
 public DateTime datestamp { get; set; }
 /// <summary>Additional data as JSON.</summary>
 public string? json_object { get; set; }
}
/// <summary>Row of view <c>vi_NuPakFeed</c>.</summary>
[Table("vi_NuPakFeed")]
public class vi_NuPakFeed : ta_NuPakFeed { }
