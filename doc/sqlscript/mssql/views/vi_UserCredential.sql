DROP VIEW IF EXISTS vi_UserCredential;
GO
CREATE VIEW vi_UserCredential AS
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
FROM ta_UserCredential AS a
GO

SELECT TOP 100
   a.cCredentialId
   , a.cUserId
   , a.cCredentialType
   , a.cCredentialState
   , a.cCredentialKey
   , a.cCredentialSecret
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM vi_UserCredential AS a
