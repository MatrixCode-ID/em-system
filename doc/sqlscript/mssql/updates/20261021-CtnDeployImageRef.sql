-- Adds how a Stack deploy writes the image into the compose file (1 = image variable, 2 = image name with tag and
-- digest, 3 = image name with tag only). Existing targets keep NULL, which the API reads as 1, so they go on using
-- their image variable; safe to run again. Run before starting an API that has this column in its model.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.ta_CtnDeploy', N'U') IS NULL
   THROW 51000, 'Install the registry deploy tables (updates/20261007-CtnDeploy.sql) before this update.', 1;

IF COL_LENGTH(N'dbo.ta_CtnDeploy', N'cCtnDeployImageRef') IS NULL
   ALTER TABLE [dbo].[ta_CtnDeploy] ADD [cCtnDeployImageRef] int NULL;
GO
