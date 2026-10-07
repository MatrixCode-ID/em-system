/*
    vi_TestItem - view, one object per file.

    Run after the tables/ files that hold ta_TestItem (tables/900-emtest.sql).
    Safe to run again (DROP IF EXISTS, then CREATE). The query below the last GO
    is an execution test and may run along.
*/

DROP VIEW IF EXISTS [dbo].[vi_TestItem];
GO

CREATE VIEW [dbo].[vi_TestItem] AS
SELECT * FROM [dbo].[ta_TestItem]
GO

-- Execution test
SELECT TOP 100 * FROM [dbo].[vi_TestItem];
GO
