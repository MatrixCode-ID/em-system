/*
    vi_NuPakFeed - view, satu objek per berkas.

    Jalankan sesudah tables/ yang memuat ta_NuPakFeed (tables/040-nupak.sql).
    Aman dijalankan ulang (DROP IF EXISTS lalu CREATE). Query di bawah GO terakhir
    adalah uji eksekusi, boleh ikut jalan.
*/

DROP VIEW IF EXISTS [dbo].[vi_NuPakFeed];
GO

CREATE VIEW [dbo].[vi_NuPakFeed] AS
SELECT
   *
FROM dbo.ta_NuPakFeed
GO

-- Uji eksekusi
SELECT TOP 100 * FROM [dbo].[vi_NuPakFeed];
GO
