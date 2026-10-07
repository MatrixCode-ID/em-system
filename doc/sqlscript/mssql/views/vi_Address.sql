/*
    vi_Address - view, one object per file.

    Run after the tables/ files that hold ta_Address (tables/010-core.sql).
    Safe to run again (DROP IF EXISTS, then CREATE). The query below the last GO
    is an execution test and may run along.
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

-- Execution test
SELECT TOP 100 * FROM [dbo].[vi_Address];
GO
