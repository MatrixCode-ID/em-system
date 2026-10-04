/*
    vi_TestItem - view, satu objek per berkas.

    Jalankan sesudah tables/ yang memuat ta_TestItem (tables/900-emtest.sql).
    Aman dijalankan ulang (DROP IF EXISTS lalu CREATE). Query di bawah GO terakhir
    adalah uji eksekusi, boleh ikut jalan.
*/

DROP VIEW IF EXISTS [dbo].[vi_TestItem];
GO

CREATE VIEW [dbo].[vi_TestItem] AS
SELECT * FROM [dbo].[ta_TestItem]
GO

-- Uji eksekusi
SELECT TOP 100 * FROM [dbo].[vi_TestItem];
GO
