/*
    vi_NuPakVersion - view, satu objek per berkas.

    Jalankan sesudah tables/ yang memuat ta_NuPakVersion (tables/040-nupak.sql).
    Aman dijalankan ulang (DROP IF EXISTS lalu CREATE). Query di bawah GO terakhir
    adalah uji eksekusi, boleh ikut jalan.
*/

DROP VIEW IF EXISTS [dbo].[vi_NuPakVersion];
GO

CREATE VIEW [dbo].[vi_NuPakVersion] AS
SELECT
   t.cNuPakVersionId,
   t.cNuPakPackageId,
   t.cNuPakVersionNumber,
   t.cNuPakVersionOriginal,
   t.cNuPakVersionPrerelease,
   t.cNuPakVersionState,
   t.cNuPakVersionRecycledAt,
   t.cNuPakVersionSize,
   t.cNuPakVersionHash,
   t.cNuPakVersionTitle,
   t.cNuPakVersionDescription,
   t.cNuPakVersionAuthors,
   t.cNuPakVersionTags,
   t.cNuPakVersionPushedBy_cRobotId,
   t.ustamp,
   t.datestamp,
   t.json_object
FROM dbo.ta_NuPakVersion t
GO

-- Uji eksekusi
SELECT TOP 100 * FROM [dbo].[vi_NuPakVersion];
GO
