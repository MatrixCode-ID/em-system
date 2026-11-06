-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW `vi_User` AS
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
   ON b.cContactDefaultAddress_cAddressId = c.cAddressId;
