DROP VIEW IF EXISTS vi_Contact;
GO
CREATE VIEW vi_Contact AS
SELECT
   a.cContactId
   , a.cContactFullName
   , a.cContactState
   , a.cContactType
   , a.cContactNote
   , a.cContactDefaultAddress_cAddressId
   , a.cContactDefaultComm_cCommId
   , a.ustamp
   , a.datestamp
   , a.json_object
   , b.cAddressName
   , b.cAddressLocation
   , b.cAddressZip
   , c.cCommType
   , c.cCommState
   , c.cCommValue
   , c.cCommNote
FROM ta_Contact AS a
LEFT OUTER JOIN ta_Address AS b
   ON a.cContactDefaultAddress_cAddressId = b.cAddressId
LEFT OUTER JOIN ta_Comm AS c
   ON a.cContactDefaultComm_cCommId = c.cCommId

GO

SELECT TOP 100
   a.cContactId
   , a.cContactFullName
   , a.cContactState
   , a.cContactType
   , a.cContactNote
   , a.cContactDefaultAddress_cAddressId
   , a.cContactDefaultComm_cCommId
   , a.ustamp
   , a.datestamp
   , a.json_object
   , a.cAddressName
   , a.cAddressLocation
   , a.cAddressZip
   , a.cCommType
   , a.cCommState
   , a.cCommValue
   , a.cCommNote
FROM vi_Contact AS a;
