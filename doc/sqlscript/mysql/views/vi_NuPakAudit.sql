-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW `vi_NuPakAudit` AS
SELECT
   t.cNuPakFeedId,
   t.cNuPakAuditFeedSlug,
   t.cNuPakAuditFeedName,
   t.cNuPakAuditId,
   t.cNuPakAuditAt,
   t.cNuPakAuditAction,
   t.cNuPakAuditPackage,
   t.cNuPakAuditVersion,
   t.cNuPakAuditActorKind,
   t.cNuPakAuditActorId,
   t.cNuPakAuditActorName,
   t.cNuPakAuditResult,
   t.cNuPakAuditDetail,
   t.cNuPakAuditAddress,
   t.ustamp,
   t.datestamp,
   t.json_object
FROM ta_NuPakAudit t;
