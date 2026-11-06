-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW `vi_NuPakVersion` AS
SELECT
   t.cNuPakVersionId,
   t.cNuPakPackageId,
   t.cNuPakVersionNumber,
   t.cNuPakVersionOriginal,
   t.cNuPakVersionPrerelease,
   t.cNuPakVersionState,
   t.cNuPakVersionRecycledAt,
   t.cNuPakVersionSize,
   t.cNuPakVersionHash,
   t.cNuPakVersionTitle,
   t.cNuPakVersionDescription,
   t.cNuPakVersionAuthors,
   t.cNuPakVersionTags,
   t.cNuPakVersionPushedBy_cRobotId,
   t.ustamp,
   t.datestamp,
   t.json_object
FROM ta_NuPakVersion t;
