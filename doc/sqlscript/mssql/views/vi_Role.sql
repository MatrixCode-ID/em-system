/*
    vi_Role - view, one object per file.

    Run after the tables/ files that hold ta_Role (tables/010-core.sql).
    Safe to run again (DROP IF EXISTS, then CREATE). The query below the last GO
    is an execution test and may run along.
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

-- Execution test
SELECT TOP 100 * FROM [dbo].[vi_Role];
GO
