/*
    vi_Comm - view, satu objek per berkas.

    Jalankan sesudah tables/ yang memuat ta_Comm (tables/010-core.sql).
    Aman dijalankan ulang (DROP IF EXISTS lalu CREATE). Query di bawah GO terakhir
    adalah uji eksekusi, boleh ikut jalan.
*/

DROP VIEW IF EXISTS [dbo].[vi_Comm];
GO

CREATE VIEW [dbo].[vi_Comm] AS
SELECT
   a.cCommId
   , a.cContactId

   , a.cCommType
   , a.cCommState
   , a.cCommValue
   , a.cCommNote
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM ta_Comm AS a
GO

SELECT
   a.cCommId
   , a.cContactId
   , a.cCommType
   , a.cCommState
   , a.cCommValue
   , a.cCommNote
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM vi_Comm AS a
GO

-- Uji eksekusi
SELECT TOP 100 * FROM [dbo].[vi_Comm];
GO
