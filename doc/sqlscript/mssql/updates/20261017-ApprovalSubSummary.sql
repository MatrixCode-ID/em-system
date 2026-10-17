-- Adds the optional second summary line of approval requests. Keeps every existing row (sub-summary stays
-- null, so the request shows its summary on one line); safe to run again. Re-run views/vi_ApprovalRequestStatus.sql
-- afterward so the view exposes the new column.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.ta_ApprovalRequest', N'U') IS NULL
   THROW 51000, 'Install approval v2 before applying the sub-summary update.', 1;

IF COL_LENGTH(N'dbo.ta_ApprovalRequest', N'cSubSummary') IS NULL
   ALTER TABLE [dbo].[ta_ApprovalRequest] ADD [cSubSummary] nvarchar(400) NULL;
GO
