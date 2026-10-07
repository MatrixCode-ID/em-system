/*
    vi_Comm - view, one object per file.

    Run after the tables/ files that hold ta_Comm (tables/010-core.sql).
    Safe to run again (DROP IF EXISTS, then CREATE). The query below the last GO
    is an execution test and may run along.
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

-- Execution test
SELECT TOP 100 * FROM [dbo].[vi_Comm];
GO
