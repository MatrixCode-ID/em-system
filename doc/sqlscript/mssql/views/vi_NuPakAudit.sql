/*
    vi_NuPakAudit - view, satu objek per berkas.

    Jalankan sesudah tables/ yang memuat ta_NuPakAudit (tables/040-nupak.sql).
    Aman dijalankan ulang (DROP IF EXISTS lalu CREATE). Query di bawah GO terakhir
    adalah uji eksekusi, boleh ikut jalan.
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

-- Uji eksekusi
SELECT TOP 100 * FROM [dbo].[vi_NuPakAudit];
GO
