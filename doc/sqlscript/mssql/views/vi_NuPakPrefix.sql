/*
    vi_NuPakPrefix - view, satu objek per berkas.

    Jalankan sesudah tables/ yang memuat ta_NuPakPrefix (tables/040-nupak.sql).
    Aman dijalankan ulang (DROP IF EXISTS lalu CREATE). Query di bawah GO terakhir
    adalah uji eksekusi, boleh ikut jalan.
*/

DROP VIEW IF EXISTS [dbo].[vi_NuPakPrefix];
GO

CREATE VIEW [dbo].[vi_NuPakPrefix] AS
SELECT
   t.cNuPakFeedId,
   t.cNuPakPrefixId,
   t.cNuPakPrefixName,
   t.cNuPakPrefixState,
   t.cNuPakPrefixDescription,
   t.ustamp,
   t.datestamp,
   t.json_object
FROM dbo.ta_NuPakPrefix t
GO

-- Uji eksekusi
SELECT TOP 100 * FROM [dbo].[vi_NuPakPrefix];
GO
