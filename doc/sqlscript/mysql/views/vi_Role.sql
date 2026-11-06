-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW `vi_Role` AS
SELECT
   a.cRoleId
   , a.cRoleName
   , a.cRoleState
   , a.cRoleDescription
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM ta_Role AS a;
