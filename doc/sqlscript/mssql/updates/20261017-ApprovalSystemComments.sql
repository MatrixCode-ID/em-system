-- Allow authenticated built-in accounts to author approval comments without creating user records.
-- Keeps request/comment relationships and all existing rows; safe to run again.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.ta_ApprovalRequestComment', N'U') IS NULL
   THROW 51000, 'Install approval v2 before applying the system-comment update.', 1;

BEGIN TRANSACTION;
DECLARE @drop nvarchar(max) = N'';
SELECT @drop += N'ALTER TABLE [dbo].[ta_ApprovalRequestComment] DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
FROM sys.foreign_keys AS fk
WHERE fk.parent_object_id = OBJECT_ID(N'dbo.ta_ApprovalRequestComment')
  AND fk.referenced_object_id = OBJECT_ID(N'dbo.ta_User')
  AND EXISTS (
     SELECT 1 FROM sys.foreign_key_columns AS fc
     WHERE fc.constraint_object_id = fk.object_id
       AND COL_NAME(fc.parent_object_id, fc.parent_column_id) = N'cUserId'
  );
IF @drop <> N'' EXEC sys.sp_executesql @drop;
COMMIT;
