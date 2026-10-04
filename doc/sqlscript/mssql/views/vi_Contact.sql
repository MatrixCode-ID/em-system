/*
    vi_Contact - view, satu objek per berkas.

    Jalankan sesudah tables/ yang memuat ta_Address, ta_Comm, ta_Contact (tables/010-core.sql).
    Aman dijalankan ulang (DROP IF EXISTS lalu CREATE). Query di bawah GO terakhir
    adalah uji eksekusi, boleh ikut jalan.
*/

DROP VIEW IF EXISTS [dbo].[vi_Contact];
GO

CREATE VIEW [dbo].[vi_Contact] AS
SELECT
   a.cContactId
   , a.cContactFullName
   , a.cContactState
   , a.cContactType
   , a.cContactNote
   , a.cContactDefaultAddress_cAddressId
   , a.cContactDefaultComm_cCommId
   , a.ustamp
   , a.datestamp
   , a.json_object
   , b.cAddressName
   , b.cAddressLocation
   , b.cAddressZip
   , c.cCommType
   , c.cCommState
   , c.cCommValue
   , c.cCommNote
FROM ta_Contact AS a
LEFT OUTER JOIN ta_Address AS b
   ON a.cContactDefaultAddress_cAddressId = b.cAddressId
LEFT OUTER JOIN ta_Comm AS c
   ON a.cContactDefaultComm_cCommId = c.cCommId
GO

-- Uji eksekusi
SELECT TOP 100 * FROM [dbo].[vi_Contact];
GO
