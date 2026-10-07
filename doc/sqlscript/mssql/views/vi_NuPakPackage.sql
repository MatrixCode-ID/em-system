/*
    vi_NuPakPackage - view, one object per file.

    Run after the tables/ files that hold ta_NuPakPackage, ta_NuPakPrefix (tables/040-nupak.sql).
    Safe to run again (DROP IF EXISTS, then CREATE). The query below the last GO
    is an execution test and may run along.
*/

DROP VIEW IF EXISTS [dbo].[vi_NuPakPackage];
GO

CREATE VIEW [dbo].[vi_NuPakPackage] AS
SELECT
   t.cNuPakFeedId,
   t.cNuPakPackageId,
   t.cNuPakPrefixId,
   t.cNuPakPackageName,
   t.cNuPakPackageState,
   t.ustamp,
   t.datestamp,
   t.json_object,
   p.cNuPakPrefixName
FROM dbo.ta_NuPakPackage t
JOIN dbo.ta_NuPakPrefix p ON p.cNuPakPrefixId=t.cNuPakPrefixId
GO

-- Execution test
SELECT TOP 100 * FROM [dbo].[vi_NuPakPackage];
GO
