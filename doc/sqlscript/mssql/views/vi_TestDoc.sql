/*
    vi_TestDoc - view, satu objek per berkas.

    Jalankan sesudah tables/ yang memuat ta_TestDoc (tables/900-emtest.sql).
    Aman dijalankan ulang (DROP IF EXISTS lalu CREATE). Query di bawah GO terakhir
    adalah uji eksekusi, boleh ikut jalan.
*/

DROP VIEW IF EXISTS [dbo].[vi_TestDoc];
GO

CREATE VIEW [dbo].[vi_TestDoc] AS
SELECT * FROM [dbo].[ta_TestDoc]
GO

-- Uji eksekusi
SELECT TOP 100 * FROM [dbo].[vi_TestDoc];
GO
