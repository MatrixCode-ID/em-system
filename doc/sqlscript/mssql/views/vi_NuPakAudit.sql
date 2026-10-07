/*
    vi_NuPakAudit - view, one object per file.

    Run after the tables/ files that hold ta_NuPakAudit (tables/040-nupak.sql).
    Safe to run again (DROP IF EXISTS, then CREATE). The query below the last GO
    is an execution test and may run along.
*/

DROP VIEW IF EXISTS [dbo].[vi_NuPakAudit];
GO

CREATE VIEW [dbo].[vi_NuPakAudit] AS
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
FROM dbo.ta_NuPakAudit t
GO

-- Execution test
SELECT TOP 100 * FROM [dbo].[vi_NuPakAudit];
GO
