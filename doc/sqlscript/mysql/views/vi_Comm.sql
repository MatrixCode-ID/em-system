-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW `vi_Comm` AS
SELECT
   a.cCommId
   , a.cContactId

   , a.cCommType
   , a.cCommState
   , a.cCommValue
   , a.cCommNote
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM ta_Comm AS a;
