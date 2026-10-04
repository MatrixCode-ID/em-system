DROP VIEW IF EXISTS vi_User;
GO
CREATE VIEW vi_User AS
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

SELECT TOP 100
   a.cUserId
   , a.cUserAccount
   , a.cContactId
   , a.cUserState
   , a.cUserIsAdmin
   , a.ustamp
   , a.datestamp
   , a.json_object
   , a.cContactFullName
   , a.cContactState
   , a.cContactType
   , a.cContactNote
   , a.cAddressName
   , a.cAddressLocation
   , a.cAddressZip
   , a.cCommType
   , a.cCommState
   , a.cCommValue
   , a.cCommNote
FROM vi_User AS a
