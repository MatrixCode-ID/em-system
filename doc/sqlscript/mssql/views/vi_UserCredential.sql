/*
    vi_UserCredential - view, satu objek per berkas.

    Jalankan sesudah tables/ yang memuat ta_UserCredential (tables/010-core.sql).
    Aman dijalankan ulang (DROP IF EXISTS lalu CREATE). Query di bawah GO terakhir
    adalah uji eksekusi, boleh ikut jalan.
*/

DROP VIEW IF EXISTS [dbo].[vi_UserCredential];
GO

CREATE VIEW [dbo].[vi_UserCredential] AS
SELECT
   a.cCredentialId
   , a.cUserId
   , a.cCredentialType
   , a.cCredentialState
   , a.cCredentialKey
   , a.cCredentialSecret
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM ta_UserCredential AS a
GO

-- Uji eksekusi
SELECT TOP 100 * FROM [dbo].[vi_UserCredential];
GO
