/*
    vi_User - view, satu objek per berkas.

    Jalankan sesudah tables/ yang memuat ta_Address, ta_Comm, ta_Contact, ta_User (tables/010-core.sql).
    Aman dijalankan ulang (DROP IF EXISTS lalu CREATE). Query di bawah GO terakhir
    adalah uji eksekusi, boleh ikut jalan.
*/

DROP VIEW IF EXISTS [dbo].[vi_User];
GO

CREATE VIEW [dbo].[vi_User] AS
SELECT
   a.cUserId
   , a.cUserAccount
   , a.cContactId
   , a.cUserState
   , a.cUserIsAdmin
   , a.ustamp
   , a.datestamp
   , a.json_object
   , b.cContactFullName
   , b.cContactState
   , b.cContactType
   , b.cContactNote
   , c.cAddressName
   , c.cAddressLocation
   , c.cAddressZip
   , d.cCommType
   , d.cCommState
   , d.cCommValue
   , d.cCommNote
FROM ta_User AS a
INNER JOIN ta_Contact AS b
   ON a.cContactId = b.cContactId
INNER JOIN ta_Comm AS d
   ON b.cContactDefaultComm_cCommId = d.cCommId
LEFT OUTER JOIN ta_Address AS c
   ON b.cContactDefaultAddress_cAddressId = c.cAddressId
GO

-- Uji eksekusi
SELECT TOP 100 * FROM [dbo].[vi_User];
GO
