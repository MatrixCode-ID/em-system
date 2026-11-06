-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW `vi_Address` AS
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
FROM ta_Address AS a;
