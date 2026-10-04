-- Rename robot tables and columns, preserving data, tokens and foreign keys.
-- Databases already renamed to the current schema are unchanged.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.ta_CtnRobot', N'U') IS NOT NULL
BEGIN
   IF OBJECT_ID(N'dbo.ta_Robot', N'U') IS NOT NULL
      THROW 51000, 'Both robot tables exist; reconcile them before migration.', 1;
   EXEC sys.sp_rename N'dbo.ta_CtnRobot', N'ta_Robot';
END;
IF OBJECT_ID(N'dbo.ta_CtnRobotRoot', N'U') IS NOT NULL
BEGIN
   IF OBJECT_ID(N'dbo.ta_CtnRootRobot', N'U') IS NOT NULL
      THROW 51000, 'Both container access tables exist; reconcile them before migration.', 1;
   EXEC sys.sp_rename N'dbo.ta_CtnRobotRoot', N'ta_CtnRootRobot';
END;
-- Identity columns and all existing foreign-key references.
DECLARE @columns TABLE (TableName sysname, OldName sysname, NewName sysname);
INSERT @columns VALUES
('ta_Robot','cCtnRobotId','cRobotId'),
('ta_Robot','cCtnRobotName','cRobotName'),
('ta_Robot','cCtnRobotState','cRobotState'),
('ta_Robot','cCtnRobotDescription','cRobotDescription'),
('ta_Robot','cCtnRobotTokenHash','cRobotTokenHash'),
('ta_Robot','cCtnRobotTokenPrefix','cRobotTokenPrefix'),
('ta_Robot','cCtnRobotTokenExpiry','cRobotTokenExpiry'),
('ta_Robot','cCtnRobotTokenLastUsed','cRobotTokenLastUsed'),
('ta_CtnRootRobot','cCtnRobotId','cRobotId'),
('ta_CtnUpload','cCtnRobotId','cRobotId'),
('ta_CtnManifest','cCtnManifestPushedBy_cCtnRobotId','cCtnManifestPushedBy_cRobotId');
DECLARE @table sysname, @old sysname, @new sysname, @qualified nvarchar(776);
DECLARE rename_columns CURSOR LOCAL FAST_FORWARD FOR SELECT TableName,OldName,NewName FROM @columns;
OPEN rename_columns;
FETCH NEXT FROM rename_columns INTO @table,@old,@new;
WHILE @@FETCH_STATUS = 0
BEGIN
   IF COL_LENGTH(N'dbo.' + @table,@old) IS NOT NULL
   BEGIN
      IF COL_LENGTH(N'dbo.' + @table,@new) IS NOT NULL
         THROW 51000, 'Both old and new robot columns exist; reconcile before migration.', 1;
      SET @qualified = N'dbo.' + QUOTENAME(@table) + N'.' + QUOTENAME(@old);
      EXEC sys.sp_rename @qualified,@new,N'COLUMN';
   END;
   FETCH NEXT FROM rename_columns INTO @table,@old,@new;
END;
CLOSE rename_columns;
DEALLOCATE rename_columns;

-- SQL Server enforces the access CHECK dependency: recreate it after column rename.
IF COL_LENGTH(N'dbo.ta_CtnRootRobot',N'cCtnRobotRootAccess') IS NOT NULL
BEGIN
   IF COL_LENGTH(N'dbo.ta_CtnRootRobot',N'cCtnRootRobotAccess') IS NOT NULL
      THROW 51000, 'Both old and new access columns exist.', 1;
   DECLARE @checkName sysname, @checkDefinition nvarchar(max), @sql nvarchar(max);
   SELECT @checkName = name, @checkDefinition = definition FROM sys.check_constraints
   WHERE parent_object_id = OBJECT_ID(N'dbo.ta_CtnRootRobot') AND definition LIKE N'%cCtnRobotRootAccess%';
   IF @checkName IS NOT NULL
   BEGIN
      SET @sql = N'ALTER TABLE dbo.ta_CtnRootRobot DROP CONSTRAINT ' + QUOTENAME(@checkName);
      EXEC sys.sp_executesql @sql;
   END;
   EXEC sys.sp_rename N'dbo.ta_CtnRootRobot.cCtnRobotRootAccess',N'cCtnRootRobotAccess',N'COLUMN';
   IF @checkName IS NOT NULL
   BEGIN
      SET @sql = N'ALTER TABLE dbo.ta_CtnRootRobot WITH CHECK ADD CONSTRAINT ' + QUOTENAME(@checkName)
         + N' CHECK ' + REPLACE(@checkDefinition,N'cCtnRobotRootAccess',N'cCtnRootRobotAccess');
      EXEC sys.sp_executesql @sql;
   END;
END;
COMMIT TRANSACTION;

END TRY
BEGIN CATCH
   IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
   THROW;
END CATCH;
