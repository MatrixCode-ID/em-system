-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW `vi_NuPakPrefix` AS
SELECT
   t.cNuPakFeedId,
   t.cNuPakPrefixId,
   t.cNuPakPrefixName,
   t.cNuPakPrefixState,
   t.cNuPakPrefixDescription,
   t.ustamp,
   t.datestamp,
   t.json_object
FROM ta_NuPakPrefix t;
