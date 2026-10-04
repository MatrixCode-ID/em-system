/*
    vi_NuPakPackage - view, satu objek per berkas.

    Jalankan sesudah tables/ yang memuat ta_NuPakPackage, ta_NuPakPrefix (tables/040-nupak.sql).
    Aman dijalankan ulang (DROP IF EXISTS lalu CREATE). Query di bawah GO terakhir
    adalah uji eksekusi, boleh ikut jalan.
*/

DROP VIEW IF EXISTS [dbo].[vi_NuPakPackage];
GO

CREATE VIEW [dbo].[vi_NuPakPackage] AS
SELECT
   t.cNuPakFeedId,
   t.cNuPakPackageId,
   t.cNuPakPrefixId,
   t.cNuPakPackageName,
   t.cNuPakPackageState,
   t.ustamp,
   t.datestamp,
   t.json_object,
   p.cNuPakPrefixName
FROM dbo.ta_NuPakPackage t
JOIN dbo.ta_NuPakPrefix p ON p.cNuPakPrefixId=t.cNuPakPrefixId
GO

-- Uji eksekusi
SELECT TOP 100 * FROM [dbo].[vi_NuPakPackage];
GO
