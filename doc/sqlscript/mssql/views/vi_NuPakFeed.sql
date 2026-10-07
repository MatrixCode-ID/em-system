/*
    vi_NuPakFeed - view, one object per file.

    Run after the tables/ files that hold ta_NuPakFeed (tables/040-nupak.sql).
    Safe to run again (DROP IF EXISTS, then CREATE). The query below the last GO
    is an execution test and may run along.
*/

DROP VIEW IF EXISTS [dbo].[vi_NuPakFeed];
GO

CREATE VIEW [dbo].[vi_NuPakFeed] AS
SELECT
   *
FROM dbo.ta_NuPakFeed
GO

-- Execution test
SELECT TOP 100 * FROM [dbo].[vi_NuPakFeed];
GO
