/*
    vi_Role - view, satu objek per berkas.

    Jalankan sesudah tables/ yang memuat ta_Role (tables/010-core.sql).
    Aman dijalankan ulang (DROP IF EXISTS lalu CREATE). Query di bawah GO terakhir
    adalah uji eksekusi, boleh ikut jalan.
*/

DROP VIEW IF EXISTS [dbo].[vi_Role];
GO

CREATE VIEW [dbo].[vi_Role] AS
SELECT
   a.cRoleId
   , a.cRoleName
   , a.cRoleState
   , a.cRoleDescription
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM ta_Role AS a
GO

-- Uji eksekusi
SELECT TOP 100 * FROM [dbo].[vi_Role];
GO
