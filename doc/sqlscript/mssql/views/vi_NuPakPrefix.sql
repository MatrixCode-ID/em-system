/*
    vi_NuPakPrefix - view, one object per file.

    Run after the tables/ files that hold ta_NuPakPrefix (tables/040-nupak.sql).
    Safe to run again (DROP IF EXISTS, then CREATE). The query below the last GO
    is an execution test and may run along.
*/

DROP VIEW IF EXISTS [dbo].[vi_NuPakPrefix];
GO

CREATE VIEW [dbo].[vi_NuPakPrefix] AS
SELECT
   t.cNuPakFeedId,
   t.cNuPakPrefixId,
   t.cNuPakPrefixName,
   t.cNuPakPrefixState,
   t.cNuPakPrefixDescription,
   t.ustamp,
   t.datestamp,
   t.json_object
FROM dbo.ta_NuPakPrefix t
GO

-- Execution test
SELECT TOP 100 * FROM [dbo].[vi_NuPakPrefix];
GO
