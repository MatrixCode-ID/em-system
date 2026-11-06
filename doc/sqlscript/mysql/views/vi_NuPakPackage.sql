-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW `vi_NuPakPackage` AS
SELECT
   t.cNuPakFeedId,
   t.cNuPakPackageId,
   t.cNuPakPrefixId,
   t.cNuPakPackageName,
   t.cNuPakPackageState,
   t.ustamp,
   t.datestamp,
   t.json_object,
   p.cNuPakPrefixName
FROM ta_NuPakPackage t
JOIN ta_NuPakPrefix p ON p.cNuPakPrefixId=t.cNuPakPrefixId;
