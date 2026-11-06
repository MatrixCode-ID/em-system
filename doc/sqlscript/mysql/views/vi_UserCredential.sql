-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW `vi_UserCredential` AS
SELECT
   a.cCredentialId
   , a.cUserId
   , a.cCredentialType
   , a.cCredentialState
   , a.cCredentialKey
   , a.cCredentialSecret
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM ta_UserCredential AS a;
