/*
    vi_User - view, one object per file.

    Run after the tables/ files that hold ta_Address, ta_Comm, ta_Contact, ta_User (tables/010-core.sql).
    Safe to run again (DROP IF EXISTS, then CREATE). The query below the last GO
    is an execution test and may run along.
*/

DROP VIEW IF EXISTS [dbo].[vi_User];
GO

CREATE VIEW [dbo].[vi_User] AS
SELECT
   a.cUserId
   , a.cUserAccount
   , a.cContactId
   , a.cUserState
   , a.cUserIsAdmin
   , a.ustamp
   , a.datestamp
   , a.json_object
   , b.cContactFullName
   , b.cContactState
   , b.cContactType
   , b.cContactNote
   , c.cAddressName
   , c.cAddressLocation
   , c.cAddressZip
   , d.cCommType
   , d.cCommState
   , d.cCommValue
   , d.cCommNote
FROM ta_User AS a
INNER JOIN ta_Contact AS b
   ON a.cContactId = b.cContactId
INNER JOIN ta_Comm AS d
   ON b.cContactDefaultComm_cCommId = d.cCommId
LEFT OUTER JOIN ta_Address AS c
   ON b.cContactDefaultAddress_cAddressId = c.cAddressId
GO

-- Execution test
SELECT TOP 100 * FROM [dbo].[vi_User];
GO
