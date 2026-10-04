/*
    vi_Address - view, satu objek per berkas.

    Jalankan sesudah tables/ yang memuat ta_Address (tables/010-core.sql).
    Aman dijalankan ulang (DROP IF EXISTS lalu CREATE). Query di bawah GO terakhir
    adalah uji eksekusi, boleh ikut jalan.
*/

DROP VIEW IF EXISTS [dbo].[vi_Address];
GO

CREATE VIEW [dbo].[vi_Address] AS
SELECT
   a.cAddressId
   , a.cContactId
   , a.cAddressName
   , a.cAddressState
   , a.cAddressLocation
   , a.cAddressZip
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM ta_Address AS a
GO

SELECT
   a.cAddressId
   , a.cContactId
   , a.cAddressName
   , a.cAddressState
   , a.cAddressLocation
   , a.cAddressZip
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM vi_Address AS a
GO

-- Uji eksekusi
SELECT TOP 100 * FROM [dbo].[vi_Address];
GO
