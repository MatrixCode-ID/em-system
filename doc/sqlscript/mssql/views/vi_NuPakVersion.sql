/*
    vi_NuPakVersion - view, one object per file.

    Run after the tables/ files that hold ta_NuPakVersion (tables/040-nupak.sql).
    Safe to run again (DROP IF EXISTS, then CREATE). The query below the last GO
    is an execution test and may run along.
*/

DROP VIEW IF EXISTS [dbo].[vi_NuPakVersion];
GO

CREATE VIEW [dbo].[vi_NuPakVersion] AS
SELECT
   t.cNuPakVersionId,
   t.cNuPakPackageId,
   t.cNuPakVersionNumber,
   t.cNuPakVersionOriginal,
   t.cNuPakVersionPrerelease,
   t.cNuPakVersionState,
   t.cNuPakVersionRecycledAt,
   t.cNuPakVersionSize,
   t.cNuPakVersionHash,
   t.cNuPakVersionTitle,
   t.cNuPakVersionDescription,
   t.cNuPakVersionAuthors,
   t.cNuPakVersionTags,
   t.cNuPakVersionPushedBy_cRobotId,
   t.ustamp,
   t.datestamp,
   t.json_object
FROM dbo.ta_NuPakVersion t
GO

-- Execution test
SELECT TOP 100 * FROM [dbo].[vi_NuPakVersion];
GO
