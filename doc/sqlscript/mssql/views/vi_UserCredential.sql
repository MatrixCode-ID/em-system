/*
    vi_UserCredential - view, one object per file.

    Run after the tables/ files that hold ta_UserCredential (tables/010-core.sql).
    Safe to run again (DROP IF EXISTS, then CREATE). The query below the last GO
    is an execution test and may run along.
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

-- Execution test
SELECT TOP 100 * FROM [dbo].[vi_UserCredential];
GO
