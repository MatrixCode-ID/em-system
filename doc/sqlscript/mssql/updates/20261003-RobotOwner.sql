-- Optional ownership metadata; robot grants remain independent of user permissions.
-- Prerequisites: ta_User and ta_Robot (run RobotUserManager migration first if needed).
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.ta_Robot', N'cRobotOwner_cUserId') IS NULL
   ALTER TABLE dbo.ta_Robot ADD cRobotOwner_cUserId char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NULL;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_Robot_Owner' AND parent_object_id = OBJECT_ID(N'dbo.ta_Robot'))
   EXEC(N'ALTER TABLE dbo.ta_Robot WITH CHECK ADD CONSTRAINT FK_ta_Robot_Owner
      FOREIGN KEY (cRobotOwner_cUserId) REFERENCES dbo.ta_User(cUserId) ON DELETE SET NULL;');
COMMIT TRANSACTION;
