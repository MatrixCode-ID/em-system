DROP VIEW IF EXISTS vi_Address;
GO
CREATE VIEW vi_Address AS
SELECT
   a.cAddressId
   , a.cContactId
   , a.cAddressName
   , a.cAddressState
   , a.cAddressLocation
   , a.cAddressZip
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM ta_Address AS a
GO

SELECT
   a.cAddressId
   , a.cContactId
   , a.cAddressName
   , a.cAddressState
   , a.cAddressLocation
   , a.cAddressZip
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM vi_Address AS a